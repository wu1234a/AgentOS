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
- 历史数据：空 DedupeKey（按存储长度判断）改写为 `legacy-{Id}`；同键重复行最早一条保留原键，
  其余改写为 `{原键}~legacy-dup-{Id}`；原键过长（>176 个 UTF-16 单元）改用
  `legacy-dup-{Id}`，改名后仍与既有键冲突的再加 8 位随机后缀。`PayloadHash` 在首次命中时惰性回填。
- 重复/冲突日志按“去重键 + 类别”分别采样（60 秒窗口，首条必记，其余每 100 条记一条，
  最多跟踪 4096 个键）；冲突有独立计数，窗口内第一条 409 必定记录。日志不记录 payload 内容。
- 任务幂等键：`EventId + RuleId + RuleVersion + DispatchKey`，由
  `AgentOS.Domain.Tasks.LogicalTaskKey` 统一生成（AOS-004 已使用）。

## 9. AOS-004 Rules Engine

### 9.1 数据模型

新增表：`ModelProfiles`、`Skills`、`Agents`、`AutomationRules`、`Tasks`（迁移
`20261106082553_AddRulesEngine`）。关键约束：

- `Tasks.LogicalTaskKey` 唯一：同一 Event + RuleId + RuleVersion + DispatchKey 只会产生一条任务；
- `Tasks.TaskCode` 唯一；`Tasks.RowVersion` 供 AOS-005 的领取/租约使用；
- `AutomationRules` 的 `MatchJson` / `DispatchJson` 有 `ISJSON` 检查约束；
- `Agents` 的 Code 在项目内唯一，全局 Agent 单独唯一；
- `Skills` 的 `(ProjectId, Code, Version)` 唯一（项目范围）。

### 9.2 规则匹配

- `SourcePattern` / `EventTypePattern` 支持 `*` 通配（大小写不敏感，整串匹配）。
- `ProjectId` 为空 = 全局规则；否则只匹配该项目的事件。
- `matchJson` 只支持声明式条件：`subjectTypes`、`priorities`（未知字段会被拒绝）。
- 匹配顺序固定：**项目规则优先 → priority 降序 → CreatedAtUtc 升序 → Id 升序**。
- `dispatchJson` 支持 `dispatchKey`（默认 `default`）与 `priority`（任务优先级）。

### 9.3 事件到任务

1. 事件首次持久化后，Rules Engine 读取启用的规则（Agent/Skill/ModelProfile 必须启用）。
2. 命中的每条规则生成一条 `Queued` 任务，记录 `RuleId` 与 `RuleVersion`。
3. 任务与事件状态（`Processed` / `Ignored`）在同一事务提交。
4. 重复投递的事件若仍处于 `Received`，会幂等重试派发；已派发的事件不会再生成任务。
5. 没有项目归属的事件无法生成任务，规则会被跳过并记录警告。

### 9.4 管理 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/rules` | 列表（`page`、`pageSize`、`enabled`、`project`） |
| GET | `/api/v1/rules/{id}` | 详情（含 matchJson / dispatchJson） |
| POST | `/api/v1/rules` | 创建，返回 201 |
| PUT | `/api/v1/rules/{id}` | 更新（Version +1） |
| POST | `/api/v1/rules/{id}/enable` / `disable` | 启用（重新校验引用）/ 禁用 |
| POST | `/api/v1/rules/validate` | 只校验不保存 |

所有规则端点都要求 `X-AgentOS-Dev-Key`（与事件端点相同）。启用引用已禁用的
Agent/Skill/ModelProfile 会返回 400 字段错误。

### 9.5 引用数据初始化

规则需要 Agent / Skill / ModelProfile 存在且启用。AOS-004 没有提供这些资源的管理 API，
本地开发用 SQL 初始化（示例）：

