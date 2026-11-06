# AgentOS 本地开发说明

版本：AOS-001 基线

## 1. 环境要求

| 组件 | 版本 | 说明 |
|---|---|---|
| .NET SDK | 10.0.x | 后端使用 ASP.NET Core Minimal API + EF Core |
| SQL Server | 2019+（本机为 2025） | 开发库 `AgentOS_Dev` |
| Node.js / npm | Node 24 / npm 11 | 前端 Vite + Vue 3 (JavaScript) |
| Git | 2.39+ | 仓库根目录 `E:\Projects\AgentOS` |

前端约束：**不使用 TypeScript**。不要创建 `.ts`/`.tsx`，不要在 `.vue` 中使用 `lang="ts"`。

## 2. 数据库

默认使用 Windows 身份连接本机默认实例，开发库为 `AgentOS_Dev`：

```text
Server=lpc:localhost;Database=AgentOS_Dev;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3
```

说明：

- 本机 SQL Server 的 TCP 1433 与命名管道当前是关闭的，只开放共享内存；`lpc:localhost` 显式走共享内存。
- 如果换到 TCP 已开放的实例，把 `Server` 改成 `localhost,1433` 即可。
- 该连接串位于 `backend/AgentOS.Api/appsettings.Development.json`；基础 `appsettings.json`
  不再内置连接串，非 Development 环境缺少 `ConnectionStrings__AgentOS` 时会在启动时快速失败。
- 不要在代码或提交里写 SQL 密码；本地开发使用 Windows 身份或 User Secrets / 环境变量。
- 本地开发使用 HTTP；非 Development 环境启用 HTTPS 重定向，生产环境建议由反向代理终止 TLS。

创建/更新数据库：

```powershell
dotnet ef database update --project backend/AgentOS.Infrastructure --startup-project backend/AgentOS.Api
```

AOS-001 的 `InitialCreate` 是空迁移，只创建 `__EFMigrationsHistory`；业务表从 AOS-002 开始。

## 3. 启动后端

```powershell
dotnet run --project backend/AgentOS.Api
```

默认地址：`http://localhost:5000`

健康检查：

- `GET /health/live`：进程存活，不依赖数据库。
- `GET /health/ready`：包含 SQL Server 连通性；数据库不可达时返回 503。

> 注意：如果通过 DSH（DeepSeek Harness）的受限子进程启动 API，进程可能是 **Low 完整性级别**，访问 SQL Server 命名管道会被拒绝（Win32 5）。请在普通 PowerShell（Medium 完整性）中启动 API，或从 DSH 之外的终端运行。构建、EF 迁移和前端命令不受影响。

## 4. 启动前端

```powershell
cd frontend/agentos-web
npm install
npm run dev
```

默认地址：`http://localhost:5173`。Vite 已把 `/api` 和 `/health` 代理到 `http://localhost:5000`，Dashboard 会显示 API Live / Ready 状态。

其他命令：

```powershell
npm run lint
npm run build
```

## 5. 测试与构建

```powershell
dotnet build backend/AgentOS.slnx
dotnet test backend/AgentOS.slnx
```

> **已知环境问题（2026-11-06 定位）**：在 DSH 沙箱工作区（`E:\Projects\AgentOS`）**内部**运行
> `dotnet test` 会在 testhost 启动阶段中止：`Win32Exception (5)` at `ProcessManager.OpenProcess`
> → `SetParentProcessExitCallback`。根因是工作区目录上的沙箱完整性标签与 ACL
> （`Low Mandatory Level`、`CodexSandboxUsers`、`S-1-4-*` 授权），不是代码或测试问题——
> 把 `tests/AgentOS.Api.Tests/bin/Debug/net10.0` 原样复制到工作区外（如 `%TEMP%`）再执行
> `dotnet vstest <copy>\AgentOS.Api.Tests.dll`，77 项全部正常（71 通过 / 6 SQL 跳过）。
>
> 规避方式：在 CI 或工作区外的路径运行测试；`dotnet build`、`dotnet ef` 不受影响。
> 不要把测试输出放在工作区内再运行。

前端：

```powershell
cd frontend/agentos-web
npm run lint
npm run build
```

## 6. 配置

- 后端：连接串在 `backend/AgentOS.Api/appsettings.Development.json`（无密码），可用环境变量
  `ConnectionStrings__AgentOS` 覆盖。
- 开发 API Key 不提交到 Git；通过 User Secrets 或环境变量配置（见第 8 节）。
- 前端：`frontend/agentos-web/.env.example` 中的 `VITE_API_BASE_URL`，默认留空走 Vite 代理。
- 根目录 `.env.example` 只列非敏感变量；不要提交真实密钥、连接密码或 PAT。

## 7. CI

`.github/workflows/ci.yml` 包含：

- 后端：`dotnet build` + `dotnet test`，并启动 **SQL Server 2022 service container**；
- 前端：`npm ci` + `npm run lint` + `npm run build`；
- 禁 TypeScript 门禁：拒绝 `.ts`、`.tsx` 和 `lang="ts"`。

