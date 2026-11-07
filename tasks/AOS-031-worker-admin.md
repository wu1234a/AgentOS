# AOS-031：多 Runner / Worker 管理（第一阶段）

状态：Completed
优先级：P1
依赖：AOS-006 Runner 注册/心跳、AOS-005 TaskClaimer
来源：Kimi Planner 草案 `01a1b7c2-75d9-70d0-beda-c91f787dc61b`（已批准）

## 1. 范围

- `GET /api/v1/workers`：Worker 列表、状态、心跳、运行任务数。
- `GET /api/v1/workers/stats`：总数/Online/Offline/Disabled/运行任务。
- `POST /{id}/enable|disable`：切换 WorkerStatus；Disabled 不再领取任务（Claimer 已有检查）。
- 不新增表/迁移，无自动扩缩容/调度算法/前端。

## 2. 不在范围

- 自动扩缩容、负载均衡、Worker 分组/标签。
- 前端管理页、审计。
- 禁用时迁移运行中任务。

## 3. 验收

- [x] Worker 列表/统计/启停。
- [x] 禁用后状态持久化；Claimer 拒绝 Disabled。
- [x] 后端全量测试、CI 通过。
