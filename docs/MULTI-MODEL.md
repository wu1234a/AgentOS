# AgentOS 多模型平台

状态：第一阶段实现
更新：2026-11-09

## 1. 能力

AgentOS 现在有两个执行通道：

```text
模型 Provider
 ├─ codex-cli         → Codex CLI
 ├─ openai-compatible → Kimi / DeepSeek / GLM / Qwen / Doubao / Hunyuan / Grok / OpenAI
 └─ anthropic         → Claude / Opus Messages API
```

Planner、Reviewer、Coder 都可由 Agent/配置选择 Provider 和 Model。

QA 不使用大模型，继续执行测试命令并记录结果。

## 2. Provider 对照

| Provider 代码 | 类型 | Base URL | 默认 Secret |
|---|---|---|---|
| `kimi` | OpenAI-compatible | `https://api.moonshot.cn/v1` | `KIMI_API_KEY` |
| `deepseek` | OpenAI-compatible | `https://api.deepseek.com/v1` | `DEEPSEEK_API_KEY` |
| `glm` | OpenAI-compatible | `https://open.bigmodel.cn/api/paas/v4` | `ZHIPU_API_KEY` |
| `qwen` | OpenAI-compatible | `https://dashscope.aliyuncs.com/compatible-mode/v1` | `DASHSCOPE_API_KEY` |
| `doubao` | OpenAI-compatible | `https://ark.cn-beijing.volces.com/api/v3` | `ARK_API_KEY` |
| `hunyuan` | OpenAI-compatible | `https://api.hunyuan.cloud.tencent.com/v1` | `HUNYUAN_API_KEY` |
| `grok` | OpenAI-compatible | `https://api.x.ai/v1` | `XAI_API_KEY` |
| `openai` | OpenAI-compatible | `https://api.openai.com/v1` | `OPENAI_API_KEY` |
| `anthropic` | Anthropic Messages | `https://api.anthropic.com` | `ANTHROPIC_API_KEY` |
| `codex-cli` | CLI | - | Codex 自身配置 |

也可以使用自定义：

```text
Provider: openai-compatible
ConfigurationJson: {"baseUrl":"https://your-gateway/v1"}
SecretReference: YOUR_API_KEY
```

## 3. ModelProfile 示例

在 `/models` 新建：

### DeepSeek Planner

```text
Code: deepseek-planner
Provider: deepseek
Model: deepseek-chat
Execution Mode: api
Secret Reference: DEEPSEEK_API_KEY
Configuration JSON: {"temperature":0.2,"maxTokens":8192}
```

### GLM Coder

```text
Code: glm-coder
Provider: glm
Model: glm-4
Execution Mode: api
Secret Reference: ZHIPU_API_KEY
Configuration JSON: {"temperature":0.1,"maxTokens":8192}
```

### Qwen Coder

```text
Code: qwen-coder
Provider: qwen
Model: qwen-plus
Execution Mode: api
Secret Reference: DASHSCOPE_API_KEY
Configuration JSON: {}
```

### GPT Reviewer

```text
Code: gpt-reviewer
Provider: openai
Model: gpt-4o
Execution Mode: api
Secret Reference: OPENAI_API_KEY
Configuration JSON: {}
```

### Opus Reviewer

```text
Code: opus-reviewer
Provider: anthropic
Model: claude-opus-4-20250514
Execution Mode: api
Secret Reference: ANTHROPIC_API_KEY
Configuration JSON: {}
```

### Codex Coder

```text
Code: codex-coder
Provider: codex-cli
Model: 你的 Codex 模型 ID（可留空）
Execution Mode: cli
Secret Reference: 留空
Configuration JSON: {}
```

## 4. Planner 多模型

Planner 使用 `api` 模式的 ModelProfile。

已支持 Provider 默认 Base URL：

```text
kimi / deepseek / glm / qwen / doubao / hunyuan / grok / openai
```

允许的域名已加入 `Planner:AllowedProviderHosts`：

```text
api.moonshot.cn
api.openai.com
api.deepseek.com
open.bigmodel.cn
dashscope.aliyuncs.com
ark.cn-beijing.volces.com
api.hunyuan.cloud.tencent.com
api.x.ai
```

设置默认 Planner：

```json
"Planner": {
  "DefaultModelProfileCode": "deepseek-planner"
}
```

## 5. Reviewer 多模型

`Review` 配置：

```json
"Review": {
  "Provider": "openai",
  "Model": "gpt-4o",
  "BaseUrl": null,
  "SecretReference": "OPENAI_API_KEY",
  "TimeoutSeconds": 600
}
```

- `Provider=codex-cli`：走 CodexReviewAgent
- 其他 Provider：走 OpenAI-compatible / Anthropic API Reviewer

Reviewer 只读，不修改代码。

## 6. Coder 多模型

Runner 配置：

```json
"Runner": {
  "AgentAdapter": "multi",
  "BaseUrl": "http://127.0.0.1:5099",
  "DevApiKey": "...",
  "WorkerCode": "local-runner-01",
  "Projects": [
    { "Slug": "agentos", "Path": "E:\\Projects\\AgentOS" }
  ]
},
"Codex": {
  "SandboxMode": "workspace-write",
  "ExecutablePath": "C:\\Users\\wxj\\AppData\\Local\\OpenAI\\Codex\\bin\\...\\codex.exe",
  "TimeoutSeconds": 1800
}
```

`AgentAdapter=multi` 时：

- ModelProfile.Provider = `codex-cli` → Codex
- ModelProfile.Provider = `openai-compatible` / `kimi` / `deepseek` / `glm` / `qwen` / `doubao` / `hunyuan` / `grok` / `openai` → 通用 Tool Loop
- ModelProfile.Provider = `anthropic` → Anthropic Tool Loop

Coder Agent 必须：

```text
Permission: workspace-write
```

通用 Tool Loop 工具：

- list_files
- read_file
- write_file
- run_command

限制：

- 只能访问 workspace
- 最多 24 步
- 命令 120 秒超时
- 文件读取 200KB
- 命令输出 100KB

## 7. Agent 路由

在 `/agents` 给每个 Agent 选择默认 ModelProfile：

| Agent | 角色 | ModelProfile |
|---|---|---|
| 3derp-planner | planner | deepseek-planner |
| 3derp-coder | coder | glm-coder |
| 3derp-qa | qa | 不需要模型 |

Reviewer 不建 Agent，使用 `Review` 配置。

## 8. QA

QA 不接大模型：

```text
QaRun
 → 执行测试命令
 → 记录通过/失败
 → 生成结论
```

需要 LLM 分析时，另建 Agent 任务，不属于 QA 执行器。

## 9. 安全

- API Key 只放环境变量或 SecretReference，不写数据库/仓库。
- 通用 Tool Loop 的 `run_command` 只在 workspace-write 开启。
- 命令限制在 workspace，路径穿越会被拒绝。
- Reviewer 强制只读。
- 工具输出会回传模型，注意不要把密钥放进工作区。

## 10. 已知限制

1. 不同厂商的 tool calling 兼容性不完全一致。
2. 暂无流式输出、视觉输入和 token 级用量统计。
3. 通用 Tool Loop 为 MVP，复杂仓库可能需要更大步数/上下文。
4. Anthropic 为 Messages API 基础适配。
5. Doubao/Hunyuan 的模型 ID 和端点需按实际账号填写。
6. 模型价格/上下文窗口尚未进入统一目录。
7. QA 不使用 LLM 是刻意设计。