```sql
DECLARE @ProjectId uniqueidentifier = (SELECT Id FROM Projects WHERE Slug = '3derp');
DECLARE @ProfileId uniqueidentifier = NEWID();

INSERT INTO ModelProfiles (Id, Code, Provider, Model, ExecutionMode, ConfigurationJson, TimeoutSeconds, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
VALUES (@ProfileId, 'strong-reasoning', 'codex-cli', 'gpt-6.1-sol', 'cli', '{}', 1800, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

INSERT INTO Skills (Id, ProjectId, Code, Version, RelativePath, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
VALUES (NEWID(), @ProjectId, '3derp-plan-development', '1.0', 'skills/projects/3derp/plan-development/SKILL.md', 1, SYSUTCDATETIME(), SYSUTCDATETIME());

INSERT INTO Agents (Id, ProjectId, Code, DisplayName, Role, DefaultModelProfileId, PermissionProfile, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
VALUES (NEWID(), @ProjectId, 'planner', 'Planner', 'planner', @ProfileId, 'read-only', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
```

### 9.6 前端

`/rules` 列表页（筛选、新建/编辑对话框、启用/禁用）与 `/rules/:id` 详情页。本地开发需在
`frontend/agentos-web/.env.local` 设置 `VITE_DEV_API_KEY`，前端会带上
`X-AgentOS-Dev-Key` 请求头。

### 9.7 测试

```powershell
$env:AGENTOS_SQL_TEST_CONNECTION = "Server=lpc:localhost;Database=AgentOS_Test;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3"
dotnet test backend/AgentOS.slnx --filter FullyQualifiedName~Rule
```

覆盖：通配匹配与顺序确定性、JSON 校验、禁用引用、规则 CRUD/版本号、任务派发幂等、
`LogicalTaskKey` 唯一索引、Agent 禁用时跳过、事件状态 `Processed`/`Ignored`。

## 10. Codex 审查与 Windows 沙箱排障

### 10.1 症状

在本机用 Codex 的只读/工作区沙箱执行任何命令都会失败：

```text
windows sandbox failed: CreateProcessAsUserW failed: 5 (拒绝访问。)
```

同一命令用 `-s danger-full-access` 可以执行，说明问题出在 Codex 的 Windows 沙箱
（受限令牌）而不是仓库或命令本身。

### 10.2 根因

Codex 在 Windows 上优先使用 `pwsh.exe`；本机 PATH 里的 `pwsh` 是 **Microsoft Store 版**
（`%LOCALAPPDATA%\Microsoft\WindowsApps\...\pwsh.exe`，AppExecutionAlias）。受限令牌无法
启动打包应用别名，于是 `CreateProcessAsUserW` 在命令真正执行前就返回 access denied。
这与 openai/codex#39276、#35871 描述的场景一致。

### 10.3 本机修复

`%APPDATA%\npm\codex.cmd`（Codex 的启动垫片）在启动 `codex.exe` 之前，把 `WindowsApps`
从**该进程**的 PATH 中移除，Codex 于是回退到桌面版 `powershell.exe`：

```bat
for /f "usebackq delims=" %%i in (`powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "[string]::Join(';', ($env:PATH -split ';' | Where-Object { $_ -and $_ -notmatch 'WindowsApps' }))"`) do set "PATH=%%i"
```

验证：

```powershell
codex exec -s read-only "Run exactly: git rev-parse --short HEAD. Reply with only the raw stdout."
```

修复只影响 `codex` 及其子进程，不改变普通终端里的 `pwsh`。如果以后卸载/安装 Store 版
PowerShell 或 PATH 变化，重新检查这一节。

### 10.4 网络与 MCP

- Codex 访问模型 API 需要代理：`$env:HTTPS_PROXY='http://127.0.0.1:7897'`（本机 Clash）。
- 沙箱修好后，审查通过**本地 git** 读取仓库，不再依赖 GitHub MCP；即使
  `chatgpt.com/backend-api/ps/mcp` 断连也不影响 `codex exec review`。

### 10.5 常用命令

```powershell
$env:HTTPS_PROXY='http://127.0.0.1:7897'
codex exec review --commit <sha>          # 审查某个提交（含修复）
codex exec -s read-only "<问题>"           # 只读问答
codex exec -s workspace-write "<任务>"     # 允许写工作区
```

> 本机仍需注意：`dotnet test` 受 DSH 工作区沙箱限制（见 §5），与 Codex 的沙箱问题相互独立。

## 11. AOS-005 任务队列与租约

### 11.1 数据与配置

新增 `Workers`、`TaskAttempts` 表，并给 `Tasks` 增加租约字段（迁移
`20261106144413_AddTaskQueue`）。队列参数在 `TaskQueue` 配置节：

