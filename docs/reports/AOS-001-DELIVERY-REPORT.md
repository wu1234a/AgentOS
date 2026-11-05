# AOS-001 交付报告：Project Foundation

**状态：Completed**  
**完成时间：2026-11-06**  
**任务文件：** `tasks/AOS-001-project-foundation.md`

## 1. 任务目标

建立 AgentOS 独立仓库的后端、前端、数据库访问和基础开发规范。完成后开发者可以分别启动
ASP.NET Core API 和 Vue 3 JavaScript 管理后台，API 能连接 SQL Server，并通过
`/health/live` 与 `/health/ready` 暴露运行状态。

## 2. 实现摘要

### 后端

- 创建 `backend/AgentOS.slnx`（.NET 10 默认 solution 格式）和 7 个项目：
  `AgentOS.Api`、`AgentOS.Domain`、`AgentOS.Application`、`AgentOS.Infrastructure`、
  `AgentOS.Worker`、`AgentOS.Runner`、`AgentOS.AgentAdapters`。
- 引用方向：Api → Application/Infrastructure；Application → Domain；
  Infrastructure → Application/Domain；Worker → Application/Infrastructure；
  Runner → Application/AgentAdapters；AgentAdapters → Application/Domain。
- `AgentOS.Api` 使用 Minimal API：
  - `/health/live`：不查询数据库，只证明进程存活；
  - `/health/ready`：自定义 `SqlServerHealthCheck` 打开 SQL Server 连接，失败返回 503；
  - 健康响应输出精简 JSON（status、各检查项状态、耗时），不包含连接串或异常细节；
  - `AddProblemDetails` + `UseExceptionHandler` + `UseStatusCodePages` 统一错误响应；
  - Serilog 结构化 JSON 控制台日志 + 请求日志；
  - 开发环境 CORS 允许 `http://localhost:5173`；
  - `public partial class Program` 供 `WebApplicationFactory` 使用。
- `AgentOS.Infrastructure` 包含 `AgentOSDbContext`、设计时工厂
  `AgentOSDbContextFactory`、`SqlServerHealthCheck` 和初始迁移。
- `AgentOS.Worker` 为可启动的空 Worker（AOS-005 再实现租约/Outbox 任务）。
- `AgentOS.Runner` 为控制台占位（AOS-006 再实现轮询/执行）。
- `AgentOS.AgentAdapters` 提供 `IAgentAdapter` 占位接口（AOS-007 实现 Codex 适配器）。

### 前端

- `frontend/agentos-web/`：Vue 3 + JavaScript + Vite。
- 依赖：Vue Router、Pinia、Axios、Element Plus、`@element-plus/icons-vue`。
- 页面：主布局（侧边栏 + 顶栏）、Dashboard（API Live / Ready 状态卡、刷新、最后检查时间）。
- `src/api/http.js` 统一 Axios 实例与错误归一化；`src/api/health.js` 调用健康接口。
- `src/stores/health.js`（Pinia）管理 Live/Ready、loading、error、lastCheckedAt。
- Vite dev server 代理 `/api` 和 `/health` 到 `http://localhost:5000`。
- ESLint 9 flat config 覆盖 `.js` 与 `.vue`；无 TypeScript。

### 工程化

- `.editorconfig`：C# 4 空格、JS/Vue 2 空格、UTF-8、LF。
- `.env.example`：仅非敏感变量（环境、连接串占位、前端 API 基地址）。
- `docs/DEVELOPMENT.md`：环境要求、数据库、启动、测试、配置和 CI 说明。
- `.github/workflows/ci.yml`：后端 build/test、前端 npm ci/lint/build、禁 TypeScript 门禁。
- `.gitignore` 已在 AOS-001 开始前提交（commit `e43d5c6`）。

## 3. 数据库 Migration

- Migration：`20261105184054_InitialCreate`。
- 内容：空迁移（AOS-001 不提前创建业务表），只建立 `__EFMigrationsHistory`。
- 数据库：`AgentOS_Dev`（SQL Server 2025，默认实例，Windows 身份）。
- 已执行：

```powershell
dotnet ef database update --project backend/AgentOS.Infrastructure --startup-project backend/AgentOS.Api
```

- 验证：`__EFMigrationsHistory` 中存在 `20261105184054_InitialCreate`，ProductVersion `10.0.14`。

## 4. API 变化

| 方法 | 路径 | 行为 |
|---|---|---|
| GET | `/health/live` | 200 `{"status":"Healthy","checks":[],"durationMs":0}` |
| GET | `/health/ready` | 200 且 SQL 检查 Healthy；数据库不可达时 503 + Unhealthy |
| GET | `/` | 服务名、状态、UTC 时间戳 |
| 任意未知路径 | — | 404 ProblemDetails（`application/problem+json`） |

AOS-001 不包含业务 API 和认证；API Client / 身份验证从 AOS-002 开始。

## 5. 测试命令与结果

| 检查 | 命令 | 结果 |
|---|---|---|
| 后端构建 | `dotnet build backend/AgentOS.slnx --nologo` | ✅ 0 警告 / 0 错误 |
| 后端测试 | `dotnet test backend/AgentOS.slnx --no-build --nologo` | ✅ 6 通过 / 0 失败 / 0 跳过 |
| 数据库迁移 | `dotnet ef database update ...` | ✅ `AgentOS_Dev` 创建，迁移历史 1 条 |
| 健康检查（真实 SQL） | `GET /health/live`、`GET /health/ready` | ✅ 均 200，`sqlserver` Healthy（175ms） |
| 前端 lint | `npm run lint` | ✅ 0 问题 |
| 前端 build | `npm run build` | ✅ 成功（Element Plus 体积 >500KB 的提示，不影响） |
| Vite dev server | `npm run dev` | ✅ `http://localhost:5173` 返回 200 |
| 前端代理 | `http://localhost:5173/health/ready` | ✅ 200 `Healthy`（Vite 代理到 API） |
| TypeScript 门禁 | 扫描 `.ts`/`.tsx`/`lang="ts"` | ✅ 未发现 |

