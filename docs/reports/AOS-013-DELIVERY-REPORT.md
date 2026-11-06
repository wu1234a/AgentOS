# AOS-013 交付报告：Coding Run 编排（第一阶段）

**状态：Completed（第一阶段）**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-013-coding-runs.md`

## 1. 能力

- 已批准 Planner 草案 → 不可变快照 → Queued Runner 任务。
- `CodingRuns`/`CodingRunEvents` 持久化 Run、快照哈希、状态与审计事件。
- 状态投影、取消、重试；重试创建新任务并保留旧任务与事件。
- `/coding-runs` 前端页面：创建、列表、详情、取消、重试。

## 2. 数据与 API

- 迁移 `AddCodingRuns`：`CodingRuns`（DraftId 唯一、Hash、SnapshotJson、TaskId、
  Status、AttemptCount）与 `CodingRunEvents`（Sequence 唯一）。
- `POST /api/v1/coding-runs`、`GET /api/v1/coding-runs`、`GET /{id}`、
  `POST /{id}/cancel`、`POST /{id}/retry`。
- 错误：400 草案未批准/项目或 Agent 不可用，404，409 草案已有 Run/状态不可取消或重试。

## 3. 安全

- 创建只接受 `Approved` 草案；请求不能替换内容或模型。
- 快照含草案 Markdown、SHA-256、Run/项目/Agent/模型 ID；不含密钥或宿主路径。
- Agent 必须启用且属于请求项目；数据库唯一索引防止重复 Run。
- 事件与错误信息截断，避免超长/敏感内容。

## 4. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **276 通过 / 5 跳过 / 0 失败** |
| 新增 SQL 用例 | ✅ 4（创建/重复/未批准、取消、终态投影/重试、列表查询） |
| 前端 lint / build | ✅ 通过 |
| CI | 见对应 run |

## 5. 已知限制（AOS-013b 范围）

1. 未在 Runner 内执行草案声明的校验命令，成功仍以 Codex 退出状态为准。
2. 未为每个 Attempt 创建独立 Git worktree；复用 Runner 已校验项目工作区。
3. 未实现 `preparing/validating/reporting` 细粒度阶段、结果回传确认与异步恢复；Run 状态
   由任务状态投影。
4. 取消 Running 任务只设置取消请求，终态依赖 Runner 心跳/任务取消流程。
5. 未新增 Runner 回调契约，复用现有 TaskAttempt/租约。

## 6. 变更文件

**新增**：`Domain/CodingRuns/CodingRun.cs`、`Application/CodingRuns/*`、
`Infrastructure/CodingRuns/CodingRunRepository.cs`、配置、迁移 `AddCodingRuns`、
`Api/Endpoints/CodingRunEndpoints.cs`、前端 `CodingRunsView.vue`/`api/codingRuns.js`、
`SqlCodingRunTests.cs`、文档与任务文件。

**修改**：`AgentOSDbContext`、`DependencyInjection`、`Program`、路由/菜单、测试清理 SQL、
`DATABASE-ER.md`、`DEVELOPMENT.md`。

## 7. 下一阶段

AOS-013b：校验命令执行与结果回传、独立工作区、细粒度状态机与 Runner 恢复；之后进入
AOS-014 Review Agent。
