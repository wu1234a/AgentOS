# AOS-008 交付报告：Execution Logs & Observability

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-008-observability.md`

## 1. 数据模型（迁移 `20261106161833_AddObservability` + `AddEventDuplicateCount`）

| 表/字段 | 说明 |
|---|---|
| `TaskLogs` + | `ProjectId`、`EventId`、`Component`、`PropertiesJson`；迁移从 `Tasks` 回填 ProjectId/EventId，旧行 Component 置 `runner` |
| `Artifacts` | 名称、服务端生成的相对路径（唯一）、SHA-256、大小、MediaType、Project/Task/Attempt/Worker 关联 |
| `UsageRecords` | Provider/Model/Token 用量；当前 Adapter 未上报时为空 |
| `OutboxMessages` | 预留可靠副作用表（Status + AvailableAtUtc 索引） |
| `Events.DuplicateCount` | 重复投递计数，Dashboard 使用 |

## 2. 管理 API

- `GET /api/v1/dashboard/stats`：事件、重复事件、任务状态计数、成功率、平均等待/执行、
  在线 Runner（Online 且 2 分钟内心跳）、租约超时（Attempt Lost 数）、最近 5 个失败任务。
- `GET /api/v1/events`（沿用 AOS-002 契约）、`GET /api/v1/events/{id}`、
  `GET /api/v1/events/{id}/tasks`。
- `GET /api/v1/tasks`（status/projectId 过滤、limit/offset 分页）、`GET /api/v1/tasks/{id}`
  （Attempts、最近 50 条日志、Artifacts、Usage）。
- `GET /api/v1/tasks/{id}/logs`、`GET /api/v1/tasks/{id}/attempts/{attemptId}/logs`
  （level 过滤、`afterSequence` 增量、limit ≤ 500）。
- `GET /api/v1/workers`。
- `GET /api/v1/artifacts/{id}/download`（Dev Key 授权）。
- Runner 产物上传：`POST /api/v1/runners/tasks/{taskId}/artifacts`，租约头校验，media type
  白名单与大小限制。

## 3. 日志规范

- 结构化字段：ProjectId、EventId、TaskId、AttemptId、WorkerId、Sequence、Level、Component、
  Message、CreatedAtUtc；`(AttemptId, Sequence)` 唯一，重传幂等。
- 服务端 `TaskLogSanitizer` 对 Runner 上传内容再做一次脱敏（`sk-*`、Bearer、password/token/
  secret、Cookie），Runner 端已按完整行脱敏。
- 大段 stdout/stderr 存 Artifact，日志表只保留结构化行。
- 日志写入失败不影响任务终态（上传为独立请求，失败只记录警告）。

**示例**

```json
{
  "id": "0199...",
  "sequence": 12,
  "level": "info",
  "component": "runner",
  "message": "codex summary: fixed the failing test",
  "createdAtUtc": "2026-11-07T08:15:22.120Z"
}
```

## 4. Artifact 存储策略

- 本地受控根目录（`Artifacts:RootPath`，默认系统临时目录，生产必须配置持久路径）。
- 目录 `<root>/<project>/<task>/<attempt>/`，文件名安全化、重名自动加序号；
  客户端只能提交文件名，不能提交 StoragePath。
- 上传流式写临时文件 → 计算 SHA-256 → 原子移动 → 写元数据；超限/类型不允许时 400 并清理临时文件。
- 下载必须携带 API 凭证；返回原始 MediaType 与文件名。
- 保留策略/自动清理尚未实现（见限制）。

## 5. 管理后台页面

| 页面 | 内容 |
|---|---|
| Dashboard | 统计卡片（事件/重复/任务状态/成功率/平均耗时/在线 Runner/租约超时）+ 最近失败任务 |
| Events | 事件分页列表（来源、类型、状态、摘要、时间） |
| Event Detail | 事件字段、payload JSON、关联任务 |
| Tasks | 状态过滤 + 分页列表（项目/Agent/Skill/尝试次数） |
| Task Detail | Attempts 时间线、日志（级别过滤 + 增量加载）、Artifacts 下载、Usage（未提供时明确提示） |
| Runners | 在线状态、最后心跳、平台/版本/主机 |

前端保持 Vue 3 + JavaScript（无 TypeScript）；新增 `@microsoft/signalr` 客户端。

## 6. SignalR

- Hub：`/hubs/tasks`，连接 query 参数 `api_key` 使用与 REST 相同的开发凭证，失败即 Abort。
- 事件：`taskChanged`（claim/complete/fail/cancel 后）与 `tasksClaimed`；仅用于 UI 刷新。
- 前端使用自动重连；`onreconnected` 后重新拉取 REST 数据，数据库为最终事实来源。
- 不承担可靠队列职责。

## 7. 验收对照

| 验收标准 | 证据 |
|---|---|
| 从 Event 找到关联 Task | `SqlObservabilityTests.Event_detail_links_tasks...`；`GET /events/{id}/tasks` |
| 从 Task 查看所有 Attempts | `Task_detail_exposes_attempts...`；`GET /tasks/{id}` |
| 查看每次 Attempt 的日志 | `GET /tasks/{id}/attempts/{attemptId}/logs` + 级别过滤测试 |
| 查看并授权下载 Artifact | `Artifact_store_enforces...`（哈希/限制/路径）；`ObservabilityApiTests.Artifact_download...` |
| 日志脱敏测试 | `Log_sanitizer_redacts_credentials` + Runner 行级脱敏测试 |
| SignalR 断线后 REST 恢复 | 前端重连回调重新拉取；Hub 断开不影响 REST（无自动化 WebSocket 测试，见限制） |
| Dashboard 与数据库一致 | `Dashboard_counts_match_the_database` |
| 大日志分页 | `Task_detail...` 中 afterSequence 分页 + limit 上限 |
| Runner 状态/心跳正确 | `GET /workers` 与 AOS-006 心跳测试；Dashboard 在线数测试 |
| 前端 lint/build、后端 build/test | 见下 |

## 8. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **250 通过 / 4 跳过 / 0 失败** |
| AOS-008 新增 SQL 用例 | ✅ 5（Dashboard 一致性、Task 详情/分页、Event 关联、Artifact 存储限制、脱敏） |
| AOS-008 API 契约用例 | ✅ 8（鉴权、统计、任务列表/详情、404、日志分页、下载、Runner 上传/租约拒绝） |
| 前端 lint | ✅ 0 error / 0 warning |
| 前端 build | ✅ 成功（chunk 体积警告，非失败） |

## 9. 安全

- 管理与下载接口都需要开发 API Key；Runner 上传额外校验租约。
- 产物路径完全由服务端生成，拒绝客户端路径；文件名安全化；类型/大小限制。
- 服务端日志脱敏兜底；日志/错误信息截断。
- SignalR 连接要求有效 API Key；当前为开发凭证模型（无用户级项目授权，见限制）。

## 10. 已知限制

1. SignalR 当前只有开发凭证认证，未做用户会话与项目级分组；“项目访问权限”随 AOS-009 认证体系补齐。
2. Artifact 保留/清理策略未实现，文件会持续增长。
3. 日志没有全文检索；按级别/Sequence/Attempt 过滤。
4. Usage 仅有表与 UI，Codex CLI JSON 事件的 usage 尚未解析入库（显示“未提供/估算”）。
5. 前端 SignalR 未做自动化 WebSocket 集成测试，断线恢复依赖重连回调重新拉取 REST。
6. Dashboard 成功率定义为 `Succeeded / (Succeeded + Failed + Cancelled)`，在页面文档中说明。
7. 事件列表沿用 AOS-002 的 `page/pageSize` 契约，任务/日志使用 `limit/offset`。

## 11. 变更文件

**后端新增**

- `AgentOS.Domain/Artifacts/Artifact.cs`、`Usage/UsageRecord.cs`、`Outbox/OutboxMessage.cs`
- `AgentOS.Application/Observability/*`（契约、sanitizer）
- `AgentOS.Infrastructure/Artifacts/ArtifactStore.cs`、`Observability/ObservabilityQueryStore.cs`
- `AgentOS.Infrastructure/Persistence/Configurations/ObservabilityConfigurations.cs`
- `AgentOS.Api/Endpoints/ObservabilityEndpoints.cs`、`Realtime/TaskHub.cs`、`Realtime/TaskEventPublisher.cs`
- 迁移：`AddObservability`、`AddEventDuplicateCount`

**后端修改**

- `TaskLog`/配置/Store（ProjectId/EventId/Component/脱敏）、`Event`/`EventRepository`（DuplicateCount）、
  `TaskQueueService.AuthorizeLeaseAsync`、`RunnerEndpoints`（产物上传、SignalR 推送）、
  `TasksEndpoints`（取消推送）、`Program`（SignalR/路由/配置）、`DbContext`、`DependencyInjection`
- Runner：`TaskHandlerResult.Artifacts`、`CodexTaskHandler` 产物清单、`TaskProcessor` 上传、
  `IRunnerApi`/`RunnerApiClient.UploadArtifactAsync`

**前端**

- `api/observability.js`、`composables/useTaskHub.js`、`utils/format.js`
- `views/DashboardView.vue`（重写）、`EventsView.vue`、`EventDetailView.vue`、`TasksView.vue`、
  `TaskDetailView.vue`、`RunnersView.vue`
- `router/index.js`、`layouts/AppLayout.vue`、`vite.config.js`、`package.json`（`@microsoft/signalr`）

**测试**

- `SqlObservabilityTests.cs`、`ObservabilityApiTests.cs`、`Fakes/FakeObservabilityStore.cs`

**文档**

- `docs/DATABASE-ER.md` §3.20、`docs/DEVELOPMENT.md` §14、本报告、任务状态

## 12. 下一任务建议

AOS-009：认证与多租户 —— 用户会话、项目级授权、SignalR 项目分组、Artifact/日志访问控制；
随后补 Artifact 保留/清理与 Usage 解析。
