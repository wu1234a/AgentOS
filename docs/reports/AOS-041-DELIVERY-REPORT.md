# AOS-041 Delivery Report：Planner 批量生成 Backlog

- 日期：2026-11-11
- 状态：Completed
- 范围：目标 → Planner 拆解 → Backlog → 自动执行/合并

## 1. 变更文件

- `backend/AgentOS.Api/Backlog/BacklogPlannerService.cs`（新增）
- `backend/AgentOS.Api/Endpoints/BacklogEndpoints.cs`
- `backend/AgentOS.Api/Program.cs`
- `tests/AgentOS.Api.Tests/BacklogPlannerServiceTests.cs`（新增）

文档：

- `tasks/AOS-041-planner-backlog-batch.md`（新增）
- `docs/reports/AOS-041-DELIVERY-REPORT.md`（本文件）

## 2. API

```text
POST /api/v1/backlog/generate
```

请求：

```json
{
  "projectId": "01a1bca2-5bee-7862-a87f-54239e8aba52",
  "goal": "Add a short documentation file ...",
  "context": "3dERP main is d32b4d5 ...",
  "maxItems": 1,
  "priority": "low",
  "delivery": {
    "repository": "wu1234a/3dERP",
    "baseBranch": "main",
    "remote": "origin",
    "tokenEnvironmentVariable": "AGENTOS_GITHUB_TOKEN",
    "requirePullRequest": true
  }
}
```

响应：

```json
{
  "items": [
    {
      "id": "01a1ca84-667a-7a1b-a2f4-36a7602f8b02",
      "title": "Add backlog planner smoke documentation",
      "status": "Pending"
    }
  ]
}
```

## 3. Planner 输出协议

模型只返回 JSON 数组：

```json
[
  {
    "kind": "backend|frontend|docs|full",
    "title": "short title",
    "instruction": "self-contained implementation instruction"
  }
]
```

解析规则：

- 支持 Markdown code fence 和前后解释文字；
- 只取首个 `[` 到最后一个 `]`；
- kind 必须在允许集合；
- title 3-200，instruction 10-20000；
- 最多取 `maxItems`（默认 5，最大 10）。

## 4. 校验命令映射

```text
backend  → dotnet build 3dERP.slnx + dotnet test 3dERP.slnx --no-build
frontend → npm --prefix web ci + npm --prefix web run build
docs     → dotnet build 3dERP.slnx
full     → 以上全部
```

生成项以 `Pending` 进入 Backlog，由 Worker 自动派发。

## 5. 端到端验证

输入目标：

```text
Add a short documentation file docs/backlog-planner-smoke.md ...
```

生成：

```text
item:  01a1ca84-667a-7a1b-a2f4-36a7602f8b02
title: Add backlog planner smoke documentation
```

自动执行：

```text
task: Succeeded
PR:   #12 Add backlog planner smoke documentation
```

自动评审并合并：

```text
mergedAt=2026-11-11T12:13:17Z
mergeCommit=ed87d52aeba4e2c0232ce66aec1767d0ac461a4a
```

Backlog 终态：

```text
Completed
```

3dERP main：

```text
ed87d52 Add backlog planner smoke documentation (#12)
```

## 6. 验证

```text
dotnet build backend/AgentOS.slnx → 0 errors
AgentOS tests → 303 passed / 64 skipped / 0 failed
```

## 7. 安全

- `maxItems` 上限 10。
- 目标/上下文作为不可信数据处理。
- 生成 instruction 经过长度、kind、字段校验。
- 不把密钥写进 prompt 或 Backlog。
- 生成项仍走标准事件、规则、隔离 worktree、校验、评审和自动合并。
- 高风险路径审批仍未实现。

## 8. 已知限制

- 使用默认 Planner 模型 `kimi-planner`，尚未支持请求指定模型。
- 校验命令映射目前是 3dERP 专用。
- 生成后立即 Pending 并自动执行，没有“先审阅再启动”模式。
- Planner 可能生成重复/过大的任务，只做基础过滤。
- 没有批量取消/调整顺序的 UI。
- 仍没有高风险路径的人工审批。

## 9. 后续

1. 生成计划先进入 Draft/Hold，人工确认后启动。
2. 按项目配置校验模板。
3. Backlog/Generate 前端页面。
4. 高风险路径审批闸门。
5. 用该接口生成库存模块 Backlog。
