# AOS-043：GLM 模型故障回退链

状态：Completed
优先级：P1
依赖：AOS-011/012 模型适配器、Backlog 执行

## 1. 范围

- 新增 `glm-flash` profile：模型 `glm-5.3-flash`。
- 新增 `glm-flashx` profile：模型 `glm-5.3-flashx`。
- 主 coder profile `3derp-coder-model` 配置回退链：
  1. `glm-5.3`
  2. `glm-5.3-flash`
  3. `glm-5.3-flashx`
  4. `deepseek-v4-pro`
- `OpenAiCompatibleAgentAdapter` 在模型 HTTP 429/401/403/404/408/5xx、请求异常、超时或空内容时，切换下一个模型并重试当前工具步骤。
- 已成功的工具调用历史保留，不会重新执行已完成的写文件/命令。

## 2. 不在范围

- 按任务选择任意回退链 UI。
- 费用预算和回退次数配额。
- Planner/Review 模型的独立回退链（当前只覆盖 coder adapter）。

## 3. 验收

- [x] `glm-5.3-flash` API 可用。
- [x] `glm-5.3-flashx` API 可用。
- [x] 主 profile 回退链持久化。
- [x] GLM 5.3 返回 429 时自动切到下一模型。
- [x] 回退后任务继续执行，不重置消息/工具历史。
- [x] DeepSeek 作为最后回退。
