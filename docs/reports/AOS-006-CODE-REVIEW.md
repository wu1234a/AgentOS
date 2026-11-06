# AOS-006 独立代码审查报告

**审查对象：** commit `3589bc2`（AOS-006 Local Agent Runner）
**审查方式：** Codex CLI `codex exec review --commit 3589bc2`
**结论：** 2 个 P1、4 个 P2，全部修复并新增回归测试。

## 1. 发现与处理

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | Runner 宿主 `Program.cs` 未注册 `TimeProvider`，`TaskProcessor` 无法解析；真实进程领取任务后每个任务都会在执行前失败（测试单独注册了该依赖，掩盖了问题） | 新增 `RunnerServiceRegistration.AddRunnerServices()`，统一注册 `TimeProvider.System`、工作区策略、Handler 与 Processor；`Program` 与 DI 冒烟测试共用，防止再次漂移 |
| 2 | P1 | 客户端凭证认证路径不检查 Worker 状态；`TouchWorkerAsync` 无条件把状态改为 Online，管理员禁用会被下一次心跳“复活”，禁用 Worker 仍可领取 | 客户端认证解析 Worker 并要求未 Disabled（否则 403）；心跳只更新 LastHeartbeat/UpdatedAt，Disabled 保持不变；`TaskClaimer` 额外校验 Worker 状态（纵深防御） |
| 3 | P2 | 心跳连续失败 3 次后取消任务，但该分支把任何非关闭取消都当作 AgentOS 取消，成功上报后任务被永久标记 Cancelled | 引入 `InterruptionState`（Requested / HeartbeatFailure / LeaseLost / Shutdown）；心跳失败上报 `heartbeat-lost`（可重试），租约丢失不再上报，只有明确取消才报 Cancelled |
| 4 | P2 | 达到批量阈值后日志上传是 fire-and-forget，最终 Flush 不等待；完成会清除租约，导致在途日志被 403 丢弃 | `LogBuffer` 跟踪在途上传，`FlushAsync` 先 `WhenAll` 等待在途批次，再发送剩余缓冲；终态上报前完成 drain |
| 5 | P2 | 使用 RegistrationKey 时，API 暂不可达会让 `EnsureRegisteredAsync` 抛异常导致宿主退出，无法自动重连 | 注册对瞬时错误做 2s→60s 指数退避（可取消）；401/400 等永久错误仍快速失败 |
| 6 | P2 | 工作区边界比较固定使用 `OrdinalIgnoreCase`，在大小写敏感的 Linux/WSL2 上可能把不同目录当成同一根目录 | 按平台选择比较方式（Windows 忽略大小写，其他平台区分大小写）；新增仅在 Linux 运行的回归测试 |

## 2. 修复后验证

- 全量测试：**211 通过 / 2 跳过 / 0 失败**（跳过项为当前 Windows 环境无法创建符号链接及大小写敏感用例；
  CI Linux 会执行大小写用例）。
- 新增回归：DI 冒烟测试、禁用 Worker 认证/领取/心跳、心跳失败可重试、日志 drain、注册重试、路径大小写。
- 构建 0 错误；迁移已应用到 `AgentOS_Dev`；CI 结果见对应 run。

## 3. 审查未覆盖项

- 审查为只读环境，未独立执行测试；以上为修复后本地实际运行结果。
