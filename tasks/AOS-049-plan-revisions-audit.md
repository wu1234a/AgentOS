# AOS-049：立项版本历史与审批审计

状态：Completed
优先级：P1
依赖：AOS-045、AOS-047、AOS-048

## 1. 范围

- 新增 ProjectPlanRevisions 快照表。
- 每次规划完成写入一条不可变快照。
- 提供版本列表 API。
- 前端“版本历史”标签页和快照查看。
- 方案批准、拒绝、启动批准记录操作者。
- 操作者来自 `X-AgentOS-Actor` 请求头。

## 2. 验收

- [x] 生成 PendingReview 时写入 Revision 快照。
- [x] `GET /project-plans/{id}/revisions` 返回快照。
- [x] 前端显示版本、模型、Tokens、费用、意见、时间。
- [x] 前端可查看版本 JSON。
- [x] 批准人/启动批准人落库。
- [x] 修订后清空旧批准信息。