| 键 | 默认 | 说明 |
|---|---|---|
| `LeaseDurationSeconds` | 90 | 每次领取/心跳后的租约时长 |
| `HeartbeatIntervalSeconds` | 20 | Runner 心跳建议间隔 |
| `TaskTimeoutSeconds` | 1800 | 任务超时上限（记录在任务上） |
| `MaxAttempts` | 2 | 规则生成任务时的默认最大尝试次数 |
| `BackoffBaseSeconds` / `BackoffMaxSeconds` | 30 / 900 | 指数退避基数和上限 |
| `ClaimBatchLimit` | 10 | 单次领取上限（API 再限制为 1–100） |
| `RecoveryIntervalSeconds` | 30 | Worker 扫描过期租约的间隔 |

### 11.2 Runner API

所有 Runner 端点同时要求 `X-AgentOS-Dev-Key` 和 `X-AgentOS-Worker-Code`
（指向 `Workers` 中 Online 的 Worker）。AOS-006 会用真正的 Runner 凭证替换 Worker Code。

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/v1/runners/claim` | `{ "capacity": 5 }`，返回任务与**一次性**租约 Token |
| POST | `/api/v1/runners/tasks/{id}/start` | `{ attemptId, leaseToken, externalSessionId? }`，记录会话并续租 |
| POST | `/api/v1/runners/tasks/{id}/heartbeat` | `{ attemptId, leaseToken }`，续租并返回 `cancelRequested` |
| POST | `/api/v1/runners/tasks/{id}/complete` | `{ attemptId, leaseToken, exitCode? }`，标记 Succeeded |
| POST | `/api/v1/runners/tasks/{id}/fail` | `{ attemptId, leaseToken, errorCode, errorMessage, retryable, cancelled }` |
| POST | `/api/v1/tasks/{id}/cancel` | 管理端取消（Queued 立即取消，Running 置取消请求） |

错误语义：`404` 任务不存在；`403` Worker/Token 不匹配或租约过期；`409` 状态不允许该操作
（例如已请求取消的任务不能再 Complete），响应扩展字段 `code` 为稳定错误码。

### 11.3 租约安全

- Token 为 256 位随机值（Base64Url），只在 Claim 响应中返回一次。
- 数据库只保存 SHA-256 哈希；比较使用恒定时间。
- 所有生命周期操作都校验 WorkerId + TaskId + AttemptId + Token + 租约未过期，不能只凭 TaskId。

### 11.4 本地初始化 Runner

队列没有注册 API（AOS-006），本地可直接插入一个 Worker：

```sql
INSERT INTO Workers (Id, WorkerCode, DisplayName, HostName, Platform, Architecture, Version, CapabilitiesJson, Status, LastHeartbeatAtUtc, RegisteredAtUtc, UpdatedAtUtc)
VALUES (NEWID(), 'local-runner', 'Local Runner', 'DESKTOP', 'Windows', 'x64', '1.0.0', '{}', 'Online', SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME());
```

### 11.5 后台恢复（AgentOS.Worker）

```powershell
dotnet run --project backend/AgentOS.Worker
```

`LeaseRecoveryWorker` 每 `RecoveryIntervalSeconds` 扫描一次：租约过期的 Running 任务，其
Attempt 置 `Lost`；已请求取消的任务直接落为 `Cancelled`（不会重新入队）；还有次数则进入
`RetryWait`（指数退避 + 0–29 秒由 TaskId 哈希派生的抖动），否则 `Failed`。
Worker 需要连接串，Development 已在 `backend/AgentOS.Worker/appsettings.Development.json` 配置。

### 11.6 测试

```powershell
$env:AGENTOS_SQL_TEST_CONNECTION = "Server=lpc:localhost;Database=AgentOS_Test;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3"
dotnet test backend/AgentOS.slnx --filter FullyQualifiedName~TaskQueue
```

SQL 用例会并发领取（4 个 Runner × 容量 4）、验证无重复领取、Attempt 一致性、优先级顺序、
租约续期/过期拒绝、失败重试与最大次数、过期租约恢复与二次领取。

## 12. AOS-006 本地 Runner

### 12.1 前置条件

- .NET 10 SDK；AgentOS API 与 SQL Server 已启动。
- 服务端配置一次性注册密钥（不要提交到 Git）：

```powershell
$env:Runner__RegistrationKey = '<random-bootstrap-key>'
dotnet run --project backend/AgentOS.Api
```

也可以用已存在的 Runner 客户端凭证（`Runner__ClientId` / `Runner__ClientSecret`）跳过注册。

### 12.2 配置

复制 `backend/AgentOS.Runner/appsettings.example.json` 为 `appsettings.json`，至少配置：

- `BaseUrl`：API 地址（只发出站请求）；
- `WorkerCode`、`DisplayName`、`Platform`、`Architecture`、`Version`；
- `WorkspaceRoots`：允许的工作区根目录；
- `Projects`：项目 slug → 工作区路径（不在列表中的项目一律拒绝执行）；
- `PollIntervalSeconds`、`HeartbeatIntervalSeconds`、`MaxConcurrentTasks`；
- `RegistrationKey`（首次注册用，注册成功后会被轮换的 ClientSecret 取代）。

密钥优先用环境变量：`Runner__RegistrationKey`、`Runner__ClientId`、`Runner__ClientSecret`。

### 12.3 启动与停止

```powershell
dotnet run --project backend/AgentOS.Runner
# 或发布后
dotnet publish backend/AgentOS.Runner -c Release -o C:\AgentOS\runner
C:\AgentOS\runner\AgentOS.Runner.exe
```

- 控制台 `Ctrl+C` 触发优雅关闭：停止领取，最多等待 15 秒让运行中的 Handler 收尾，
  未完成任务以 `runner-shutdown`（可重试）回传。
- Runner 启动后先注册/心跳，再按 `PollIntervalSeconds` 轮询，并发不超过 `MaxConcurrentTasks`。
- API 不可用时指数退避（2s → 60s），不会把任务误标成功；租约到期由 Worker 恢复。

### 12.4 Windows 服务 / WSL2

- Windows 服务：用 `sc.exe create AgentOSRunner binPath= "...\AgentOS.Runner.exe" start= auto`
  或 NSSM；工作目录设为发布目录，Secrets 用机器级环境变量。AOS-006 不要求开机自启。
- WSL2：同一二进制可在 Linux 运行；`Platform` 改为 `Linux`，`WorkspaceRoots` 用
  `/mnt/d/...` 或 WSL 文件系统路径；注意大小写敏感与换行差异。

### 12.5 故障排查

| 现象 | 原因 / 处理 |
|---|---|
| 注册 401 | `X-AgentOS-Registration-Key` 与服务端 `Runner:RegistrationKey` 不一致 |
| 注册 503 | 服务端未配置注册密钥；改用预置 ClientId/Secret |
| 领取 401 | ClientSecret 已轮换/禁用；重启 Runner 重新注册，或核对 DevKey 回退 |
| `workspace-rejected` | 任务项目不在 `Projects` 白名单，或路径不在 `WorkspaceRoots` 内 / 是符号链接 |
| Runner 显示离线 | Worker 心跳失败；检查网络、API 日志与 `HeartbeatIntervalSeconds` |
| 任务一直 Running | Runner 崩溃或断网；租约到期后由 `AgentOS.Worker` 恢复（AOS-005） |
| 日志缺失 | 日志上传是尽力而为；检查 `TaskLogs` 与 API 的 403（租约失效） |

### 12.6 安全

- Runner 只使用 Runner Scope 的客户端凭证；Secret 只存哈希，轮换后旧 Secret 立即失效。
- `RegistrationKey` 仅用于首次绑定，不应长期保留在配置中。
- Handler 只能访问校验通过的工作区；路径穿越、符号链接逃逸与未授权项目都会被拒绝。

## 13. AOS-007 Codex CLI Adapter

### 13.1 已验证版本与参数

- 验证环境：Windows 11，`codex-cli 0.166.0-alpha.6`（`codex --version`）。
- 非交互执行：`codex exec [OPTIONS] -`，Prompt 从 stdin 读取；
  `--json` 输出 JSONL（`thread.started.thread_id`、`item.completed.agent_message`），
  `-o <file>` 写入最后一条消息。
- Adapter 固定使用：`exec --sandbox <read-only|workspace-write> -C <workspace>
  --skip-git-repo-check --ephemeral --color never --json -o <artifact>/last-message.txt [-m model] -`。
- 明确禁止：`--dangerously-bypass-approvals-and-sandbox`、
  `--dangerously-bypass-hook-trust`、`--add-dir`、任意 `-c sandbox_permissions=...`。
- Windows 必须指向 `codex.exe`（`.cmd`/`.bat` shim 会被拒绝，避免引入 shell 解析）。

### 13.2 Runner 配置

```json
"Runner": { "AgentAdapter": "codex-cli", "...": "..." },
"Codex": {
  "ExecutablePath": "C:\\Users\\<user>\\AppData\\Local\\OpenAI\\Codex\\bin\\codex.exe",
  "SandboxMode": "read-only",
  "TimeoutSeconds": 900,
  "MaxOutputBytes": 2000000,
  "EnvironmentAllowList": [ "PATH", "USERPROFILE", "HTTPS_PROXY", "OPENAI_API_KEY", "..." ],
  "RedactEnvironmentVariables": [ "OPENAI_API_KEY" ]
}
```

- `read-only` 用于分析/评审；`workspace-write` 用于需要改文件的任务，仍然受 Runner
  的项目/工作区白名单约束。
- API Key 只通过子进程环境变量提供，不写入 Prompt、配置或日志。
- `AGENTOS_CODEX_SMOKE=1` 可运行真实 CLI 冒烟测试（默认跳过）。

### 13.3 故障排查

| 现象 | 处理 |
|---|---|
| `codex-not-found` | `Codex:ExecutablePath` 不存在；Windows 用 `codex.exe` 绝对路径 |
| `codex-unavailable` / start 失败 | 检查可执行权限、`.cmd` 被拒绝、PATH |
| `codex-timeout` | 增大 `Codex:TimeoutSeconds` 或任务超时；进程树已被终止 |
| 退出码非零 | 查看 `%TEMP%\agentos\codex\<task>\<attempt>\stderr.log` |
| 日志被 `[REDACTED]` | 正常脱敏；确认没有把 Secret 放进 Prompt |

### 13.4 安全边界

- Prompt 由可信模板 + 转义后的任务数据/Skill 内容组成；数据段不能关闭或覆盖安全规则。
- 只允许 `read-only` / `workspace-write`；沙箱模式来自受信配置。
- 工作目录必须通过 Runner 白名单；Adapter 不额外放宽路径。
- 超时/取消会终止整个进程树；输出按行脱敏后写 artifact 与日志。

## 14. AOS-008 可观测性

### 14.1 管理 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/dashboard/stats` | 事件/任务统计、成功率、平均等待与执行、在线 Runner、租约超时、最近失败 |
| GET | `/api/v1/events?page&pageSize` | 事件列表（沿用 AOS-002 契约） |
| GET | `/api/v1/events/{id}` | 事件详情与 payload |
| GET | `/api/v1/events/{id}/tasks` | 事件关联任务 |
| GET | `/api/v1/tasks?status&projectId&limit&offset` | 任务列表（分页） |
| GET | `/api/v1/tasks/{id}` | 任务详情：Attempts、最近日志、Artifacts、Usage |
| GET | `/api/v1/tasks/{id}/logs?level&afterSequence&limit` | 日志分页（limit ≤ 500） |
| GET | `/api/v1/tasks/{id}/attempts/{attemptId}/logs` | 单次 Attempt 日志 |
| GET | `/api/v1/workers` | Runner 状态与最后心跳 |
| GET | `/api/v1/artifacts/{id}/download` | 授权下载产物（需要 Dev Key） |

