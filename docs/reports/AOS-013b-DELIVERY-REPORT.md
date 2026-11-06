# AOS-013b 交付报告：Coding Run 校验命令

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-013b-validation-commands.md`

## 1. 能力

- 创建 Coding Run 时可声明最多 10 条校验命令，随不可变快照进入任务 InputJson。
- Runner 在 Codex Adapter 成功后按顺序执行；全部退出码 0 才返回成功。
- 失败码：`validation-config-invalid`、`validation-failed`、`validation-timeout`（不可重试），
  `validation-error`（可重试）。
- 命令在任务工作区执行，独立超时、输出截断、禁止继承 Runner 密钥环境变量。

## 2. 数据与 API

- 无数据库迁移；校验命令保存在 `CodingRuns.SnapshotJson` 与任务 `InputJson`。
- `POST /api/v1/coding-runs` 新增可选字段 `validationCommands: string[]`。
- 前端 `/coding-runs` 创建表单新增“校验命令”多行输入（每行一条）。

## 3. 安全

- 最多 10 条、每条 ≤1000 字符；创建与执行前均校验。
- 清空环境，仅保留 PATH/SystemRoot/WINDIR/TEMP/TMP/USERPROFILE/COMSPEC/PATHEXT/HOME
  （Unix 为 PATH/HOME/TMPDIR/LANG 等），不传递 `KIMI_API_KEY`。
- 工作目录固定为 Runner 已校验的 workspace；命令输出最多 4000 字符、日志最多 50 行。
- 超时杀整个进程树。

## 4. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **285 通过 / 5 跳过 / 0 失败** |
| 新增 Handler 用例 | ✅ 3（执行校验、失败不可重试、非法配置） |
| 新增 Runner 用例 | ✅ 4（成功输出、非 0、密钥隔离、超时杀进程） |
| 前端 lint / build | ✅ 通过 |
| CI | 见对应 run |

## 5. 已知限制

1. 校验命令在项目现有工作区执行，未创建独立 worktree（AOS-017）。
2. 没有命令白名单/容器隔离；安全边界依赖 Runner 主机与已批准草案。
3. Run 状态仍由任务状态投影，未单独暴露 `validating/reporting` 阶段。
4. 校验输出只进任务日志，不单独归档为 Artifact。
5. 校验失败不会自动修复或重跑；需人工判断后重试或修改方案。

## 6. 变更文件

**Runner**：`IValidationCommandRunner.cs`、`ValidationCommandRunner.cs`、
`CodexTaskHandler.cs`、`ServiceRegistration.cs`。

**Application/API/UI**：`CodingRunContracts.cs`、`CodingRunService.cs`、
`CodingRunsView.vue`。

**测试**：`CodexTaskHandlerTests.cs`、`ValidationCommandRunnerTests.cs`、
`SqlCodingRunTests.cs`。

**文档**：任务文件、`DEVELOPMENT.md`、本报告。

## 7. 下一步

AOS-014 Review Agent，或 AOS-017 Git Worktree 自动隔离。
