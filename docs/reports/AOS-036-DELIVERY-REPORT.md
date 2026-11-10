# AOS-036 Delivery Report：Planner 批准后自动派发任务

- 日期：2026-11-11
- 状态：Completed
- 范围：Planner 审批 → Event Hub → Rules → Task

## 1. 变更文件

后端：

- `backend/AgentOS.Application/Planning/PlannerContracts.cs`
- `backend/AgentOS.Application/Planning/PlannerDispatchService.cs`（新增）
- `backend/AgentOS.Application/Projects/IProjectLookup.cs`
- `backend/AgentOS.Api/Endpoints/PlannerEndpoints.cs`
- `backend/AgentOS.Api/Program.cs`
- `backend/AgentOS.Api/appsettings.json`
- `backend/AgentOS.Infrastructure/Projects/ProjectLookup.cs`

测试：

- `tests/AgentOS.Api.Tests/PlannerDispatchServiceTests.cs`（新增）
- `tests/AgentOS.Api.Tests/Fakes/FakeProjectLookup.cs`

文档：

- `tasks/AOS-036-planner-auto-dispatch.md`（新增）
- `docs/reports/AOS-036-DELIVERY-REPORT.md`（本文件）

## 2. 行为

批准 Planner 草案后：

1. 校验 `Planner:AutoDispatch:Enabled`。
2. 按 `ProjectSlug` 限定项目，避免把 3dERP 的交付配置用于其他项目。
3. 生成事件：

```text
source:             planner
eventType:          draft.approved
dedupeKey:          planner:{draftId}
externalEventId:    planner-draft:{draftId}
project:            {project.slug}
```

4. 事件 payload 包含：

- `instruction`：原始目标 + 批准的 Markdown 计划；
- `validationCommands`：配置中的校验命令；
- `delivery`：仓库、基础分支、token 环境变量、自动生成分支。

5. 事件进入现有 Rules → Task → Runner 链路。
6. 审批响应增加：

```text
X-AgentOS-Dispatch: Dispatched | Skipped | NoTask | Failed | Disabled
X-AgentOS-Dispatch-Event: <event id>
X-AgentOS-Dispatch-Tasks: <count>
```

## 3. 配置

仓库默认关闭：

```json
"Planner": {
  "AutoDispatch": {
    "Enabled": false,
    "ProjectSlug": "3derp",
    "Source": "planner",
    "EventType": "draft.approved",
    "ValidationCommands": [
      "dotnet build 3dERP.slnx",
      "dotnet test 3dERP.slnx --no-build",
      "npm --prefix web ci",
      "npm --prefix web run build"
    ],
    "Delivery": {
      "Repository": "wu1234a/3dERP",
      "BaseBranch": "main",
      "Remote": "origin",
      "TokenEnvironmentVariable": "AGENTOS_GITHUB_TOKEN",
      "RequirePullRequest": true
    }
  }
}
```

部署环境将 `Enabled` 设为 `true`，并创建规则：

```text
name:              3dERP planner auto dispatch
project:           3derp
sourcePattern:     planner
eventTypePattern:  draft.approved
agentCode:         3derp-coder
modelProfileCode:  3derp-coder-model
maxAttempts:       3
timeoutSeconds:    3600
```

规则 ID：`01a1c6aa-debe-757e-a216-17094c1308fe`

## 4. 验证

构建：

```text
dotnet build backend/AgentOS.slnx
→ 0 errors, 1 existing warning
```

全量测试：

```text
dotnet vstest AgentOS.Api.Tests.dll
→ 300 passed, 64 skipped, 0 failed
```

端到端验证：

```text
Planner draft: 01a1c6af-0bc6-7b82-8ed4-ecbd6b4a66e1
Approval:      200
Dispatch:      Dispatched
Event:         01a1c6af-1ce6-7334-b434-d139888784db
Task:          01a1c6af-1d99-790a-87be-d975526b3662
Tasks created: 1
```

验证任务在 Runner 领取前被主动取消，避免冒烟测试产生代码改动。

## 5. 安全

- 仓库配置默认关闭自动派发。
- 仅匹配指定 project slug。
- 不读取或写入 token；只传环境变量名。
- 分支名由草案 ID 生成，避免并发覆盖。
- 事件 dedupe key 保证同一草案只派发一次。
- 审批失败或派发异常不会回滚已批准的草案，但会记录错误并返回失败标记。

## 6. 已知限制

- 审批 UI 尚未展示 `X-AgentOS-Dispatch` 状态。
- 如果规则不存在，事件会保留但不会创建任务，当前没有补派接口。
- 自动派发依赖部署配置中的仓库与校验命令，尚无多项目模板 UI。
- 自动 Review、自动合并、Quality Gate 和 Runner 开机自启动仍未实现。

## 7. 后续

1. 前端展示派发结果和任务链接。
2. 增加补派接口，处理“草案已批准但当时无规则”的情况。
3. 自动 Review / Quality Gate / 自动合并。
4. Runner 与隧道开机自启动。
5. 任务模板按项目/前后端类型拆分。
