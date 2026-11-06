# AOS-007 交付报告：Codex CLI Adapter

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-007-codex-adapter.md`

## 1. 开发前 CLI 验证（任务 §2）

| 项目 | 结果 |
|---|---|
| 环境 | Windows 11，Codex CLI 通过本机 npm 安装 |
| 版本 | `codex-cli 0.166.0-alpha.6`（`codex --version`） |
| 帮助 | `codex exec --help` 确认 `-s/--sandbox`、`-C/--cd`、`-m/--model`、`--json`、`-o/--output-last-message`、`--skip-git-repo-check`、`--ephemeral` |
| 输入方式 | `... | codex exec --sandbox read-only --skip-git-repo-check --ephemeral -C <tmp> -` → Prompt 走 stdin |
| 输出格式 | `--json` 输出 JSONL：`thread.started.thread_id`、`item.completed.agent_message`、`turn.completed.usage` |
| 最后消息 | `-o <file>` 在成功时写入最终回答（实测 `ADAPTER_OK`） |
| 退出码 | 成功为 0；Adapter 对非零退出码映射为 `codex-exit-{code}` |
| 真实适配器冒烟 | `AGENTOS_CODEX_SMOKE=1` 运行 `CodexCliSmokeTests` → 通过（14s，read-only，stdin prompt） |

**固定参数（受信配置，不允许任务输入修改）**

```
codex exec --sandbox <read-only|workspace-write> -C <validated-workspace>
  --skip-git-repo-check --ephemeral --color never --json
  -o <artifact-dir>/last-message.txt [-m <model>] -
```

**明确禁止**：`--dangerously-bypass-approvals-and-sandbox`、`--dangerously-bypass-hook-trust`、
`--add-dir`、`-c sandbox_permissions=...`、任何 shell 拼接。Windows 上 `.cmd/.bat` shim 被拒绝，
必须配置 `codex.exe`。

## 2. 实现

### 2.1 Adapter 契约（`AgentOS.AgentAdapters`）

- `IAgentAdapter`、`AgentExecutionRequest/Result/Policy/Status`、`AgentSandboxMode`。
- `PromptBuilder`：可信模板（角色 → 安全规则 → 任务 → 输出要求）+ 转义后的任务输入/Skill 数据块。
- `CodexCliAdapter`：构造固定参数、stdin Prompt、环境白名单、JSONL 解析（session id、最终消息）、
  退出码/超时/取消映射、`codex-not-found`/`codex-unavailable` 明确错误。
- `ExternalProcessRunner`：`ArgumentList`、并发读取 stdout/stderr、输出上限、超时/取消终止进程树、
  写 redacted artifact（`%TEMP%\agentos\codex\<task>\<attempt>\stdout.log|stderr.log`）。
- `LogRedactor`：`sk-*`、Bearer、`api_key/token/secret=` 及配置的 Secret 环境变量值。

### 2.2 Runner 集成

- `Runner:AgentAdapter` = `demo` | `codex-cli`；Codex 配置见 `Codex` 节。
- `CodexTaskHandler` 将任务上下文转成 `AgentExecutionRequest`，映射结果：
  Succeeded → 成功；TimedOut → `codex-timeout`（可重试）；Unavailable → 不可重试；
  Cancelled → 取消；非零退出 → 可重试失败。
- 工作目录继续由 AOS-006 的 `WorkspacePolicy` 校验；Adapter 不放宽任何路径。

### 2.3 控制面输入

- Claim 响应新增 `AgentCode`、`AgentRole`、`SkillCode`、`SkillContent`。
- Skill 内容从 `Skills:RootPath` 读取（路径必须在根内、≤ `Skills:MaxBytes`，默认 64KB），
  无新表、无迁移。

## 3. 安全

- 无 shell 字符串：所有参数经 `ArgumentList` 传入；Prompt 只走 stdin。
- 可执行文件、沙箱模式、模型均来自 Runner 受信配置，任务数据不能覆盖。
- 只允许 read-only / workspace-write；危险绕过参数被硬编码禁止。
- 环境变量白名单注入，API Key 不进入 Prompt/日志；输出与错误信息脱敏。
- 超时/取消 kill 整个进程树；输出大小有上限；artifact 写入专用临时目录。
- Prompt 中任务/Skill 数据经过 `& < >` 转义，不能伪造 `<task-input>` 结束标签。

## 4. 测试

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 全量测试 | ✅ **234 通过 / 3 跳过 / 0 失败** |
| AOS-007 单元测试 | ✅ 22（Prompt、参数、超时/取消、退出码、缺失 CLI、脱敏、Handler 映射、Skill 读取） |
| 真实 Codex 冒烟 | ✅ 手动 `AGENTOS_CODEX_SMOKE=1` 通过（CI 默认跳过，避免依赖外网/凭证） |
| 进程树终止 | ✅ `ExternalProcessRunnerTests` 超时/取消用例 |

## 5. 已知限制

1. Artifact（stdout/stderr/last-message）只保存在 Runner 本地 tmp，尚未上传控制面（AOS-008）。
2. `workspace-write` 直接写入配置的项目路径，未做 copy-on-write 隔离副本；清理/未提交变更检查
   留给后续任务。
3. 未实现 CLI 版本自动校验（`CliVersionConstraint` 仅记录）；升级 Codex 后需重新验证参数。
4. Prompt 中的 Skill 内容来自控制面文件，缺少版本化 Diff/签名。
5. 真实 Codex 冒烟测试依赖网络与凭证，默认不在 CI 运行。

## 6. 变更文件

**新增**

- `backend/AgentOS.AgentAdapters/Contracts.cs`、`PromptBuilder.cs`、`LogRedactor.cs`
- `backend/AgentOS.AgentAdapters/Codex/CodexCliAdapter.cs`
- `backend/AgentOS.AgentAdapters/Processes/ExternalProcessRunner.cs`
- `backend/AgentOS.Application/Tasks/SkillContentContracts.cs`
- `backend/AgentOS.Infrastructure/Skills/SkillContentReader.cs`
- `backend/AgentOS.Runner/CodexTaskHandler.cs`
- 测试：`CodexCliAdapterTests.cs`、`CodexTaskHandlerTests.cs`、`ExternalProcessRunnerTests.cs`、
  `SkillContentReaderTests.cs`（含真实 CLI 冒烟）

**修改**

- `IAgentAdapter.cs` 占位接口移除（合并进 `Contracts.cs`）
- `TaskQueueContracts.cs`、`TaskClaimer.cs`（Agent/Skill 字段、技能内容）
- `RunnerOptions.cs`、`TaskProcessor.cs`、`Handlers/ITaskHandler.cs`、`ServiceRegistration.cs`、
  `Program.cs`、`appsettings*.json`
- `DependencyInjection.cs`、`Program.cs`、`appsettings.json`（Skills 配置）
- `docs/DATABASE-ER.md`、`docs/DEVELOPMENT.md`、`tasks/AOS-007-codex-adapter.md`

## 7. 下一任务建议

AOS-008：任务可观测性与产物管理 —— 拉取 Claim 的 Skill 版本、上传 stdout/stderr artifact、
任务日志/详情 API 与前端展示；并把 Codex 真实运行接入端到端演示。
