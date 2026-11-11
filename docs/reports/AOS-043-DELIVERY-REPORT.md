# AOS-043 Delivery Report：GLM 模型故障回退链

- 日期：2026-11-12
- 状态：Completed
- 范围：GLM coder 模型自动回退

## 1. 背景

GLM 5.3 资源包耗尽：

```text
HTTP 429 code 1113
余额不足或无可用资源包
```

任务连续失败并触发 repair 循环。需要在不修改任务/规则的情况下，自动改用备用模型。

## 2. 模型配置

新增 profile：

```text
code:  glm-flash
model: glm-5.3-flash
provider: glm
secret: ZHIPU_API_KEY

code:  glm-flashx
model: glm-5.3-flashx
provider: glm
secret: ZHIPU_API_KEY
```

主 coder profile `3derp-coder-model`：

```json
{
  "temperature": 0.2,
  "maxTokens": 16384,
  "maxSteps": 100,
  "fallbacks": [
    {"provider":"glm","model":"glm-5.3-flash","secretReference":"ZHIPU_API_KEY"},
    {"provider":"glm","model":"glm-5.3-flashx","secretReference":"ZHIPU_API_KEY"},
    {"provider":"deepseek","model":"deepseek-v4-pro","secretReference":"DEEPSEEK_API_KEY","baseUrl":"https://api.deepseek.com/v1"}
  ]
}
```

## 3. 适配器行为

`OpenAiCompatibleAgentAdapter`：

- 每个工具步骤维护当前模型目标。
- 以下情况切换下一个模型并重试当前步骤：
  - HTTP 401 / 403 / 404 / 408 / 429 / 5xx
  - 网络请求异常
  - 请求超时
  - 模型返回空 content
- 保留 `messages` 和已完成工具调用历史。
- 全部目标失败时返回最后一次错误。
- 回退模型使用自己的 provider/baseUrl/secret。

## 4. 验证

模型直连：

```text
glm-5.3-flash  → HTTP 200
glm-5.3-flashx → HTTP 200
deepseek-v4-pro → OK
```

端到端：

```text
Task: 01a1cb84-5496-78a1-a2fd-3cb7d4994836
using model profile 3derp-coder-model (glm/glm-5.3)
model glm/glm-5.3 returned HTTP 429; trying fallback
→ 任务继续读取仓库并执行
```

验证任务已主动取消，避免产生测试代码。

## 5. 验证命令

```text
dotnet build backend/AgentOS.slnx → 0 errors
```

## 6. 已知限制

- 回退链目前只覆盖 `OpenAiCompatibleAgentAdapter`（coder）。
- Planner 和 Review 使用各自的模型配置，不共享该回退链。
- 没有按项目/任务自定义回退顺序的 UI。
- 回退到 DeepSeek 后，模型能力/工具调用风格可能不同。
- 不自动重置失败的步骤；只重试当前步骤。

## 7. 后续

1. 为 Planner 增加回退链。
2. 为 Review 增加回退链。
3. 回退次数与费用预算。
4. 前端展示当前使用的模型和回退记录。
