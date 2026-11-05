# AOS-007：Codex CLI Adapter

状态：Not Started  
优先级：P0  
依赖：AOS-006

## 1. 目标

实现 Codex CLI Adapter，使 AgentOS.Runner 可以在受控工作区中启动 Codex
CLI，向其传递任务说明和 Skill
内容，收集输出、退出码和执行时长，并将结果回传 AgentOS。

Codex 是首个适配器，但平台必须通过接口隔离具体 CLI，不能把 Codex
专有参数写入任务队列核心逻辑。

## 2. 开发前验证

Codex CLI 的命令参数和执行模式可能随版本变化。实施前必须： 1. 在目标
Windows/WSL2 环境检查已安装 Codex CLI 版本。 2. 执行 CLI
帮助命令确认当前版本支持的非交互执行参数。 3.
使用临时测试仓库验证输入方式、输出格式、退出码、超时和沙箱行为。 4.
将验证过的 CLI 版本和参数记录在交付报告。 5.
不得复制过期博客中的参数作为事实。

## 3. 适配接口

建议抽象： - `IAgentAdapter` - `AgentExecutionRequest` -
`AgentExecutionResult` - `AgentExecutionEvent`

Request 至少包含： - TaskId / AttemptId - ProjectCode -
WorkingDirectory - Prompt - SkillPaths 或已解析的 Skill 内容 - Timeout -
CancellationToken - ExecutionPolicy

Result 至少包含： - Status - ExitCode - StartedAt / FinishedAt - Stdout
/ Stderr 的 Artifact 引用 - Summary - ErrorCode / ErrorMessage -
ExternalSessionId（如 CLI 可提供）

## 4. 进程安全

- 使用 `ProcessStartInfo.ArgumentList`，禁止拼接 shell 命令字符串。
- 不允许用户输入决定可执行文件路径或任意命令参数。
- Codex 可执行文件路径来自 Runner 受信配置。
- 工作目录必须经过白名单验证。
- 设置超时并终止整个进程树。
- 支持取消令牌。
- 对 stdout/stderr 流式读取，避免缓冲区阻塞。
- 日志脱敏。
- 不将 Runner Secret、API Key 或其他密钥注入 Prompt。
- 禁止默认使用绕过沙箱或审批的危险执行参数。
- 任务内容和 Skill 内容视为不可信/受控输入，外部网页和 Issue
  内容不得覆盖系统安全规则。

## 5. Prompt 组装

Prompt 由可信模板和任务数据组合： 1. Agent 角色说明。 2. 全局安全规则。
3. 项目上下文。 4. Skill SOP。 5. 任务描述和验收标准。 6. 输出报告格式。

任务数据必须作为数据段呈现，不允许被解释为更高优先级的系统指令。

## 6. 执行策略

第一版只允许： - 在隔离工作区读取和修改文件。 -
执行项目允许的构建和测试命令。 - 生成任务报告。

默认禁止： - 生产部署。 - 删除生产数据。 - 修改系统账户或权限。 -
读取浏览器 Cookie 或密码库。 - 读取 Runner 配置中的 Secret。 -
任意网络扫描。 - 任意目录写入。

如果 Codex CLI 当前版本无法提供满足要求的安全执行模式，Adapter
必须拒绝启动任务并给出明确错误，不能退化为无约束 shell 执行。

## 7. 配置

ModelProfile/Adapter 配置应包含： - AdapterCode：`codex-cli` -
ExecutablePath（受信配置） - CliVersionConstraint -
ArgumentsProfile（经验证的参数配置） - TimeoutSeconds - WorkspaceMode -
OutputMode - EnvironmentAllowList

不保存明文 API Key。

## 8. 测试

使用临时 Git 仓库： - 成功执行只读分析任务。 -
成功执行受限文件修改任务。 - CLI 不存在时返回明确错误。 -
参数非法时拒绝执行。 - 工作目录越界时拒绝执行。 - 超时能终止进程树。 -
取消任务能终止进程树。 - stdout/stderr 可收集。 -
非零退出码被正确识别。 - 日志脱敏。 - 并发执行受 Runner 限制。

## 9. 验收标准

- [ ] AgentOS 可通过统一 Adapter 接口启动 Codex。
- [ ] Codex 版本和执行参数已验证并记录。
- [ ] 进程启动无 shell 字符串注入风险。
- [ ] 工作目录经过白名单校验。
- [ ] 超时和取消有效。
- [ ] 退出码和结果正确回传。
- [ ] 无危险的绕过沙箱参数。
- [ ] 失败情况不会被标记为成功。

## 10. 交付

创建 `docs/reports/AOS-007-DELIVERY-REPORT.md`，记录 Codex CLI
版本、验证命令、参数策略、安全限制和测试证据。