Runner 上传产物：`POST /api/v1/runners/tasks/{taskId}/artifacts?attemptId&name&mediaType`，
请求头 `X-AgentOS-Lease-Token`，body 为文件内容；media type 与大小受服务端限制。

### 14.2 配置

```json
"Artifacts": {
  "RootPath": "",
  "MaxBytes": 10485760,
  "AllowedMediaTypes": ["text/plain","text/markdown","application/json","application/octet-stream","image/png","image/jpeg"]
}
```

`RootPath` 为空时使用系统临时目录 `agentos-artifacts`，生产必须配置持久目录。文件按
`<project>/<task>/<attempt>/` 分层，文件名由服务端安全化，客户端不能指定存储路径。

### 14.3 前端与 SignalR

- 页面：Dashboard、Events、Event Detail、Tasks、Task Detail（Attempts/Logs/Artifacts/Usage）、Runners。
- `VITE_DEV_API_KEY` 同时用于 REST 与 SignalR（`/hubs/tasks?api_key=...`）。
- SignalR 只推送 `taskChanged` / `tasksClaimed`；断线重连后前端重新调用 REST 拉取，
  数据库始终是事实来源。
- 日志按 `afterSequence` 增量加载；UI 只渲染当前页，避免一次性加载全部日志。

### 14.4 日志与脱敏

