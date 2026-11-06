# AOS-005：Task Queue & Lease Management

状态：Completed  
优先级：P0  
依赖：AOS-004

## 1. 目标

实现 SQL Server
持久化任务队列，支持优先级、并发领取、租约、心跳、超时恢复、重试和取消。此任务建立队列服务端能力；本地常驻
Runner 的完整程序在 AOS-006 实现。

## 2. 任务状态

至少支持： - Pending - Queued - Running - RetryWait - Succeeded -
Failed - Cancelled

AwaitingApproval、Rejected 可以预留枚举，但审批流程由后续里程碑实现。

## 3. 领取机制

实现原子 Claim 用例： - 只领取启用项目下可执行的 Queued 任务。 -
排除未到 NextRunAtUtc 的 RetryWait 任务。 - 按 Priority
和创建时间排序。 - 一次只领取 Runner 声明容量范围内的任务。 -
在同一个数据库事务中锁定任务、更新租约并创建 TaskAttempt。 -
领取成功返回 Task、Attempt、租约 Token 和执行限制。 - 同一个 Task
同时只能有一个有效租约。

SQL Server 可以使用事务和适当锁提示（例如经过验证的
`UPDLOCK`、`READPAST`、`ROWLOCK`）或存储过程实现。必须用真实 SQL Server
并发测试，不得仅凭 EF InMemory 测试通过。

## 4. 租约

默认配置： - LeaseDurationSeconds = 90 - HeartbeatIntervalSeconds = 20 -
TaskTimeoutSeconds = 1800 - MaxAttempts = 2

这些值必须配置化。

租约 Token： - 使用密码学安全随机值。 - API 只向当前 Runner 返回明文
Token 一次。 - 数据库保存 Token 哈希。 - 后续
Start、Heartbeat、Complete、Fail 必须验证 WorkerId、TaskId、AttemptId 和
Token。 - 不能只凭 TaskId 更新任务。

## 5. 后台恢复

AgentOS.Worker 定期： - 扫描过期租约。 - 将失联任务 Attempt 标记为
Lost/Failed（明确状态）。 - 如果还有重试次数，进入 RetryWait
并设置退避时间。 - 超过最大次数则 Failed。 - 处理未完成 Outbox。 -
清理过期临时记录（必须遵循保留策略）。

重试建议采用指数退避并设置上限，避免所有失败任务同时重试。

## 6. API

- `POST /api/v1/runners/claim`
- `POST /api/v1/runners/tasks/{taskId}/start`
- `POST /api/v1/runners/tasks/{taskId}/heartbeat`
- `POST /api/v1/runners/tasks/{taskId}/complete`
- `POST /api/v1/runners/tasks/{taskId}/fail`
- `POST /api/v1/tasks/{taskId}/cancel`

Runner API 必须使用 Runner 身份验证。普通用户不能冒充 Runner 完成任务。

## 7. 取消行为

- Queued 任务可直接取消。
- Running 任务标记 CancelRequested，Runner
  在心跳/控制通道收到后终止子进程。
- 取消完成后记录终止原因。
- 不能把取消误记为成功。

## 8. 验收标准

- [ ] 多 Runner 并发领取不会领取同一任务。
- [ ] Claim 和 Attempt 创建具有事务一致性。
- [ ] Heartbeat 可续租。
- [ ] 无效或过期租约不能更新任务。
- [ ] 过期租约可自动恢复。
- [ ] 重试次数和退避正确。
- [ ] 取消任务状态正确。
- [ ] 服务重启后任务仍存在。
- [ ] 真实 SQL Server 并发测试通过。

## 9. 交付

创建
`docs/reports/AOS-005-DELIVERY-REPORT.md`，包含并发测试数据、租约状态图和失败恢复说明。
