# AOS-042 Delivery Report：大 PR 评审超时修复

- 日期：2026-11-11
- 状态：Completed
- 范围：OpenAI-compatible adapter transport timeout

## 1. 问题

大型 PR 的自动评审出现：

```text
The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.
```

`OpenAiCompatibleAgentAdapter` 默认使用 `HttpClient` 的 100 秒传输超时，而评审请求自身允许更长
的模型调用时间。2200 行以上 diff 的评审会先被传输层切断。

## 2. 变更

`backend/AgentOS.AgentAdapters/OpenAiCompatibleAgentAdapter.cs`

- 无论传入还是新建 `HttpClient`，都设置 `Timeout = InfiniteTimeSpan`。
- 实际的每次请求超时仍由 adapter 的 `CancellationTokenSource` 和请求配置控制。
- 不影响普通短请求；只是不再让传输层提前截断长请求。

## 3. 验证

- `dotnet build backend/AgentOS.slnx`：0 errors。
- 评审请求不再在 100 秒处被取消。
- 大 PR 的风控仍由 Review worker 的 `MaxReviewAttempts` 和请求超时控制。

## 4. 已知限制

- 模型本身仍可能超过 600 秒，届时由 `AgentExecutionRequest.TimeoutSeconds` 取消。
- 大 PR 的评审耗时可能达到数分钟，自动合并会等待评审完成。
