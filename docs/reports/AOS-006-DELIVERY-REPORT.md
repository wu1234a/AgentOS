# AOS-006 交付报告：Local Agent Runner

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-006-local-runner.md`

## 1. 任务目标

实现常驻本地 Runner：主动注册、领取任务、发送心跳、执行受控 Handler、回传日志与结果；
支持断线重连、项目/工作区白名单、并发限制和优雅关闭。AOS-006 只提供 Demo Handler，
Codex Adapter 留待 AOS-007。

## 2. 服务端变更

### 2.1 数据模型（迁移 `20261106151327_AddRunnerClientsAndLogs`）

| 表 | 说明 |
|---|---|
| `ApiClients` | 机器身份：`ClientCode` 唯一、`ClientType`、`WorkerId`、`SecretHash`（SHA-256）、`ScopesJson`、启用/过期时间 |
| `TaskLogs` | Runner 日志：`(AttemptId, Sequence)` 唯一、level、message（≤4000）、时间 |

### 2.2 注册与凭证

- `POST /api/v1/runners/register`，请求头 `X-AgentOS-Registration-Key`（配置
  `Runner:RegistrationKey`）；成功返回 **一次性** `clientSecret`，库中只存哈希。
- 重复注册会就地轮换同一 `runner-{WorkerCode}` 客户端的密钥并重新启用；不会产生重复
  ClientCode（SQL 测试发现并修正了唯一键冲突）。
- 未配置注册密钥时注册返回 503，只允许管理员预置的客户端连接。
- Runner 端点支持两种认证：
  1. `X-AgentOS-Client-Id` + `X-AgentOS-Client-Secret`（Runner 类型、启用、未过期、Secret 恒定时间比较）；
  2. 开发回退：`X-AgentOS-Dev-Key` + `X-AgentOS-Worker-Code`（Worker 必须 Online）。
- `POST /api/v1/runners/heartbeat` 更新 `Workers.LastHeartbeatAtUtc`，用于展示在线状态。
- `POST /api/v1/runners/tasks/{id}/logs` 校验租约后写入日志；同 `(attempt, sequence)` 重传
  不会重复（SQL 测试覆盖）。

## 3. Runner 实现（`backend/AgentOS.Runner`）

| 文件 | 职责 |
|---|---|
| `RunnerOptions.cs` | BaseUrl、Worker 身份、WorkspaceRoots/Projects、轮询/心跳/并发、凭证与注册键；`Validate()` 启动即校验 |
| `RunnerApiClient.cs` / `IRunnerApi.cs` | 只发出站 HTTP：注册、Worker 心跳、claim、start/heartbeat/complete/fail、日志；非 2xx 抛 `RunnerApiException`（区分瞬时与永久） |
| `WorkspacePolicy.cs` | 项目白名单 + 路径规范化 + 根目录包含检查 + reparse point/符号链接拒绝 |
| `Handlers/ITaskHandler.cs` | `ITaskHandler`、标准结果（Succeeded/Failed/Cancelled、退出码、错误码、可重试） |
| `Handlers/DemoTaskHandler.cs` | 无副作用 Demo：分步日志、可取消 |
| `TaskProcessor.cs` | 单任务生命周期：start → Handler → 心跳/取消 → 日志批量上传 → 唯一终态上报；终态上报失败只记录，绝不本地判成功 |
| `RunnerService.cs` | 注册、轮询领取、`MaxConcurrentTasks` 并发、指数退避、Worker 心跳、优雅关闭（最多等 15s） |
| `Program.cs` / `appsettings.example.json` | 配置加载、HttpClient、DI、运行配置示例 |

### 3.1 关键行为

- **断线重连**：Claim 失败按 2s→60s 指数退避；恢复后继续领取，不退出进程。
- **API 停机不误报成功**：Complete 失败重试 3 次后仅记录错误；任务租约到期由 AOS-005
  的恢复流程重新排队。
- **取消传递**：心跳响应 `cancelRequested=true` → 取消 Handler 的 CancellationToken →
  `fail(cancelled=true)`；进程关闭则回传 `runner-shutdown`（可重试），不会与用户取消混淆。
- **并发上限**：`running.Count` 达到 `MaxConcurrentTasks` 后不再领取。
- **日志**：批量（默认 50 行）上传，失败只丢弃该批（Debug 日志），不影响任务状态。
- **优雅关闭**：停止领取 → 等待运行中任务 → 未完成以可重试失败回传。

## 4. 验收项对照

| 验收标准 | 证据 |
|---|---|
| Runner 可成功注册 | `RunnerSessionApiTests.Registration_creates_a_worker_and_returns_a_client_secret_once`；SQL `Registration_rotates_runner_credentials` |
| API 能看到 Runner 在线状态 | `POST /runners/heartbeat` 更新 `LastHeartbeatAtUtc`；`Worker_heartbeat_updates_the_last_seen_time` |
| Runner 能领取测试任务 | `Runner_backs_off_then_claims_and_completes_a_task`（先失败一次再领取并完成）；`Claim_returns_the_claimed_tasks_and_lease` |
| 定时发送心跳 | `RunnerService.HeartbeatLoop` + SQL/单元心跳用例；Worker 心跳端点测试 |
| 回传日志和完成结果 | `Logs_are_stored_for_a_valid_lease`、`Task_logs_are_idempotent_per_attempt_sequence`、`Successful_handler_starts_logs_and_completes` |
| 断网后可重连 | `Runner_backs_off_then_claims_and_completes_a_task`（Claim 503 → 退避 → 恢复） |
| API 停机不会错误标记成功 | `Completion_rejected_by_the_api_is_never_reported_as_success` |
| 只能访问白名单项目路径 | `WorkspacePolicyTests`：未知项目、外部路径、路径穿越、缺失目录、符号链接；`Workspace_rejection_never_starts_the_handler` |
| 取消可以传递给 Handler | `Heartbeat_cancellation_stops_the_handler_and_reports_cancelled` |
| 多任务并发受上限限制 | `RunnerService` 容量计算 + `MaxConcurrentTasks` 配置校验；并发领取由 AOS-005 队列测试覆盖 |

## 5. 测试

```powershell
dotnet build backend/AgentOS.slnx
$env:AGENTOS_SQL_TEST_CONNECTION = "Server=lpc:localhost;Database=AgentOS_Test;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3"
dotnet test backend/AgentOS.slnx
```

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 全量测试（含 SQL） | ✅ **211 通过 / 2 跳过 / 0 失败**（跳过项为当前 Windows 环境不允许创建符号链接/大小写敏感用例；CI Linux 会执行大小写用例） |
| SQL 队列/Runner 用例 | ✅ 9/9（并发领取、优先级、续租、恢复、重试、取消、注册轮换、日志幂等、禁用 Worker） |
| 迁移 | ✅ `AddRunnerClientsAndLogs` 已应用到 `AgentOS_Dev` |
| 前端 | 无改动 |

## 6. 安全

- Runner Secret 只存 SHA-256；注册只返回一次；轮换后旧 Secret 立即失效。
- 注册需要独立 Bootstrap Key；未配置时注册关闭。
- Runner 凭证限定 Runner 类型与 Worker 绑定，普通 API Client 不能领取任务。
- 工作区校验：项目白名单、路径规范化、根目录包含、reparse point/符号链接逃逸拒绝；
  Handler 不接收任意路径。
- 日志/错误信息截断到列宽，超长输入不会导致 500。
- 未提交任何真实密钥；`appsettings.example.json` 只有占位符。

## 7. 已知限制

1. 仅 Demo Handler；Codex 进程、工作区写隔离与清理在 AOS-007/后续任务。
2. “写任务使用独立工作区”尚未实现——当前只做路径白名单校验，Demo Handler 不写入。
3. Client 的 `ScopesJson` 已存储，但端点尚未按 Scope 细分授权（Runner 范围整体授权）。
4. 日志上传为尽力而为，API 长时间不可用时内存缓冲批次会丢弃。
5. 未提供 Windows Service 安装器或自动启动；文档给出 `sc.exe`/NSSM 指引。
6. 注册密钥为共享 Bootstrap 凭证，不是一次性令牌；生产建议短时启用后关闭。
7. Runner 未做自更新/版本协商。

### 7.1 独立代码审查\n\nCodex CLI 审查 commit `3589bc2`：2 个 P1（Runner 宿主缺少 TimeProvider、禁用 Worker 可绕过认证/被心跳复活）与 4 个 P2（心跳失败误判为取消、日志批次未 drain、注册不重试、Linux 路径大小写），全部修复并新增回归测试，详见 `docs/reports/AOS-006-CODE-REVIEW.md`。\n\n## 8. 下一任务建议

AOS-007 Codex Adapter：

1. 实现 `ITaskHandler` 的 Codex CLI 版本（受控进程、超时、输出捕获）；
2. 工作区准备/清理（独立副本、未提交变更保护）；
3. 产物清单与结构化结果；
4. 与 AOS-006 的取消/日志/租约路径联调。

## 9. 变更文件

**新增（后端）**

- `AgentOS.Domain/Security/ApiClient.cs`、`AgentOS.Domain/Tasks/TaskLog.cs`
- `AgentOS.Application/Security/ClientSecretHasher.cs`、`IApiClientStore.cs`
- `AgentOS.Application/Runners/RunnerRegistrationContracts.cs`、`RunnerRegistrationService.cs`
- `AgentOS.Application/Tasks/TaskLogContracts.cs`
- `AgentOS.Infrastructure/Security/ApiClientStore.cs`、`Runners/RunnerRegistrationStore.cs`、
  `Tasks/TaskLogStore.cs`、`Persistence/Configurations/{ApiClient,TaskLog}Configuration.cs`
- `AgentOS.Infrastructure/Migrations/20261106151327_AddRunnerClientsAndLogs*.cs`
- `AgentOS.Api/Endpoints/RunnerRegistrationEndpoints.cs`
- `AgentOS.Runner/*`（Options、ApiClient、WorkspacePolicy、Handlers、TaskProcessor、RunnerService、Program、配置示例）

**新增（测试）**

- `Fakes/FakeRunnerApi.cs`、`FakeRunnerRegistrationStore.cs`、`FakeTaskLogStore.cs`
- `TaskProcessorTests.cs`、`RunnerServiceTests.cs`、`WorkspacePolicyTests.cs`、
  `RunnerSessionApiTests.cs`，以及 `SqlTaskQueueTests` 中的注册/日志用例

**修改**

- `RunnerEndpoints.cs`、`RunnerAuthenticationFilter.cs`、`Program.cs`、`appsettings.json`、
  `AgentOSDbContext.cs`、`DependencyInjection.cs`、`TaskQueueService.cs`、`TaskQueueContracts.cs`、
  `TaskClaimer.cs`（返回 `ProjectSlug`）、`AgentOS.Runner.csproj`
- `docs/DATABASE-ER.md`、`docs/DEVELOPMENT.md`、`tasks/AOS-006-local-runner.md`
