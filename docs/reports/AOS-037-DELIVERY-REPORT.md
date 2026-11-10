# AOS-037 Delivery Report：PR 自动评审与自动合并

- 日期：2026-11-11
- 状态：Completed
- 范围：PR 自动 Review → 自动 Merge

## 1. 变更文件

后端：

- `backend/AgentOS.Api/GitHub/GitHubAutoMergeWorker.cs`（新增）
- `backend/AgentOS.Api/GitHub/AutoMergeOptions.cs`（新增）
- `backend/AgentOS.Api/GitHub/GitHubPullRequestClient.cs`
- `backend/AgentOS.Api/GitHub/GitHubReviewWorker.cs`
- `backend/AgentOS.Api/GitHub/GitHubOptions.cs`
- `backend/AgentOS.Api/Reviews/ApiReviewAgent.cs`
- `backend/AgentOS.Api/Reviews/CodexReviewAgent.cs`
- `backend/AgentOS.Api/Program.cs`
- `backend/AgentOS.Api/appsettings.json`

测试：

- `tests/AgentOS.Api.Tests/SqlMergeReadinessTests.cs`

文档：

- `tasks/AOS-037-auto-review-merge.md`（新增）
- `docs/reports/AOS-037-DELIVERY-REPORT.md`（本文件）

## 2. 行为

`GitHubAutoMergeWorker` 每 30 秒轮询配置仓库的开放 PR：

1. 只处理：
   - base 分支匹配；
   - head 分支以 `aos/` 开头；
   - 非 draft；
   - head SHA 对应评审成功。
2. 没有评审任务时，自动加入评审队列。
3. 评审任务：
   - `Critical` / `Major` → `Blocked`，不合并；
   - 仅 `Info` / `Minor` → `Succeeded`，允许合并。
4. PR mergeable 为 `null` 时，通过单 PR API 刷新，避免 GitHub 列表接口返回 null。
5. 评审成功且 PR mergeable 时，使用 `squash` 自动合并。

评审来源：

```text
Provider:  glm
Model:     glm-5.3
BaseUrl:   https://open.bigmodel.cn/api/paas/v4
Secret:    ZHIPU_API_KEY
```

## 3. 配置

生产 `GitHub`：

```json
{
  "ReviewEnabled": true,
  "MaxReviewAttempts": 3,
  "AutoMerges": {
    "wu1234a/3dERP": {
      "Enabled": true,
      "BaseBranch": "main",
      "BranchPrefix": "aos/",
      "MergeMethod": "squash",
      "PollSeconds": 30,
      "RequireReview": true
    }
  }
}
```

Token 只从环境变量注入，不写入配置或日志。

## 4. 端到端验证

测试 PR：

```text
PR #9  docs: add auto-merge smoke note 3
head:  a5697c524503bf2791df8344fb703db6c660427c
```

评审：

```text
结论：IssuesFound
发现：1 个 Info
状态：Succeeded
```

自动合并：

```text
merged=true
merged_at=2026-11-10T18:46:31Z
merge_commit=de83901afa7a773b3299c3ad23aa6f3014f93930
```

3dERP main 已更新到：

```text
de83901 docs: add auto-merge smoke note 3 (#9)
```

## 5. 验证

```text
dotnet build backend/AgentOS.slnx
→ 0 errors

dotnet vstest AgentOS.Api.Tests.dll
→ 300 passed, 64 skipped, 0 failed
```

## 6. 安全

- 只自动合并 `aos/` 前缀分支。
- 只允许配置仓库和配置 base 分支。
- 必须有同一 head SHA 的评审成功记录。
- `Critical` / `Major` 阻塞合并。
- PR 必须 mergeable 且非 draft。
- Token 只通过 `GitHub__ReviewToken` 环境变量提供。
- 不依赖公网 webhook；轮询由本机控制平面发起。

## 7. 已知限制

- 自动合并目前只覆盖配置的单个 3dERP 仓库。
- 没有人工审批卡片；高风险改动默认也走自动合并。
- 没有接入 GitHub Actions quality gate；质量依据是构建/测试校验 + AgentOS 独立评审。
- PR 冲突时不会自动 rebase，只会跳过并持续记录。
- 评审模型仍是 GLM 5.3，存在误判和输出格式偏差风险。
- 没有自动删除远端分支；当前由人工或后续清理任务处理。

## 8. 后续

1. 自动清理已合并分支。
2. PR 冲突时自动 rebase/re-run。
3. 高风险路径（支付、权限、迁移）强制人工审批。
4. 接入 GitHub Actions quality gate。
5. 自动启动 API、Runner 和隧道。
6. 前端展示自动评审/合并状态。
