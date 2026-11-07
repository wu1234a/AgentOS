# AOS-030：自动恢复、死信队列和人工接管（第一阶段）

状态：Completed
优先级：P1
依赖：AOS-005 Task Queue、TaskAttempts
来源：Kimi Planner 草案 `01a1b7ba-5585-7b28-8969-b5e1c33b54ef`（已批准）

## 0. 修订

Kimi 草案含死信表、审计事件、复杂策略。本阶段用现有 Task 状态实现：

1. 死信 = Failed/Blocked/Rejected 任务。
2. 查询/过滤/人工 retry/takeover/cancel。
3. 自动恢复：Failed 且 15 分钟未更新、RecoveryCount<3，每 5 分钟最多 20 个。
4. 不新增死信表、不做无限重试、不做复杂审计。

## 1. 范围

- `AutomationTask.RecoveryCount`，迁移 `AddTaskRecoveryCount`。
- `GET /api/v1/dead-letters?projectId=&status=&limit=`。
- `POST /{id}/retry`、`/takeover` → Queued，清 lease/error，AttemptCount=0，RecoveryCount++。
- `POST /{id}/cancel` → Cancelled。
- Background `DeadLetterRecoveryWorker` 每 5 分钟自动恢复 Failed。

## 2. 不在范围

- 独立死信表/消息队列、无限重试、人工审批流。
- 前端、复杂告警、跨项目调度。
- 自动恢复 Blocked/Rejected。

## 3. 安全

- 开发 Key；只操作任务状态。
- 自动恢复上限 3 次、限批量 20。
- 保留错误信息直到重新入队；不删除历史。

## 4. 验收

- [x] 死信列表/重试。
- [x] 自动恢复扫描与上限。
- [x] 后端全量测试、CI 通过。
