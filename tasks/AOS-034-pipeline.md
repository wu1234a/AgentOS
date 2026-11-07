# AOS-034：自动化流水线可视化（只读 API）

状态：Completed
优先级：P1
依赖：AOS-005/013/014/015
来源：Kimi Planner 草案 `01a1b7d6-75a2-782b-a49d-50a463aca024`（已批准）

## 1. 范围

- `GET /api/v1/pipelines?projectId=&limit=`：最近任务摘要。
- `GET /api/v1/pipelines/tasks/{taskId}`：任务、Attempts、CodingRuns、ReviewRuns、QaRuns 阶段。
- 只读聚合，无迁移/前端/实时推送。

## 2. 不在范围

- 前端图组件、SSE/WebSocket、复杂 DAG/跨任务依赖图。
- 修改任务/执行状态。

## 3. 验收

- [x] 任务流水线阶段聚合。
- [x] 最近任务列表。
- [x] 后端全量测试、CI 通过。
