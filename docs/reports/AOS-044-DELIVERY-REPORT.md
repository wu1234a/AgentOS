# AOS-044 Delivery Report：全自动化四项硬化

- 日期：2026-11-12
- 状态：Completed
- 范围：评审分片、PR 队列、update-branch、Planner 回退、启动验证

## 1. 大型 PR 评审分片

`ApiReviewAgent`：

- diff ≤ 60,000 字符：单次评审。
- 大 diff：按行切分，每片 ≤ 60,000 字符，最多 12 片。
- 每片独立调用模型并解析 JSON。
- 汇总 findings（最多 200 条）、summary（最多 4,000 字符）和 conclusion。
- 单片失败则整次评审失败，由现有 review worker 重试。

测试：`tests/AgentOS.Api.Tests/ReviewChunkingTests.cs`。

## 2. PR 有序合并队列

`GitHubAutoMergeWorker`：

- 只处理配置仓库、`aos/` 分支、目标 base 的开放 PR。
- 按 PR number 升序取第一条。
- 只有当前 PR review 完成且 mergeable 时才合并。
- 合并成功后立即结束本轮；下一条下一轮处理。
- 前一条 review Pending/Failed 或不可 merge 时，后续 PR 保持排队。

## 3. 分支自动更新

`GitHubPullRequestClient.UpdateBranchAsync`：

```text
PUT /repos/{repository}/pulls/{number}/update-branch
body: { expected_head_sha: <headSha> }
```

- 仅当配置打开 `AutoUpdateBranch` 且仓库有多个开放 PR 时调用。
- 成功后分支 head 变化，下一轮会重新排队评审。
- 真实文本冲突时 GitHub 返回错误，worker 记录并保持排队；不做危险的自动冲突改写。

## 4. Planner 批量 Backlog 回退

`PlannerOptions.BacklogFallbackProfileCodes`：

```json
["deepseek-reviewer"]
```

`BacklogPlannerService`：

1. 先尝试 `DefaultModelProfileCode`（当前 `kimi-planner`）。
2. 每个 profile 最多尝试 2 次。
3. 主 profile 网络错误、空内容或非法 JSON 时，尝试备用 profile。
4. 所有 profile 失败才返回 502。

## 5. 模型回退链

AOS-043 已配置：

```text
glm-5.3 → glm-5.3-flash → glm-5.3-flashx → deepseek-v4-pro
```

实测日志：

```text
model glm/glm-5.3 returned HTTP 429; trying fallback
```

## 6. 启动验证

Windows 任务：

```text
AgentOS Startup
Trigger: AtLogOn
Action:  wscript.exe //B run-hidden.vbs watchdog.ps1
```

Watchdog：

- 同步 3dERP main
- 启动 API
- 启动 Runner
- 启动 cloudflared
- 同步 GitHub webhook

模拟登录启动验证会停掉 API/Runner，再执行 Startup 任务，确认二者恢复。

## 7. 验证

```text
AgentOS build → 0 errors
AgentOS tests → 305 passed / 64 skipped / 0 failed
```

## 8. 已知限制

- 真实文本冲突仍需要人工/后续 AI 冲突解决器。
- GitHub update-branch 需要 Contents write 权限。
- Planner 备用 profile 目前是 DeepSeek，尚未支持多级候选列表 UI。
- 合并队列按 PR number 串行，不表达更细的业务依赖图。
- 未执行整机重启，只执行了 Startup 任务模拟。

## 9. 后续

1. AI 冲突解决任务。
2. 业务依赖图合并队列。
3. 前端展示模型/回退/评审分片状态。
4. 真实整机重启验证。
5. 高风险路径审批。
