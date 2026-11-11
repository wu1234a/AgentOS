# AOS-040：失败 Backlog 自动修复

状态：Completed
优先级：P1
依赖：AOS-039 Backlog 自动派发

## 1. 范围

- Backlog 任务失败时自动生成一条 repair item。
- Repair item 继承原 instruction、校验命令和 delivery。
- Repair 分支重新生成，避免与原 PR 分支冲突。
- 原始 item 标记 `RepairScheduled`。
- Repair 自身再次失败时标记 `TaskFailed`，不无限循环。
- 默认最多 1 次 repair，可配置 `Backlog:MaxRepairAttempts`。

## 2. 不在范围

- 从失败日志中自动定位并修复代码。
- 多级修复链。
- 人工审批。
- 前端展示。

## 3. 验收

- [x] 失败任务生成 repair item。
- [x] repair 包含失败原因和原任务指令。
- [x] 原任务标记 RepairScheduled。
- [x] repair 失败后标记 TaskFailed。
- [x] 不超过最大 repair 次数。
- [x] 构建和测试通过。