- Runner 端按完整行脱敏后上传；服务端 `TaskLogSanitizer` 再做一次兜底（sk-*、Bearer、
  password/token/secret、Cookie）。
- 大段 stdout/stderr 通过 Artifact 保存，数据库只保留摘要与引用。
- 保留策略/自动清理尚未实现（见交付报告限制）。

### 14.5 审查修正

- 日志按 **Attempt** 分页（`(AttemptId, Sequence)` 唯一）；Task 详情页先选择 Attempt 再加载/增量拉取。
- Artifact 上传端点会把请求体上限提升到 `Artifacts:MaxBytes`，存储层仍做流式大小校验。
- Worker 列表按 2 分钟心跳新鲜度显示 Offline；Disabled 保持原状态。
- 前端把无时区的 SQL 时间按 UTC 解析后再本地化展示。

## 15. AOS-009 Agent 注册中心

### 15.1 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/agents?projectId&role&includeArchived&limit&offset` | 列表（默认排除归档） |
| GET | `/api/v1/agents/{id}` | 详情 |
| POST | `/api/v1/agents` | 创建（Code 不可变） |
| PUT | `/api/v1/agents/{id}` | 更新可变字段 |
| POST | `/api/v1/agents/{id}/enable` / `disable` | 启用/禁用（幂等） |
| POST | `/api/v1/agents/{id}/archive` | 归档（有未终态任务时 409） |

