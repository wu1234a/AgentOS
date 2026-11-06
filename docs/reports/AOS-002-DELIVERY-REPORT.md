# AOS-002 交付报告：Event Hub

**状态：Completed**  
**完成时间：2026-11-06**  
**任务文件：** `tasks/AOS-002-event-hub.md`

## 1. 任务目标

建立统一事件接收 API，将外部系统或内部模块发送的事件标准化、验证并保存到 SQL Server。
本任务只负责接收和持久化，不负责去重（AOS-003）和规则分发（AOS-004）。

## 2. 实现摘要

### 数据模型

- `Projects`（AOS-002 最小表）：`Id`、`Name`、`Slug`（唯一）、`IsEnabled`、`CreatedAtUtc`、
  `UpdatedAtUtc`。AOS-002 需要按 Project Slug 校验事件归属，但此前没有任何任务负责建 Projects；
  先建最小表，AOS-004/006 再补 `AllowedWorkspaceRoot` 等字段。
- `Events`：任务要求的字段 + `SubjectType`、`SubjectId`、`Priority`（标准 Envelope 字段），
  以及 `ProcessedAtUtc`（DATABASE-ER §3.6）。
- 字段长度、索引、`Project` 外键（Restrict）、`PayloadJson` 的 `ISJSON` 检查约束。
- 去重唯一约束和冲突行为**不在本任务**，留给 AOS-003。

### API

| 方法 | 路径 | 行为 |
|---|---|---|
| POST | `/api/v1/events` | 校验并保存事件，成功 202 |
| GET | `/api/v1/events/{eventId}` | 详情（含 Payload），不存在 404 |
| GET | `/api/v1/events` | 分页 + `source/type/status/project` 过滤 |

- 成功响应：`{ eventId, accepted: true, duplicate: false, status: "Received" }`。
- 400：字段级 `ValidationProblemDetails`（含未知/禁用 Project）。
- 401：缺少或错误的 `X-AgentOS-Dev-Key`。
- 413：请求体超过 `Events:MaxRequestBodyBytes`（默认 256 KB）。
- 404：事件不存在。
- 未捕获的非法 JSON / 空 body 通过异常状态映射返回 400。

### 认证占位

- `IApiClientAuthenticator` 抽象位于 Application；
- Development 使用 `X-AgentOS-Dev-Key`，密钥从配置/User Secrets/环境变量读取，**不提交到 Git**；
- 非 Development 注册 fail-closed 实现，未实现真正 API Client 前全部返回 401；
- 比较使用 `CryptographicOperations.FixedTimeEquals`。

### 分层

Domain 只有实体和枚举；Application 包含 DTO、Validator、Service、Repository/ProjectLookup/Auth
接口；Infrastructure 实现 EF Core 配置、Repository、ProjectLookup 和 DI；Api 只做端点、过滤器、
认证实现和组合根。

## 3. 数据库 Migration

| Migration | 内容 |
|---|---|
| `20261105201445_AddProjectsAndEvents` | 创建 `Projects`、`Events` 及索引/外键/ISJSON 约束 |
| `20261105202604_AddProcessedAtUtc` | 为 `Events` 增加 `ProcessedAtUtc` |

- 已应用到 `AgentOS_Dev`；`AgentOS_Test` 由 SQL 集成测试 `MigrateAsync()` 创建/升级。
- 本地开发项目数据：`3derp`（启用）、`disabled-demo`（禁用）。

## 4. 测试命令与结果

| 检查 | 命令 | 结果 |
|---|---|---|
| 后端构建 | `dotnet build backend/AgentOS.slnx --nologo` | ✅ 0 警告 / 0 错误 |
| 后端测试 | `dotnet test backend/AgentOS.slnx --no-build --nologo` | ✅ 33 通过 / 0 失败 / 1 跳过 |
| SQL 集成测试 | 设置 `AGENTOS_SQL_TEST_CONNECTION` 后运行 `--filter FullyQualifiedName~SqlEventRepositoryTests` | ✅ 1 通过 / 0 失败 |
| 健康检查 | `GET /health/live`、`GET /health/ready` | ✅ 均 200，SQL Healthy |
| API 冒烟 | POST 合法事件 / GET 详情 / GET 列表 | ✅ 202 / 200 / 200 |
| 未授权 | 不带 `X-AgentOS-Dev-Key` | ✅ 401 |
| 未知/禁用项目 | `project=nope`、`project=disabled-demo` | ✅ 400，`errors.project` |
| 非法 JSON / 空 body | POST `{ not json` / 空 body | ✅ 400（自动化测试） |
| 超限请求体 | 300 KB body | ✅ 413（自动化测试） |
| 分页/过滤 | `page/pageSize/source/type/status/project` | ✅ 通过；`page=30000000`、`status=1`、`status=Processed,Received` 均 400 |
| 时间时区 | `occurredAtUtc` 无 `Z`/偏移 | ✅ 400（Validator 测试） |

