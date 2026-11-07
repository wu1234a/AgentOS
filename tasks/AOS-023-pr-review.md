# AOS-023：PR 独立代码审查

状态：Completed
优先级：P1
依赖：AOS-022、AOS-014
来源：Kimi Planner 草案 `01a1b6e0-2aea-7d3b-b942-5d8f397e0393`（已批准）

## 0. 修订

Kimi 草案含租约、重试分类、inline comments、大 diff 分片。本阶段先交付：

1. `PullRequestReviewJobs` 队列表 + opened/synchronize 入队。
2. GitHub PR client：元数据、diff、创建 COMMENT Review。
3. Worker：校验 head、调用 CodexReviewAgent、发布单条 COMMENT Review、失败重试上限。
4. 不做 inline comments、租约/多实例、大 diff 分片、自动 approve/merge。

## 1. 范围

- 迁移 `AddPullRequestReviewJobs`：Repo/PR/HeadSha 唯一、Status、Attempts、NextAttempt、GitHubReviewId。
- Webhook 在 PR opened/synchronize 且 `ReviewEnabled` 时入队。
- Worker 每 10s 处理 Pending；head 不匹配 → Superseded；获取 diff → IReviewAgent → POST Review（event=COMMENT）→ Succeeded；失败指数退避，最多 `MaxReviewAttempts`。
- 环境变量 `GitHub__ReviewToken`，配置 `ReviewEnabled`、`MaxReviewAttempts`。

## 2. 不在范围

- inline review comments、大 diff 分片、auto approve/merge、GitHub App/OAuth。
- 多实例租约/行锁、Worker 状态 API、前端。
- 评论幂等检查（仅任务级 GitHubReviewId）。

## 3. 安全

- Token 仅环境变量；不传 Agent；Review 只读、event 固定 COMMENT。
- 不执行 PR 代码、不 checkout。
- diff/评论正文有上限（Agent 1MB；评论由 findings 生成）。
- 参考免责声明明确不构成审批/合并依据。

## 4. 验收

- [x] PR opened/synchronize 入队，重复 repo/PR/head 不重复。
- [x] Worker 校验 head，发布 COMMENT Review。
- [x] 失败重试与上限。
- [x] 后端全量测试、CI 通过。
