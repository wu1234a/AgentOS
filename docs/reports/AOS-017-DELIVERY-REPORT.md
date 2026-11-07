# AOS-017 交付报告：Git Worktree 隔离（MVP）

**状态：Completed**
**完成时间：2026-11-07**
**任务文件：** `tasks/AOS-017-worktree.md`

## 1. 能力

- 新增 `Runner:WorktreeRoot`（可选绝对路径）；未配置时声明 worktree 的任务 fail closed。
- 任务 InputJson 声明 `workspace.mode=git-worktree`、`codingRunId`、完整 `baseRevision`。
- `GitWorktreeManager`：
  - 校验 root/仓库/完整 commit；不 fetch；
  - 每个 Attempt 创建 `<root>/<repoKey>/<aosc-guid>` 和临时分支 `aos/coding-run/<id>`；
  - 验证 `HEAD == baseRevision`；
  - 任务结束后仅清理本次创建、位于 root 内的 worktree 与自有分支。
- `TaskProcessor` 创建 worktree 后用其路径执行 Codex、校验、changeset 采集；`finally` 清理。

## 2. 配置

```json
"Runner": { "WorktreeRoot": "E:\\AgentOS-Worktrees" }
```

任务 InputJson：

```json
{"codingRunId":"...","baseRevision":"<40/64 hex commit>","workspace":{"mode":"git-worktree"}}
```

## 3. 安全

- root 必须绝对路径且不与共享仓库重叠；base 必须完整 16 进制且本地可达。
- Git 通过 `ArgumentList` 调用，无 shell；60s 超时。
- 不 fetch/pull/reset/clean 共享工作区；不自动合并/推送。
- 清理前校验路径在 root 内、分支前缀；只删除本次 worktree/分支。

## 4. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **303 通过 / 5 跳过 / 0 失败** |
| Worktree 集成测试 | ✅ 3（创建/隔离/清理、未知 base、请求解析） |
| CI | 见对应 run |

## 5. 已知限制（后续 AOS-017b）

1. 无本地 worktree 清单/DB 状态；Runner 崩溃后不自动对账/恢复。
2. 清理失败只记录日志并保留现场，无 `CleanupRequired` 状态/API。
3. 未对 `worktree add/remove` 做 per-repo 锁；依赖 Git 自身与单 Runner 实例。
4. 无 API/前端展示 workspace 状态、changeset/诊断产物。
5. AOS-014 changeset 采集在 worktree 内完成；成功后清理，产物已上传。

## 6. 变更文件

**Runner**：`GitWorktreeManager.cs`、`TaskProcessor.cs`、`RunnerOptions.cs`、`ServiceRegistration.cs`。
**测试**：`GitWorktreeManagerTests.cs`、`TaskProcessorTests.cs`、`RunnerServiceTests.cs`。
**文档**：任务文件、`DEVELOPMENT.md`、本报告。
