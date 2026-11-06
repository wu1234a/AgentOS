# AOS-013：Coding Run 编排（第一阶段）

状态：Completed
优先级：P1
依赖：AOS-006 Runner、AOS-007 Codex Adapter、AOS-010b 模型下发、AOS-012 Planner

## 1. 目标

把已批准的 Planner 草案转换为可追踪的 Coding Run：冻结草案快照，创建 Runner 任务，
提供状态查询、取消和重试。执行、租约、Attempt、日志仍由 AOS-006 Runner 负责；本任务
不重复实现执行器，也不替换 Codex Adapter。

## 2. 范围

- `CodingRuns` / `CodingRunEvents` 表（迁移 `AddCodingRuns`）：
  - 草案 ID、内容 SHA-256、不可变快照 JSON、项目/Agent、TaskId、状态、AttemptCount。
  - 事件序列、迁移前后状态、审计时间。
- 服务端 `CodingRunService`：
  - 只接受 `Approved` 草案；校验项目/Agent 启用且同项目；一份草案只能创建一个 Run。
  - 创建 `AutomationTask`（Queued），InputJson 为冻结快照（草案内容、哈希、模型 Profile）。
  - 读取时把任务状态投影为 Run 状态：Queued/Running/RetryWait/Succeeded/Failed/Canceled。
  - 取消：调用现有 TaskQueue 取消；任务进入终态后 Run 进入 Canceled。
  - 重试：仅 Failed/Canceled 可重试，创建新任务并保留旧任务与事件；AttemptCount+1。
- API（`X-AgentOS-Dev-Key`）：
  - `GET /api/v1/coding-runs?status&limit&offset`
  - `GET /api/v1/coding-runs/{id}`（含事件）
  - `POST /api/v1/coding-runs`
  - `POST /api/v1/coding-runs/{id}/cancel`
  - `POST /api/v1/coding-runs/{id}/retry`
- 前端 `/coding-runs`：列表、创建（选择已批准草案/Agent）、详情与事件、取消/重试。

## 3. 不在本任务范围（后续 AOS-013b）

- 在 Runner 内执行草案声明的校验命令；当前成功仍以 Codex 退出状态为准。
- 每个 Attempt 的独立 Git worktree/工作区隔离与清理；当前复用 Runner 已校验的项目工作区。
- 新增 Runner 回调协议/租约表；当前复用 TaskAttempt 与任务状态。
- `preparing/validating/reporting` 细粒度阶段与结果回传确认；当前投影为 Queued/Running/
  RetryWait/终态。
- 异步创建、心跳恢复、自动重试退避；这些仍由 AOS-006 的任务队列负责。

## 4. 安全

- 创建只接受已批准草案；不能通过请求替换草案内容或模型。
- 快照哈希覆盖草案 Markdown；任务 InputJson 只含草案内容与 ID，不含密钥。
- 项目/Agent 必须启用且属于同一项目；一份草案唯一 Run 由数据库唯一索引保证。
- API 沿用 `X-AgentOS-Dev-Key`；返回体不包含密钥、宿主路径或连接串。

## 5. 验收

- [x] 已批准草案创建 Queued Run 和 Queued Task，快照含草案/哈希/Run ID。
- [x] 未批准草案、跨项目/禁用 Agent、重复 Run 被拒绝。
- [x] 取消 Queued Run 后 Run/Task 均为 Canceled。
- [x] 任务 Failed/Succeeded 投影到 Run；事件序列可查询。
- [x] Failed Run 重试创建新任务、AttemptCount+1，旧任务保留。
- [x] 后端全量测试、前端 lint/build 通过。

## 6. 交付

`docs/reports/AOS-013-DELIVERY-REPORT.md`。
