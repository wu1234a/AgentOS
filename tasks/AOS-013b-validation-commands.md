# AOS-013b：Coding Run 校验命令

状态：Completed
优先级：P1
依赖：AOS-013 Coding Run、AOS-007 Codex Adapter

## 1. 目标

Codex 执行完成后，在 Runner 工作区执行已批准草案声明的校验命令；只有全部命令退出码为 0，
任务才算成功。校验失败必须使 Coding Run 失败，不能因为 Codex 自述完成而误判成功。

## 2. 范围

- 创建 Coding Run 时可传 `validationCommands`（最多 10 条，每条 ≤1000 字符）。
- 快照保存 `validationCommands: [{ command, timeoutSeconds }]`，任务 InputJson 携带。
- Runner `CodexTaskHandler` 在 Adapter 返回 Succeeded 后执行校验：
  - 校验配置非法 → `validation-config-invalid`（不可重试）；
  - 命令非 0 退出 → `validation-failed`（不可重试）；
  - 超时 → `validation-timeout`（不可重试）；
  - 进程无法启动 → `validation-error`（可重试）。
- `ValidationCommandRunner`：
  - 在任务工作区执行；Windows `cmd /d /s /c`，Unix `/bin/sh -c`；
  - 每条命令独立超时（默认 600s，范围 1–1800s），超时杀进程树；
  - 输出上限 4000 字符，写回任务日志（最多 50 行）；
  - 清空 Runner 环境，仅保留 PATH/SystemRoot/TEMP 等必要变量，避免密钥泄露。
- API/前端增加可选校验命令输入（每行一条）。

## 3. 不在本任务范围

- 每个 Attempt 独立 Git worktree（AOS-017）。
- `validating/reporting` 细粒度状态与结果回传确认（后续 013c）。
- 自动修复循环、命令白名单、容器级隔离。
- 校验命令产物归档（当前仅日志）。

## 4. 安全

- 命令来自已批准草案，创建时限制数量/长度；执行前再次校验。
- 不继承 Runner/Kimi 等密钥环境变量。
- 工作目录固定为 Runner 已校验项目路径；不拼接 shell 参数以外的用户输入。
- 输出截断，错误信息不回传宿主连接串。

## 5. 验收

- [x] 校验命令随快照进入任务 InputJson。
- [x] Adapter 成功后执行全部命令；任一失败任务失败且不可重试。
- [x] 非法配置/超时/启动失败有明确错误码。
- [x] 校验命令不能读取 Runner 密钥环境变量。
- [x] 后端全量测试与前端构建通过。

## 6. 交付

`docs/reports/AOS-013b-DELIVERY-REPORT.md`。