说明：本机 DSH 派生的子进程为 **Low 完整性级别**，`dotnet test` 的 testhost 与 API 访问
SQL Server 命名管道会被拒绝；上述测试/API 验收在提权（或普通 Medium 完整性）终端中完成，
这是本地 DSH 环境限制，不是项目代码要求。独立 `Microsoft.Data.SqlClient` 连接测试在
Medium 进程中全部通过。

## 5.1 独立代码审查与复审

- 审查对象：commit `a6fe2d6`（AOS-001 初始交付）。
- 审查方式：`dsh_code_review`（Codex 审查 rubric 的 DSH 原生实现，独立子 Agent，只读）。
- 结论：无 Blocker / High；2 个 Medium、4 个 Low。
- 处理：Medium 两项与 Low 四项已修复，详见 `docs/reports/AOS-001-CODE-REVIEW.md`。
- 复审：`dotnet build` 0 警告 0 错误；`dotnet test` 6 通过 / 0 失败；
  `/health/live`、`/health/ready` 均 200（SQL Healthy 180ms）；
  `dotnet ef migrations list --no-connect` 正常返回 `20261105184054_InitialCreate`。

## 6. 安全检查

- 数据库使用 Windows 身份，不在代码、配置样例或日志中保存 SQL 密码。
- `appsettings.json` 只包含本机开发连接串（无密码）；支持 `ConnectionStrings__AgentOS` 环境变量覆盖。
- 健康检查响应不输出异常堆栈、连接串或配置值。
- Serilog 不记录请求体；日志结构化为 JSON。
- `.env.example` 只列变量名和非敏感示例；`.gitignore` 已覆盖 `.env`、密钥/证书和本地配置。
- 通过直接引用 `System.Security.Cryptography.Pkcs` 10.0.14 覆盖了 `Microsoft.Data.SqlClient`
  7.1.1 传递引入的 9.0.18 已知漏洞；最终构建 0 警告。
- 全仓库无 `.ts`、`.tsx`、`lang="ts"`。
- 未提交任何密钥、Token、连接密码或生产配置。

## 7. 已知限制

1. 本机 SQL Server 的 TCP/IP 与命名管道被禁用，只开放共享内存；连接串使用
   `Server=lpc:localhost`。换到 TCP 已启用的实例时改为 `localhost,1433` 即可。
2. DSH（DeepSeek Harness）派生的子进程是 Low 完整性；在 DSH 内直接 `dotnet test` 或
   `dotnet run` 会因 testhost/SQL 命名管道权限失败。请使用普通 PowerShell（Medium）或
   提权终端运行测试/API。项目本身不要求提权。
3. CI 的后端测试使用替身健康检查，不启动 SQL Server；真实数据库验收在本地完成。
4. `InitialCreate` 为空迁移，业务表从 AOS-002 开始创建。
5. AOS-001 不包含认证与授权；管理后台目前是本机开发界面。
6. 前端 Element Plus 全量引入导致单个 JS chunk 约 1.07MB（gzip 349KB）；后续可按需引入或
   代码分割，AOS-001 不阻塞。
7. 测试日志 `tests/test-run.log` 为本地 UTF-16 日志，已被 `.gitignore` 忽略。

## 8. 未完成事项

AOS-001 验收范围内无未完成项。以下内容按任务边界推迟：

- 事件接收、去重、规则引擎（AOS-002～004）；
- 任务队列、租约、重试（AOS-005）；
- Runner 轮询与 Codex Adapter（AOS-006/007）；
- 日志/产物管理后台（AOS-008）；
- 管理后台认证、API Client 与 Scope（AOS-002 起）。

## 9. 下一任务建议

先完成 AOS-002 Event Hub：

1. 确定 API Client 认证方案（开发环境 Bearer/API Key，正式环境再切换）；
2. 建立 `Events` 实体、DTO、验证器、`POST/GET /api/v1/events`；
3. 事件写入与 Outbox 的一致性策略在 AOS-002/003 ADR 中明确；
4. 增加 OpenAPI（.NET 10 内置）和 Scalar/Swagger UI，便于联调；
5. 在 CI 中增加 SQL Server service container，运行真实 `Events` 迁移与 API 集成测试。

## 10. 变更文件（摘要）

| 路径 | 说明 |
|---|---|
| `backend/AgentOS.slnx` | .NET 10 solution |
| `backend/AgentOS.Api/` | Minimal API、健康检查、ProblemDetails、Serilog |
| `backend/AgentOS.Infrastructure/` | DbContext、设计时工厂、SQL 健康检查、迁移 |
| `backend/AgentOS.Domain/`、`Application/`、`AgentAdapters/` | 分层占位与接口 |
| `backend/AgentOS.Worker/`、`Runner/` | 可启动占位 |
| `tests/AgentOS.Api.Tests/` | xUnit + WebApplicationFactory 健康检查测试 |
| `frontend/agentos-web/` | Vue 3 JS + Vite + Pinia + Router + Element Plus |
| `docs/DEVELOPMENT.md` | 本地开发说明 |
| `docs/reports/AOS-001-DELIVERY-REPORT.md` | 本报告 |
| `docs/reports/AOS-001-CODE-REVIEW.md` | 独立审查记录与修复处理 |
| `.editorconfig`、`.env.example` | 工程规范与配置样例 |
| `.github/workflows/ci.yml` | 后端/前端 CI 与 TS 门禁 |
| `tasks/AOS-001-project-foundation.md` | 状态更新为 Completed |
