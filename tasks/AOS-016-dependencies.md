# AOS-016：任务依赖与阻塞状态（第一阶段）

状态：Completed
优先级：P1
依赖：AOS-013、AOS-006
来源：Kimi Planner 草案 `01a1b55c-c528-7cd6-93fe-c55313a5de2d`（已批准）

## 0. 修订

Kimi 草案包含子任务 API、四阶段 CodingRun、状态审计与前端，范围过大。本阶段先交付
**依赖基础**：

1. `TaskStatus.Blocked` 与 `TaskDependencies` 表；不新增子任务创建 API。
2. Claimer 只领取依赖全部 Succeeded 的 Queued/RetryWait 任务。
3. `TaskDependencyCoordinator` 在 LeaseRecovery 每轮运行：
   - 全部前置成功 → Blocked 变 Queued；
   - 任一前置 Failed/Cancelled → Blocked 变 Failed（`dependency-failed`），支持传递。
4. 不实现阶段字段、状态审计表、CodingRun 四阶段、前端、并发图编辑。

## 1. 目标

提供最小可用的 Finish-to-Start 任务依赖：依赖未满足不领取、成功后释放、失败后传播。

## 2. 范围

- `TaskStatus.Blocked = 9`（不重排现有值）。
- `TaskDependencies(TaskId, PrerequisiteTaskId)` 唯一复合键，双 FK Restrict。
- Claimer SQL 增加 `NOT EXISTS` 依赖检查（选择与原子领取两处）。
- `TaskDependencyCoordinator`：释放/失败传播，最多 20 轮，批 200。
- `LeaseRecoveryService` 每轮先运行依赖协调。
- 迁移 `AddTaskDependencies`。

## 3. 不在本阶段范围

- 子任务创建/列表 API、父子汇总。
- 状态/阶段审计表、CodingRun 四阶段推进。
- 前端展示、依赖图编辑、动态依赖、环检测 API。
- 自动重试、取消、补偿。

## 4. 安全

- 依赖通过 FK 关联现有任务，跨项目校验留给后续 API。
- Claimer 原子 UPDATE 同时复查依赖，避免绕过。
- 协调器只处理 Blocked，不修改 Running/Succeeded 任务。

## 5. 验收

- [x] 迁移应用，现有任务状态不变。
- [x] Claimer 不领取依赖未满足的任务。
- [x] 前置成功 → 后继 Queued；前置失败 → 后继 Failed `dependency-failed`。
- [x] 传递依赖按轮次传播。
- [x] 后端全量测试、CI 通过。

## 6. 交付

`docs/reports/AOS-016-DELIVERY-REPORT.md`。
