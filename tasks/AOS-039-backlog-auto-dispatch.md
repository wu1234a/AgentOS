# AOS-039：任务 Backlog 与自动派发

状态：Completed
优先级：P1
依赖：AOS-004 Rules、AOS-037 自动合并、AOS-038 连续执行

## 1. 范围

- 新增 `BacklogItems` 持久化表。
- `POST /api/v1/backlog` 预置任务，包含 instruction、校验命令和 delivery。
- Backlog Worker 仅当项目没有活动任务时派发下一条。
- 派发走标准 `backlog.task.requested` 事件 → 规则 → 任务链路。
- 跟踪已派发任务的终态：`Completed` / `TaskFailed`。
- 任务完成后自动允许下一条 Pending 项派发。

## 2. 不在范围

- AI 自动生成 backlog 内容。
- 前端 backlog 管理页面。
- 多 API 实例的分布式抢占锁。
- 失败任务自动生成修复项。

## 3. 验收

- [x] Backlog 可创建、列表、取消。
- [x] 无活动任务时自动派发一条。
- [x] 派发后创建标准事件和任务。
- [x] 任务成功后 backlog 标为 Completed。
- [x] 失败任务标为 TaskFailed。
- [x] 端到端清理任务自动合并。
