# AOS-005 独立代码审查报告

**审查对象：** commit `0fc638c`（AOS-005 Task Queue & Lease Management）
**审查方式：** Codex CLI `codex exec review --commit 0fc638c`（本地沙箱修复后可用）
**结论：** 无 Blocker；1 个 P1、2 个 P2。全部修复并新增回归测试。

## 1. 发现与处理

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | 已请求取消的 Running 任务，如果 Runner 在确认前断线，租约恢复会把它重新放入队列，导致已取消的任务再次执行并消耗尝试次数；`FailAsync` 在 `cancelled=false` 时同样会重排已取消任务 | **已修复**：`LeaseRecoveryService` 对带 `CancelRequestedAtUtc` 的任务直接落为 `Cancelled`（Attempt 记 `Cancelled`），不再重试；`TaskQueueService.FailAsync` 以 `cancelled \|\| CancelRequestedAtUtc != null` 为准。新增单元测试与真实 SQL 用例（取消 → 租约过期 → 恢复为 Cancelled → 永远不可再领取） |
| 2 | P2 | Runner 传入超长 `errorCode`（>100）或 `externalSessionId`（>200）会在写库时被 SQL Server 拒绝，接口返回 500 且失败无法记录 | **已修复**：`errorCode` 截断到 100，`externalSessionId` 截断到 200（`errorMessage` 原本已截断到 2000）；新增边界测试 |
| 3 | P2 | 退避抖动取 `taskId.ToByteArray()[0]`，而 UUIDv7 的前导字节是时间戳，同一批创建的任务会得到相同抖动，恢复时同步重试 | **已修复**：改为对完整 TaskId 做 SHA-256 后取前 4 字节取模 30；新增“同批 UUIDv7 抖动不同”的测试 |

## 2. 修复后验证

- 后端全量测试：**180 通过 / 0 失败**（含 6 个真实 SQL 队列用例）。
- 构建 0 错误；迁移已应用到 `AgentOS_Dev`。
- CI（GitHub Actions）结果见对应 run。

## 3. 审查未覆盖项

- 审查为只读且受本机环境限制，未独立执行测试；上述结果为修复后本地实际运行。
