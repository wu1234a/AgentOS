# AOS-007 独立代码审查报告

**审查对象：** commit `5ee364d`（AOS-007 Codex CLI Adapter）
**审查方式：** Codex CLI `codex exec review --commit 5ee364d`
**结论：** 3 个 P1、1 个 P2，全部修复并新增回归测试。

## 1. 发现与处理

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | 流式输出按读取块回调，凭证可能被拆到两条日志中，逐块脱敏无法识别，导致部分凭证被上传到控制面 | `ExternalProcessRunner` 改为按**完整行**回调（`LineBuffer`），Adapter 对整行脱敏后再上传；新增跨读取边界的单元测试 |
| 2 | P1 | Codex `-o` 会把最终消息原样写入 artifact，Adapter 只对返回的 summary 脱敏，原始文件仍保留凭证 | 读取后立即用脱敏后的 summary 覆盖 `last-message.txt`；新增断言文件内容已脱敏的测试 |
| 3 | P1 | Skill 路径只做词法前缀检查，符号链接/目录联接可让文件实际位于 skills root 之外，内容被读取并发送给模型 | 读取前检查文件与所有父目录的 reparse point，拒绝符号链接；新增符号链接用例（Windows 无权限时跳过） |
| 4 | P2 | 超时/取消源在写 stdin 之后才创建；若 CLI 不读 stdin 且 Prompt 超过管道缓冲，写入会无限阻塞 | 先创建 timeout/linked CTS，用 watchdog 覆盖 stdin 写入；超时或取消即终止进程树；新增大输入阻塞用例 |

## 2. 修复后验证

- 全量测试：**237 通过 / 4 跳过 / 0 失败**（跳过项为 Windows 无符号链接权限、大小写敏感用例与需显式开启的真实 Codex 冒烟）。
- 真实 Codex 冒烟：`AGENTOS_CODEX_SMOKE=1` 通过。
- 构建 0 错误；CI 结果见对应 run。

## 3. 审查未覆盖项

- 审查为只读环境，未独立执行测试；以上为修复后本地实际运行结果。
