# AOS-028：多项目注册、隔离与配额（第一阶段）

状态：Completed
优先级：P1
依赖：AOS-004/005、现有 Projects
来源：Kimi Planner 草案 `01a1b753-8969-74ae-9daa-b3364fc01a43`（已批准）

## 0. 修订

Kimi 草案含项目 CRUD、配额、projectId 过滤、资源归属校验、审计。本阶段先交付：

1. Projects API + MaxConcurrentTasks。
2. Claimer 原子配额检查（运行中任务数 < 配额）。
3. 不做 Task/Agent/Rule/CodingRun 的 projectId 查询过滤（后续 028b）。
4. 不做前端/审计/多租户。

## 1. 范围

- `Project.MaxConcurrentTasks`（NULL=不限），迁移 `AddProjectQuota`。
- API `/api/v1/projects`：GET/POST/PATCH/enable/disable；slug 唯一、配额 1–10000。
- Claimer SQL：
  - 候选与原子领取都检查项目配额；
  - 项目停用不可领取（Project join 已有，ClaimOne 加 EXISTS）。
- 无前端。

## 2. 不在范围

- 多租户认证/成员/计费。
- projectId 查询过滤、资源归属校验、审计日志。
- 前端、CodingRun 项目配额联动。
- 停用项目时终止运行中任务。

## 3. 安全

- Projects 写接口使用开发 Key。
- 配额检查与领取在同一 SQL 更新内完成。
- 调低配额不终止运行任务；仅阻止新领取。
- 停用项目不删除历史数据。

## 4. 验收

- [x] 项目创建/重复 slug/更新配额/启停。
- [x] 配额=1 且已有 Running 任务时，Queued 任务不被领取。
- [x] 后端全量测试、CI 通过。