测试总数 34：33 通过、1 跳过（SQL 集成测试默认跳过，设置环境变量后 1/1 通过）。
跳过使用 `Xunit.SkippableFact` 显式标记，而不是静默通过。

## 5. 独立代码审查

- 对象：AOS-002 暂存变更；方式：`dsh_code_review`（Codex rubric，独立子 Agent，只读）。
- 结论：**无 Blocker；1 High、5 Medium、4 Low**。
- 处理：High/Medium 全部修复；Low 全部修复或明确记录（CI SQL service container 作为后续补强）。
- 详见 `docs/reports/AOS-002-CODE-REVIEW.md`。

## 6. 安全检查

- 开发 API Key 不提交到 Git；通过 User Secrets/环境变量提供；未配置时 fail-closed。
- 非 Development 环境没有匿名入口；AOS-006 前统一 401。
- 认证 key 使用恒定时间比较。
- 列表接口不返回完整 Payload；详情才返回；日志不记录 Payload。
- Project Slug 必须存在且启用；未知/禁用项目 400。
- 请求体上限可配置，Kestrel 与端点过滤器共用同一配置键。
- API 错误为 ProblemDetails；不泄露连接串或堆栈。
- SQL 使用 Windows 身份，无密码入库；`ISJSON` 检查约束兜底 Payload 格式。

## 7. 已知限制

1. 没有项目管理 API；本地需要手工插入 `Projects`（文档提供了 SQL），AOS-004/006 再补。
2. Development API Key 必须显式配置；未配置时 API 对事件端点返回 401，这是刻意的 fail-closed。
3. CI 尚未加入 SQL Server service container；SQL 集成测试在 CI 中会跳过。真实 SQL 验收在本地
   通过 `AGENTOS_SQL_TEST_CONNECTION` 完成，CI SQL 服务作为后续补强。
   *（后续状态：该补强已于 AOS-003 期间完成——CI 后端 job 现在启动 SQL Server 2022 容器并
   真实执行 SQL 用例，见 `docs/DEVELOPMENT.md` §7。）*
4. 去重唯一索引、冲突行为（AOS-003）、规则引擎（AOS-004）、Outbox 尚未实现。
5. `Projects` 是最小表，ER 中的 `AllowedWorkspaceRoot`、`RepositoryUrl` 等字段后续补。
6. 在 DSH 受限令牌下运行 API/测试仍需要普通 PowerShell 或提权终端（Low 完整性无法访问
   SQL Server 命名管道）；这是本地 DSH 环境限制，不是应用要求。

## 8. 未完成事项

AOS-002 验收范围内无未完成项；以下按任务边界推迟：

- AOS-003：`Source + DedupeKey` 唯一索引、PayloadHash、409 冲突、并发去重测试；
- AOS-004：规则引擎、Task 生成、事件状态处理；
- AOS-006：真正的 API Client 凭证与 Scope；
- CI SQL Server service container（AOS-003/005 前建议补上）。

## 9. 下一任务建议

AOS-003 Event Deduplication：

1. 为 `Events` 增加 `Source + DedupeKey` 唯一索引（先制定旧数据回填/清理方案）；
2. 增加 `PayloadHash` 并固定规范化 JSON 策略；
3. 重复内容返回原 EventId + `duplicate: true`；相同 key 不同内容返回 409；
4. 并发提交测试使用真实 SQL Server；
5. 把 SQL 集成测试接入 CI 的 SQL Server service container。

## 10. 变更文件（摘要）

| 路径 | 说明 |
|---|---|
| `backend/AgentOS.Domain/Events/`、`Projects/` | Event、EventStatus、Project 实体 |
| `backend/AgentOS.Application/Events/`、`Projects/`、`Security/` | DTO、Validator、Service、Repository/ProjectLookup/Auth 接口 |
| `backend/AgentOS.Infrastructure/Persistence/Configurations/` | EF 配置、索引、约束 |
| `backend/AgentOS.Infrastructure/Repositories/`、`Projects/` | EF 仓储与 ProjectLookup |
| `backend/AgentOS.Infrastructure/Migrations/` | 两个迁移 |
| `backend/AgentOS.Api/Endpoints/`、`Filters/`、`Security/` | 事件端点、认证过滤器、开发/失败关闭认证 |
| `backend/AgentOS.Api/Program.cs`、`appsettings*.json` | 组合根、异常映射、请求体限制 |
| `tests/AgentOS.Api.Tests/` | Validator、API、SQL 集成测试与 fakes |
| `docs/DEVELOPMENT.md` | 事件 API、认证、Projects 初始化、SQL 测试说明 |
| `docs/reports/AOS-002-CODE-REVIEW.md` | 独立审查与修复记录 |
