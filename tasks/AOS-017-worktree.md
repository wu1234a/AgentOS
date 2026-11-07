# AOS-017：Git Worktree 隔离（MVP）

状态：Completed
优先级：P1
依赖：AOS-013、AOS-006、AOS-014
来源：Kimi Planner 草案 `01a1b4d5-4098-703a-ac2e-44ad496c6758`（已批准）

## 0. 对 Kimi 草案的修订

Kimi 草案覆盖本地清单、workspace 状态机、API 字段、崩溃对账和前端展示，范围过大。
本阶段先交付**可用的隔离 MVP**：

1. 仅当任务 `InputJson` 声明 `workspace.mode=git-worktree` 时，Runner 为本次 Attempt 创建
   worktree；其他任务保持原共享工作区行为。
2. 不新增 DB/API/前端字段；workspace 状态通过 Runner 日志和任务错误码体现。
3. 不做本地清单/重启对账/自动恢复；清理失败保留现场并记录错误，人工处理。
4. 不自动 fetch/pull/merge/push，不切换共享工作区，不对共享路径 reset/clean。

## 1. 目标

每个 Coding Run Attempt 使用独立 Git worktree：从批准的 `baseRevision` 创建、在其内执行 Codex/
校验/changeset 采集，任务结束后安全清理；两个 Attempt 不共享文件系统。

## 2. 范围

- `Runner:WorktreeRoot` 可选绝对路径；未配置时声明 worktree 的任务直接失败（fail closed）。
- 任务 InputJson 约定：
  ```json
  {
    "codingRunId": "...",
    "baseRevision": "<40/64 位 commit>",
    "workspace": { "mode": "git-worktree" }
  }
  ```
- `GitWorktreeManager`：
  - 校验 root 是绝对路径、已存在或可创建、与项目仓库不重叠；
  - 校验 baseRevision 为完整 16 进制 commit 且 `git cat-file -e <sha>^{commit}` 可达；
  - `git worktree add -b aos/<worktreeId> <path> <base>`，目录
    `<root>/<repoKey>/<worktreeId>`；
  - 创建后校验 `HEAD == baseRevision`；
  - 清理仅对本次创建且路径在 root 内的 worktree 执行 `git worktree remove --force`，再删除自有分支；
  - 所有 Git 通过 `ArgumentList` 调用，超时 60s；不 fetch。
- `TaskProcessor`：
  - 校验 WorkspacePolicy 通过后，按 InputJson 创建 worktree，用其路径执行；
  - 创建失败 → `worktree-create-failed`（不可重试），不启动任务；
  - 任务终态后 `finally` 清理；清理失败记录 `worktree-cleanup-failed` 日志并保留现场，
    不把已成功任务改成失败。
- 日志不含绝对路径/凭据；错误码固定。

## 3. 不在本阶段范围

- 本地 worktree 清单、workspace 状态持久化、API/前端只读状态。
- Runner 崩溃后的自动对账/恢复、清理重试、Retained/CleanupRequired 状态机。
- 自动合并/推送/PR、跨仓库、submodule、网络共享目录。
- 每个 Attempt 的独立锁/并发管理（Git 自身串行化 `worktree add`；MVP 单 Runner 实例）。

## 4. 安全

- root/base 校验；拒绝相对路径、`..`、与仓库重叠、短 SHA/branch/tag。
- 只清理本次创建路径与分支；不递归删除任意目录，不 prune，不 reset/clean 共享工作区。
- Git 参数数组，无 shell 拼接；超时/取消。

## 5. 验收

- [ ] 未配置 root / 非法 base / 非 git 仓库 / 路径越界时任务失败且无 worktree。
- [ ] 合法任务创建独立目录，HEAD 等于 baseRevision，执行路径在 worktree 内。
- [ ] 两个 Attempt 使用不同目录/分支，互不影响。
- [ ] 成功与失败后均清理 worktree 和自有分支；清理失败不删除现场。
- [ ] 未声明 worktree 的既有任务行为不变。
- [ ] Git Worktree Manager 集成测试通过；后端全量测试、CI 通过。

## 6. 交付

`docs/reports/AOS-017-DELIVERY-REPORT.md`。
