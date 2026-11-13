# AOS-046：立项后续控制

状态：Completed
优先级：P1
依赖：AOS-045

## 1. 范围

- 批准后的计划支持把 Hold Backlog 恢复为 Pending，并启动执行。
- 按 Epic 汇总 Backlog 进度：Pending / Hold / 执行中 / 完成 / 失败。
- 人工反馈触发计划修订：版本 +1，重新运行规划委员会。
- 前端提供启动执行、进度表和修订意见输入。

## 2. API

```text
POST /api/v1/project-plans/{id}/release
GET  /api/v1/project-plans/{id}/progress
POST /api/v1/project-plans/{id}/revise
```

## 3. 约束

- release 只允许 Approved 计划。
- revise 只允许 PendingReview 或 Failed 计划。
- revise 需要 5-8000 字符人工意见。
- release 只处理 Hold，不直接改 Running/Completed/Failed。

## 4. 验收

- [x] release 返回释放数量。
- [x] progress 按 Epic 汇总。
- [x] 修订意见写入计划并递增 Revision。
- [x] 前端展示进度与启动按钮。
- [x] 前端展示修订输入框。
