# AOS-039 Delivery Report：任务 Backlog 与自动派发

- 日期：2026-11-11
- 状态：Completed
- 范围：Backlog 持久化、API、Worker、自动派发、终态跟踪

## 1. 变更文件

后端：

- `backend/AgentOS.Domain/Backlog/BacklogItem.cs`（新增）
- `backend/AgentOS.Infrastructure/Persistence/Configurations/BacklogItemConfiguration.cs`（新增）
- `backend/AgentOS.Infrastructure/Persistence/AgentOSDbContext.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261111101449_AddBacklogItems.cs`（新增）
- `backend/AgentOS.Api/Backlog/BacklogOptions.cs`（新增）
- `backend/AgentOS.Api/Backlog/BacklogService.cs`（新增）
- `backend/AgentOS.Api/Backlog/BacklogWorker.cs`（新增）
- `backend/AgentOS.Api/Endpoints/BacklogEndpoints.cs`（新增）
- `backend/AgentOS.Api/Program.cs`
- `backend/AgentOS.Api/appsettings.json`
- `backend/AgentOS.Api/GitHub/GitHubPullRequestClient.cs`
- `backend/AgentOS.Api/GitHub/GitHubReviewWorker.cs`
- `backend/AgentOS.Api/Reviews/CodexReviewAgent.cs`
- `backend/AgentOS.Runner/Delivery/GitAutoDelivery.cs`

测试：

- `tests/AgentOS.Api.Tests/GitAutoDeliveryTests.cs`
- `tests/AgentOS.Api.Tests/SqlMergeReadinessTests.cs`

文档：

- `tasks/AOS-039-backlog-auto-dispatch.md`（新增）
- `docs/reports/AOS-039-DELIVERY-REPORT.md`（本文件）

## 2. API

```text
GET  /api/v1/backlog
POST /api/v1/backlog
POST /api/v1/backlog/{id}/cancel
```

创建示例：

```json
{
  "projectId": "01a1bca2-5bee-7862-a87f-54239e8aba52",
  "title": "Remove auto-merge smoke artifact retry",
  "instruction": "Delete docs/auto-merge-smoke-3.md ...",
  "validationCommands": ["dotnet build 3dERP.slnx"],
  "delivery": {
    "repository": "wu1234a/3dERP",
    "baseBranch": "main",
    "remote": "origin",
    "tokenEnvironmentVariable": "AGENTOS_GITHUB_TOKEN",
    "requirePullRequest": true
  }
}
```

自动生成分支：

```text
aos/backlog-{itemId:N}
```

## 3. 派发规则

```text
source:            backlog
eventType:         task.requested
agent:             3derp-coder
model:             3derp-coder-model
ruleId:            01a1ca1b-9d2a-7ce6-b279-ae3136147b23
```

Worker 每 15 秒检查：

1. 更新已派发项的终态。
2. 对每个项目检查是否存在非终态任务。
3. 无活动任务时派发该项目的下一条 Pending 项。
4. 事件创建成功且生成任务后标记 `Dispatched`。
5. 任务 Succeeded → `Completed`；Failed/Cancelled/Rejected → `TaskFailed`。

## 4. 端到端验证

创建 backlog：

```text
id:      01a1ca21-ad2d-7f67-b838-430688f7e6d4
status:  Pending → Dispatched
eventId: 01a1ca21-d051-7c75-b567-06537913aa69
taskId:  01a1ca21-d0c6-76bb-b6be-c38969dca615
```

执行：

```text
task: Succeeded
PR:   #11 Remove auto-merge smoke artifact retry
```

自动评审和合并：

```text
merged=true
mergedAt=2026-11-11T10:30:40Z
mergeCommit=d32b4d5d1bc95eba08482b5b85741c84dd5514a1
```

结果：

```text
docs/auto-merge-smoke-3.md removed
3dERP main = d32b4d5
```

## 5. 同时修复

- Agent 自行 commit 后 delivery 误判“无改动”：现在检查 `origin/{base}..HEAD` 的已提交工作量。
- 评审输出夹带解释文字：现在从首个 `{` 到最后一个 `}` 提取 JSON。
- GitHub review 发布 403：回退为普通 PR comment；评审状态仍以本地结果为准。
- 生产 `GitHub__ReviewToken` 使用了权限不足的 fine-grained token；改为与 `AGENTOS_GITHUB_TOKEN` 一致的 classic token 后 merge 成功。

## 6. 验证

```text
AgentOS build → 0 errors
AgentOS tests → 300 passed / 64 skipped / 0 failed
Backlog migration applied to AgentOS_Prod
```

## 7. 安全

- Backlog 只允许已启用项目。
- 事件走标准校验和 dedupe。
- 分支由 item id 生成，不覆盖现有分支。
- 不执行任意 shell；validation/delivery 仍是任务 payload 的受控字段。
- Worker 只在项目没有活动任务时派发，避免并发覆盖。
- 自动合并仍只处理 `aos/` 分支、配置仓库、已评审且 mergeable 的 PR。

## 8. 已知限制

- Backlog 内容仍需人工预置；尚未由 Planner 自动批量生成。
- 单进程 Worker，没有多 API 实例的分布式抢占锁。
- `TaskFailed` 不会自动生成修复项或重试。
- 没有前端 backlog 页面。
- 任务执行失败时，已创建的 PR 可能需要人工关闭。
- 评审发布 403 时只发 fallback comment；若 comment 也失败，仅在日志中记录。

## 9. 后续

1. Planner 批量生成 backlog。
2. 失败任务自动派生修复项。
3. Backlog 前端管理页。
4. 多实例 Worker 抢占锁。
5. 高风险路径人工审批。
