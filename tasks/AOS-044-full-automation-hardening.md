# AOS-044：全自动化四项硬化

状态：Completed
优先级：P1
依赖：AOS-037/038/041/043

## 1. 范围

- 大型 PR 评审分片：60,000 字符/片，最多 12 片，聚合 findings。
- PR 有序合并：按 PR number 串行推进；前一个未就绪时后续 PR 不合并。
- 分支自动更新：PR 不可 merge 时调用 GitHub update-branch，更新后重新评审。
- Planner 批量 Backlog 回退：默认 profile 失败后按 `BacklogFallbackProfileCodes` 尝试备用 profile，每个 profile 重试 2 次。
- 开机自启验证：使用 `AgentOS Startup` 任务和 watchdog 模拟登录启动。
- 模型回退链已由 AOS-043 提供：GLM 5.3 → flash → flashx → DeepSeek。

## 2. 不在范围

- 有真实文本冲突时由 AI 全自动解决冲突。
- 多 API 实例的分布式合并锁。
- 高风险路径人工审批。

## 3. 验收

- [x] 大 diff 被切分为有界 chunk。
- [x] 评审结果跨 chunk 聚合。
- [x] PR 合并按编号串行。
- [x] 不可 merge 的 PR 自动请求 update-branch。
- [x] Planner 主 profile 失败时尝试备用 profile。
- [x] Startup 任务可由模拟登录启动 API/Runner。
- [x] 构建和测试通过（已知 flaky 单独重跑）。
