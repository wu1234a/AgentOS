# AOS-035：3DERP 与皇帝模拟器业务事件接入

状态：Completed
优先级：P1
依赖：AOS-002 Event Hub、AOS-004 Rules、Projects
来源：Kimi Planner 草案 `01a1b7da-0759-77fd-9229-b7ea2ace91a6`（已批准）

## 1. 范围

- `POST /api/v1/business-events`：
  - `source` 只允许 `3derp` / `emperor`；
  - `eventType` 组合为 `<source>.<eventType>`；
  - 必填 project/dedupeKey；
  - 可选 subject/payload/occurredAt/summary。
- 通过现有 EventService 写入 Event Hub，进入 Rules/任务管线。
- 幂等由 DedupeKey 保证；重复 200，新事件 202，冲突 409。
- 无迁移/前端/SDK。

## 2. 不在范围

- 业务系统专用 SDK、回调、数据同步。
- 自定义鉴权/签名（复用开发 Key）。
- 前端事件调试台。

## 3. 验收

- [x] 3derp 事件创建 + 重复去重。
- [x] 未知 source 拒绝。
- [x] 后端全量测试、CI 通过。
