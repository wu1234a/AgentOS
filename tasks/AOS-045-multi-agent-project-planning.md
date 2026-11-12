# AOS-045：多 Agent 项目立项与路线图 MVP

状态：Completed
优先级：P1
依赖：AOS-012 Planner、AOS-039 Backlog、AOS-041 批量 Backlog

## 1. 范围

- 新增项目级 `ProjectPlan` 和 `ProjectPlanContribution`。
- 三个分析角色：Product、Architecture、Cost/Risk。
- 两轮讨论：独立提案 + 交叉评审。
- Chair 综合为结构化 JSON：推荐方案、路线图、风险、验收指标、任务列表。
- 人工批准后自动创建带 `PlanId` / `EpicKey` 的 Backlog。
- 生成过程异步执行，最多 7 次模型调用。

## 2. API

```text
POST /api/v1/project-plans
GET  /api/v1/project-plans
GET  /api/v1/project-plans/{id}
POST /api/v1/project-plans/{id}/approve
POST /api/v1/project-plans/{id}/reject
```

## 3. 不在范围

- 自动执行批准后的计划（仍进入 Backlog 队列）。
- 7+ Agent 长对话。
- 多轮无限辩论。
- 前端立项评审页面。
- 真实预算计费。

## 4. 验收

- [x] 3 个角色独立提案。
- [x] 3 个角色交叉评审。
- [x] Chair 输出结构化路线图与任务。
- [x] 人工批准后创建 Backlog。
- [x] Backlog 带 PlanId/EpicKey。
- [x] 模型失败有 DeepSeek 回退。