### 15.2 校验与生命周期

- Code 小写 slug（3–80），项目内/全局唯一；创建后不可修改。
- Role：planner / coder / reviewer / qa；PermissionProfile：read-only / workspace-write。
- SystemPromptPath 只允许相对路径，禁止根路径与 `..`；实际文件读取留给后续任务。
- Project/DefaultModelProfile 必须存在且启用。
- 归档终态；禁用 Agent 后规则校验会拒绝新任务，已运行任务继续。

### 15.3 前端

`/agents` 页面支持列表、角色/归档过滤、创建/编辑、启用/禁用/归档。项目级 Agent
需通过 API 指定 ProjectId（当前 UI 只创建全局 Agent）。

## 16. AOS-010 Model Router / Provider Profiles

### 16.1 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/model-profiles?enabled&limit&offset` | 列表 |
| GET/POST | `/api/v1/model-profiles` / `/{id}` | 详情/创建 |
| PUT | `/api/v1/model-profiles/{id}` | 更新（Code 不可变） |
| POST | `/api/v1/model-profiles/{id}/enable\|disable` | 启停 |
| GET | `/api/v1/model-profiles/resolve?taskId&ruleId&agentId` | 解析有效配置 |

### 16.2 规则与安全

- 解析顺序 Task → Rule → Agent；禁用 Profile 自动跳过，全部不可用返回
  `404 model-profile-unresolved`。
- `SecretReference` 必须是环境变量名（`^[A-Z][A-Z0-9_]{2,80}$`）；`ConfigurationJson`
  必须是 JSON 对象且不含密钥字段。
- `TimeoutSeconds` 1–7200；`MaxCostPerTask` ≥ 0。
- 管理 API 需要开发 API Key；前端 `/models` 页面提供 CRUD 与启停。

### 16.3 Runner 集成边界

AOS-010 只提供注册表与解析 API；Claim/Adapter 尚未自动使用解析结果（AOS-010b 将把
Model/Provider/ExecutionMode 下发到 Runner，并传给 Codex `-m`）。

## 17. 模型接入决策记录

- 当前工作流保持不变：任务文件由 Agent 按 ROADMAP/架构起草，实现后由 Codex 独立审查。
- **Kimi 在 AOS-012（Planner Agent）阶段接入**，用于 Planner/任务草案生成；草案必须人工批准，
  不自动进入执行队列。