后端 job 的 `sqlserver` service 监听 `localhost:1433`，工作流把连接串放进
`AGENTOS_SQL_TEST_CONNECTION`，因此 **SQL 集成测试在 CI 中真实执行**（仓储读写、唯一索引、
并发提交、升级迁移、历史行回填都会跑）。容器密码优先取仓库 secret `MSSQL_SA_PASSWORD`，
未配置时使用只在容器内有效的固定值；数据库 `AgentOS_Test` 随 job 丢弃。

本地仍默认跳过 SQL 用例（见 §8.5），需要时显式设置 `AGENTOS_SQL_TEST_CONNECTION`。

## 8. AOS-002 Event Hub

### 8.1 端点

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/v1/events` | 接收标准 Envelope，成功返回 202 |
| GET | `/api/v1/events/{eventId}` | 查询事件详情（含 Payload） |
| GET | `/api/v1/events` | 分页/过滤：`page`、`pageSize`（最大 100）、`source`、`type`、`status`、`project` |

状态码：首次接收 202；重复事件 200（`duplicate: true`）；去重键冲突 409（ProblemDetails，
`code = event.dedupe.conflict`）；校验失败 400（ProblemDetails）；未授权 401；不存在 404；
请求体超限 413。

### 8.2 开发认证

开发环境使用标头 `X-AgentOS-Dev-Key`。密钥不提交到 Git，二选一配置：

```powershell
# 方式一：User Secrets
dotnet user-secrets --project backend/AgentOS.Api set "Authentication:DevelopmentApiKey" "your-local-key"

# 方式二：当前 shell 环境变量
$env:Authentication__DevelopmentApiKey = "your-local-key"
```

然后请求：

```powershell
curl.exe -X POST http://localhost:5000/api/v1/events `
  -H "Content-Type: application/json" `
  -H "X-AgentOS-Dev-Key: your-local-key" `
  --data-binary "@event.json"
```

未配置密钥时开发认证 fail-closed（401）；非 Development 环境统一返回 401，直到 AOS-006 实现
真正的 API Client 凭证。

### 8.3 Projects 初始化

带 `project` 的事件要求 `Projects` 表中存在且启用的项目。AOS-002 还没有项目管理 API，本地开发
可直接插入：

```sql
IF NOT EXISTS (SELECT 1 FROM Projects WHERE Slug = '3derp')
    INSERT INTO Projects (Id, Name, Slug, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
    VALUES (NEWID(), '3DERP', '3derp', 1, SYSUTCDATETIME(), SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Projects WHERE Slug = 'disabled-demo')
    INSERT INTO Projects (Id, Name, Slug, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
    VALUES (NEWID(), 'Disabled Demo', 'disabled-demo', 0, SYSUTCDATETIME(), SYSUTCDATETIME());
```

### 8.4 请求体限制

`Events:MaxRequestBodyBytes` 默认 `262144`（256 KB），可用配置或环境变量
`Events__MaxRequestBodyBytes` 覆盖；Kestrel 与端点过滤器共用该值。

### 8.5 SQL 集成测试

SQL 仓储测试默认跳过。设置一次性测试库连接后执行：

```powershell
$env:AGENTOS_SQL_TEST_CONNECTION = "Server=lpc:localhost;Database=AgentOS_Test;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3"
dotnet test backend/AgentOS.slnx --filter FullyQualifiedName~Sql
```

该测试会执行 `Database.MigrateAsync()`，验证迁移、`ISJSON` 约束、仓储读写和分页过滤。

注意：`AGENTOS_SQL_TEST_CONNECTION` 必须指向**可丢弃的测试库**。AOS-003 的
`SqlEventDeduplicationTests` 会把库回退到 AOS-002 迁移、写入历史重复数据，再升级到最新
迁移以验证升级路径；不要指向 `AgentOS_Dev` 或任何生产库。两个 SQL 测试类通过
`[Collection("SqlServer")]` 串行执行，避免互相干扰。

CI 的后端 job 已提供 SQL Server 容器并自动设置该变量，因此上述 SQL 用例在流水线中会真实执行。

### 8.6 AOS-003 去重与幂等

- 数据库唯一索引 `UX_Events_Source_DedupeKey (Source, DedupeKey)` 是最终防线；应用层
  不采用“先查询再插入”。
- 首次接收：202；内容一致的重复提交：200 + `duplicate: true` + 原 `eventId`；
  同键不同 payload：409，原事件不被覆盖。
- `PayloadHash` 使用固定策略 `agentos.payload-canonical-json/v1`（对象键排序、数组保序、
  数字保留字面形式、UTF-8 最小转义），详见 `docs/EVENT-SPECIFICATION.md` §5.2。
- 历史数据：空 DedupeKey 改写为 `legacy-{Id}`；同键重复行最早一条保留原键，其余改写为
  `{原键}~legacy-dup-{Id}`；`PayloadHash` 在首次命中时惰性回填。
- 重复/冲突日志按“去重键 + 类别”分别采样（60 秒窗口，首条必记，其余每 100 条记一条，
  最多跟踪 4096 个键）；冲突有独立计数，窗口内第一条 409 必定记录。日志不记录 payload 内容。
- 任务幂等键预留：`EventId + RuleId + RuleVersion + DispatchKey`，由
  `AgentOS.Domain.Tasks.LogicalTaskKey` 统一生成，AOS-004 创建 Task 时使用。
