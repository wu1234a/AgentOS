# AOS-010b 交付报告：模型解析下发到 Runner

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-010b-model-propagation.md`

## 1. 变更

- `ClaimedTask` 增加可选 `ModelProfile`（AOS-010 解析结果，Source 为 task/rule/agent）。
- `TaskClaimer` 领取时调用 `IModelProfileRepository.ResolveAsync(taskId)`，把有效配置附加到
  Claim；解析失败为 null，不影响领取。
- Runner：
  - `TaskExecutionContext` 携带 `ResolvedModelProfile`；
  - `CodexTaskHandler` 校验 Provider=`codex-cli`、ExecutionMode=`cli`，否则返回不可重试的
    `model-provider-unsupported`；
  - 模型传给 `AgentExecutionRequest.Model`，Codex 参数使用 `-m <model>`，覆盖全局默认；
  - 超时取 task/profile/Codex 配置的最小值；
  - 日志记录 Profile Code、Provider、Model、Source。
- 无数据库迁移。

## 2. 测试

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **263 通过 / 5 跳过 / 0 失败** |
| 新增用例 | Claim 附带 agent 级 Profile；Adapter 使用请求模型覆盖默认；Handler 传递模型/超时；不支持的 Provider 不可重试 |
| 前端 | 无改动 |
| CI | 见对应 run |

## 3. 安全

- Claim 只传 `SecretReference` 名称，不传值。
- Provider/ExecutionMode 不匹配直接拒绝，不降级为未受控执行。
- 模型名来自受信 Profile，仍通过 `ArgumentList` 传递，无 shell 拼接。

## 4. 已知限制

1. 仅支持 Codex CLI Provider；Kimi/OpenAI API 需要通用 API Adapter（AOS-012 前置）。
2. `ConfigurationJson` 尚未映射到 Adapter 参数（如 reasoning effort），仅随 Claim 传输。
3. Profile 解析失败时静默回退，任务详情暂不展示“未解析”原因。
4. 现有 `SqlObservabilityTests` Reset 会清理 Workers/ApiClients，避免跨测试在线数污染。

## 5. 变更文件

`TaskQueueContracts.cs`（Claim 字段）、`TaskClaimer.cs`（解析注入）、Runner
`ITaskHandler.cs`/`TaskProcessor.cs`/`CodexTaskHandler.cs`、AgentAdapters
`Contracts.cs`/`CodexCliAdapter.cs`、测试 `SqlModelProfileTests.cs`、`CodexTaskHandlerTests.cs`、
`CodexCliAdapterTests.cs`、`SqlTaskQueueTests.cs`、`SqlObservabilityTests.cs`、文档。

## 6. 下一任务

AOS-012 Planner Agent + Kimi：通用 API Adapter、Planner 任务类型、任务草案人工审批。
