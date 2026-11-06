# AOS-010b：模型解析下发到 Runner

状态：Completed
优先级：P0
依赖：AOS-010（ModelProfile 注册/解析）、AOS-006/007（Runner、Codex Adapter）

## 1. 目标

把 AOS-010 的模型解析结果真正带到执行侧：Claim 响应携带 Task → Rule → Agent 解析出的
有效 ModelProfile，Runner 用它决定 Codex `-m` 模型、超时与 Provider 校验。AOS-012 的
Kimi Planner 接入依赖本任务。

## 2. 范围

- `ClaimedTask` 增加 `ModelProfile`（Code/Provider/Model/ExecutionMode/ConfigurationJson/
  SecretReference/TimeoutSeconds/Source），解析顺序复用 AOS-010。
- TaskClaimer 在领取时解析并附带；解析失败不阻止领取（Adapter 回退 Runner 配置）。
- Runner `AgentExecutionRequest` 增加 `Model`；Codex CLI 使用
  `request.Model ?? Codex:Model`。
- `CodexTaskHandler`：
  - 仅支持 Provider=`codex-cli`、ExecutionMode=`cli`，否则返回不可重试的
    `model-provider-unsupported`；
  - 超时取 `min(task.TimeoutSeconds, profile.TimeoutSeconds, Codex:TimeoutSeconds)`；
  - 模型传入 Adapter。
- 日志中记录实际使用的 Model/Provider/Source（不含密钥值）。

## 3. 不在本任务范围

- Kimi/OpenAI API Adapter（AOS-012 前置的通用 API Adapter）。
- 密钥值下发；Runner 仍从环境变量读取密钥。
- 费用/配额执行（AOS-029）。

## 4. 安全

- Claim 只传 `SecretReference` 名称，不传值。
- Provider/ExecutionMode 不匹配时拒绝执行，不降级为未受控命令。
- 模型名来自受信配置，不拼接进 shell；Codex 参数仍走 ArgumentList。

## 5. 验收

- [ ] Claim 返回解析后的 ModelProfile（task > rule > agent，禁用回退）。
- [ ] Codex Adapter 使用 Claim 的模型覆盖全局默认。
- [ ] Provider/ExecutionMode 不匹配时任务失败且不可重试。
- [ ] 全量测试（含 SQL）通过。

## 6. 交付

创建 `docs/reports/AOS-010b-DELIVERY-REPORT.md`。