- 前置条件：
  1. AOS-010b：Claim 携带解析后的 Model/Provider/ExecutionMode/Timeout，Runner 下发给 Adapter；
  2. 通用 `ApiAgentAdapter`（OpenAI 兼容接口），不把 Kimi 参数写进 Codex CLI Adapter；
  3. 密钥仅通过环境变量引用（`SecretReference`），配置中不得出现明文。
- 编码执行（AOS-013 之后）是否使用 Kimi 再单独评估；优先从只读分析/测试补充/文档任务开始。

## 18. AOS-011 Skill 管理与版本

### 18.1 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/skills?projectId&code&includeDisabled&limit&offset` | 列表 |
| GET | `/api/v1/skills/{id}` | 元数据 |
| GET | `/api/v1/skills/{id}/content` | Markdown（text/markdown） |
| POST | `/api/v1/skills` | 创建版本 |
| PUT | `/api/v1/skills/{id}` | 更新描述 |
| POST | `/api/v1/skills/{id}/enable\|disable` | 启停版本 |

### 18.2 校验与存储

- Code 小写 slug（3–120），Version `[0-9A-Za-z][0-9A-Za-z._-]{0,39}`，均不可变。
- 内容 ≤ `Skills:MaxBytes`（默认 64KB）；服务端写入并计算 SHA-256。
- `Skills:RootPath` 未配置时创建返回 503；客户端不能提交路径。
- 同项目/全局 Code+Version 重复返回 409 `skill-version-conflict`。
- 规则引用仍按项目优先、最新 `CreatedAtUtc` 解析；禁用最新版本会阻止新规则引用。

### 18.3 前端

`/skills` 页面支持列表/过滤、创建版本（Markdown 编辑）、查看内容、启用/禁用。

## 19. AOS-010b 模型下发

- Claim 的每个任务携带 `modelProfile`（含 Source：task/rule/agent）；字段只读，密钥仅为
  环境变量名。
- Runner 规则：Provider 必须为 `codex-cli`、ExecutionMode 必须为 `cli`，否则任务以
  `model-provider-unsupported` 失败且不重试。
- 有效超时 = `min(task.TimeoutSeconds, profile.TimeoutSeconds, Codex:TimeoutSeconds)`；
  模型优先使用 Claim 的 `modelProfile.model`，缺失时回退 `Codex:Model`。
- Codex CLI 仍通过 `-m <model>` 传参；密钥不进入 Prompt/参数。

## 20. AOS-012 Planner Agent 与 Kimi

### 20.1 配置 Kimi

1. 创建 ModelProfile（`/models` 或 API）：
   - `provider=kimi`、`executionMode=api`、`model=<Kimi 控制台模型 ID>`
   - `configurationJson={"baseUrl":"https://api.moonshot.cn/v1"}`（K3 系列只接受 temperature=1，建议不配）
   - `secretReference=KIMI_API_KEY`
2. 设置环境变量 `KIMI_API_KEY`（API 进程）。
3. `Planner:DefaultModelProfileCode` 指向该 Profile（默认 `kimi-planner`）；
   `Planner:AllowedProviderHosts` 默认仅 `api.moonshot.cn`、`api.openai.com`。

### 20.2 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/planner/drafts?status&limit&offset` | 草案列表 |
| GET | `/api/v1/planner/drafts/{id}` | 草案详情（含 Markdown） |
| POST | `/api/v1/planner/drafts` | 生成草案（PendingReview） |
| POST | `/{id}/approve\|reject` | 人工决策 |

- 生成请求：`{ title, goal, context?, projectId?, modelProfileId? }`。
- 草案必须人工批准；批准仅改变状态，不自动实现。

### 20.3 安全

- 仅 HTTPS + 主机白名单；密钥只从环境变量读取，不落库/日志。
- 生成失败返回 502 且不保存草案；非 API 模式/缺密钥返回 400/503。

## 21. AOS-013 Coding Run

### 21.1 流程

1. `/planner` 生成草案并人工批准。
2. `/coding-runs` 选择已批准草案与同项目的编码 Agent，创建 Run。
3. 服务端冻结快照并创建 Queued 任务；Runner 领取后按现有 Codex 流程执行。
4. Run 读取时投影任务状态；取消调用任务取消，重试创建新任务并保留历史。

