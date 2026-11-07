# AOS-023 交付报告：PR 独立代码审查

**状态：Completed**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-023-pr-review.md`

## 1. 能力

- GitHub PR opened/synchronize 入队 `PullRequestReviewJobs`。
- 后台 Worker 拉取 PR diff，调用 `IReviewAgent`（Codex 只读），发布单条 `COMMENT` Review。
- 校验 head：PR 已推进则旧任务 Superseded，不发布过时结论。
- 失败指数退避，最多 `MaxReviewAttempts`；成功保存 GitHubReviewId。

## 2. 配置

```json
"GitHub": {
  "ReviewEnabled": true,
  "ReviewToken": null,
  "MaxReviewAttempts": 5
}
```
环境变量：`GitHub__ReviewToken`（最小权限：Pull requests read/write + contents read）。

## 3. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **313 通过 / 5 跳过 / 0 失败** |
| CI | 见对应 run |

### 3.1 独立代码审查\n\nCodex CLI 审查发现 1 个 P1（重试状态卡 Running）与 2 个 P2（metadata 瞬态失败被 Superseded、发布前未复核 head），全部修复，详见 `docs/reports/AOS-023-CODE-REVIEW.md`。\n\n## 4. 已知限制

1. 无 PR review worker 自动化测试（Fake client/agent 未补）。
2. 无 inline comments、大 diff 分片。
3. 无多实例租约/行锁；单 Worker 假设。
4. 评论发布无幂等检查；任务级 GitHubReviewId 防重复成功。
5. 真实 GitHub API 未在 CI 调用。
6. 自动 approve/merge 未实现（符合设计）。

## 5. 变更文件

`PullRequestReviewJob.cs`、配置、迁移、`GitHubPullRequestClient`、`GitHubReviewQueue`、
`GitHubReviewWorker`、Webhook 入队、GitHubOptions、Program、文档。
