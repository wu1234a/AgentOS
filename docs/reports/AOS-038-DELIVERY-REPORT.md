# AOS-038 Delivery Report：连续执行基础与开机自启

- 日期：2026-11-11
- 状态：Completed
- 范围：base main 同步、Watchdog、Runner 自启、登录启动

## 1. 变更文件

Runner：

- `backend/AgentOS.Runner/RunnerOptions.cs`
- `backend/AgentOS.Runner/GitWorktreeManager.cs`
- `backend/AgentOS.Runner/TaskProcessor.cs`
- `tests/AgentOS.Api.Tests/TaskProcessorTests.cs`

部署脚本（本机 `E:\AgentOS.Prod`）：

- `start-runner.ps1`（新增）
- `watchdog.ps1`（增加 main 同步与 Runner 检查）
- Windows 任务 `AgentOS Startup`（登录时触发 Watchdog）

文档：

- `tasks/AOS-038-continuous-runner-startup.md`（新增）
- `docs/reports/AOS-038-DELIVERY-REPORT.md`（本文件）

## 2. 行为

### worktree 前同步

自动 worktree 创建前：

```text
git status --porcelain --untracked-files=all
git rev-parse --abbrev-ref --symbolic-full-name @{u}
git pull --ff-only --prune
```

- 工作区不干净：跳过同步，不覆盖用户改动。
- 没有 upstream：跳过同步。
- 同步失败：记录警告，继续使用当前 HEAD。

### Watchdog

每 5 分钟：

1. 3dERP 工作区干净时 `git pull --ff-only`。
2. 确保 AgentOS API 运行。
3. 确保 AgentOS Runner 运行。
4. 确保 cloudflared 运行并同步 GitHub Webhook。

### 登录启动

新增 Windows 任务：

```text
TaskName: AgentOS Startup
Trigger:  AtLogOn (wxj)
Action:   wscript.exe //B run-hidden.vbs watchdog.ps1
```

### 连续领取

Runner 继续每 2 秒轮询任务队列；队列中有任务时自动领取下一个。

## 3. 验证

Watchdog 实测：

```text
Started AgentOS API.
Started AgentOS Runner.
Synced 3dERP main: Already up to date.
```

Runner：

```text
3derp-local Online
```

构建：

```text
dotnet build backend/AgentOS.slnx → 0 errors
```

## 4. 安全

- 只对干净工作区执行 fast-forward。
- 不使用 force pull/reset。
- 不覆盖未提交改动。
- API/Runner 密钥只从 User 环境变量注入，不写入脚本。
- Watchdog 只启动缺失进程，不终止已有进程。

## 5. 已知限制

- Watchdog 默认 5 分钟一次，合并后 main 的同步可能有最长约 5 分钟延迟；下一个任务创建 worktree 前还会再同步一次。
- 没有 backlog 自动生成；只有已入队任务会被连续领取。
- 同步失败只记录警告，不阻塞任务。
- 没有合并冲突自动 rebase。
- Runner 使用 Debug 构建目录，生产部署仍依赖该目录。

## 6. 后续

1. 任务 backlog 表和自动生成下一个任务。
2. 合并冲突自动 rebase/re-run。
3. 失败任务自动生成修复任务。
4. 高风险路径人工审批。
5. Runner 发布版目录与版本化部署。