### 21.2 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/coding-runs` | 列表（status/limit/offset） |
| GET | `/api/v1/coding-runs/{id}` | 详情 + 事件 |
| POST | `/api/v1/coding-runs` | `{ draftId, projectId, agentId, priority?, timeoutSeconds? }` |
| POST | `/{id}/cancel` | 取消 |
| POST | `/{id}/retry` | 仅 Failed/Canceled |

- 一份草案只能创建一个 Run（唯一索引）。
- 成功/失败以任务终态为准；当前不执行草案校验命令，也不为每个 Attempt 建独立工作区。

## 22. AOS-013b 校验命令

- 创建 Coding Run 可传 `validationCommands: ["dotnet test ..."]`（最多 10 条）。
- Runner 在 Codex 成功后按顺序执行；全部退出码 0 才成功。
- 失败分类：`validation-config-invalid`/`validation-failed`/`validation-timeout` 不可重试；
  `validation-error`（进程无法启动）可重试。
- 每条命令默认 600s，范围 1–1800s；Runner 清空环境，只保留 PATH/SystemRoot/TEMP 等，
  因此校验命令读不到 `KIMI_API_KEY` 等密钥。
- 当前仍在项目工作区执行，不创建独立 worktree；worktree 属于 AOS-017。

## 23. AOS-014 Review Agent

### 23.1 变更集

- 创建 Coding Run 时可传 `baseRevision`（7–64 位十六进制 commit）。
- Runner 在 Codex 与校验成功后采集 `HEAD`、`git diff base..HEAD`、变更文件清单，上传
  `changeset.diff`/`changeset.json` Artifact；采集失败任务为 `changeset-error`。
- 没有 baseRevision 的 Run 不能发起 Review。

### 23.2 审查

- `POST /api/v1/coding-runs/{id}/review`：仅 Succeeded 且有 changeset 的 Run；同步执行
  Codex 只读审查，返回 ReviewRun + findings；已成功/进行中幂等，Failed 可重试。
- `GET .../review`：查询结论、摘要与 findings；未发起 404。
- Review Agent 使用 Codex CLI 只读沙箱、固定提示词、严格 JSON 解析；不修改代码、不合并、
  不执行校验命令。
- 结论只是辅助意见，不改变 Coding Run/校验状态。

### 23.3 限制

- 同步审查最长 600s；无后台队列。
- 不自动触发；必须在 `/coding-runs` 详情手动点击。
- 不提供多 Reviewer/多模型、历史趋势或自动修复。

## 24. AOS-015 QA Agent

- Runner 在 validationCommands 执行后上传 `validation-results.json`（命令/状态/退出码/耗时/输出）。
- `POST /api/v1/coding-runs/{id}/qa-runs`：读取既有结果，生成 QA Attempt；不重新执行测试。
- `GET /api/v1/coding-runs/{id}/qa-runs/latest`：最近一次 QA 与命令结果。
- 结论：全部 Passed → Passed；任一 Failed/Timeout/Error → Failed；缺失/空结果被拒绝。
- 限制：只做命令级结果；JUnit/Cobertura 用例级与覆盖率在 AOS-015b。

## 25. AOS-015b QA 报告

- validationCommands 需把 JUnit 输出写到 `junit/*.xml`，覆盖率写到根目录
  `coverage.cobertura.xml`（仅这两个固定位置）。
- Runner 上传 `junit-N.xml` 与 `coverage.cobertura.xml` Artifact；QA 创建时解析。
- 安全：禁用 DTD/外部实体；JUnit ≤20 个/单个 2MB，覆盖率 ≤4MB，用例 ≤5000。
- 缺失报告 → not_provided；部分损坏 → partial + issues；全部损坏 → failed。
- QA 面板展示用例计数与行/分支覆盖率；未知显示 `—`。

## 26. AOS-017 Git Worktree 隔离

- 配置 `Runner:WorktreeRoot` 为绝对路径；不配置时声明 worktree 的任务失败。
- 任务 InputJson：`{"codingRunId":"...","baseRevision":"<完整 commit>","workspace":{"mode":"git-worktree"}}`。
- Runner 从 base commit 创建独立目录/分支，执行、校验、changeset 采集都在 worktree 内；结束后清理。
- 不 fetch、不切换共享分支、不 merge/push；清理失败保留现场。
- 限制：无清单/崩溃恢复、无 API/UI 状态、无 per-repo 管理锁（后续 017b）。
