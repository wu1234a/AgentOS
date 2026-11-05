# AOS-004：Rules Engine

状态：Not Started  
优先级：P0  
依赖：AOS-003

## 1. 目标

实现可配置的事件规则引擎。Event 被接收并去重后，Rules Engine 根据
source、eventType、project 和其他受控条件匹配规则，并生成待执行 Task。

## 2. 核心原则

- 规则决定路由，不执行 Agent。
- 数据库 AutomationRules 是运行时权威数据源。
- JSON 文件是样例/初始化来源，不是唯一运行时存储。
- 每次 Task 必须记录命中的 RuleId 和 RuleVersion。
- 规则匹配必须确定、可测试、可解释。
- 不能执行任意 C#、JavaScript 或表达式代码作为规则。

## 3. 数据库范围

创建： - Agents - ModelProfiles - Skills - AutomationRules

若 AOS-001 已创建其中基础表，按现有结构增量完善，不得重复建表。

AutomationRules 至少支持： - Name - ProjectId（可空） - SourcePattern -
EventTypePattern - MatchJson - AgentId - SkillId（可空） -
DispatchJson - Priority - MaxParallel - MaxAttempts - TimeoutSeconds -
NotificationPolicy - IsEnabled - Version

## 4. 规则样例

``` json
{
  "code": "3derp-dev-plan",
  "name": "3DERP development planning",
  "when": {
    "source": "3derp",
    "eventTypes": ["development.task.created"],
    "project": "3derp"
  },
  "dispatch": {
    "agentCode": "planner",
    "modelProfileCode": "strong-reasoning",
    "skillCode": "3derp-plan-development",
    "workspaceMode": "read-only"
  },
  "limits": {
    "maxParallel": 1,
    "maxAttempts": 2,
    "timeoutSeconds": 1800
  },
  "notificationPolicy": "failure-or-approval"
}
```

## 5. 处理流程

1.  Event 成功持久化。
2.  读取所有启用且适用的规则。
3.  按确定顺序匹配规则（优先级高者优先；同优先级按稳定顺序）。
4.  校验 Agent、Skill、ModelProfile 已启用。
5.  对每个命中规则生成 Task。
6.  通过唯一逻辑键保证同一 Event/RuleVersion 不重复生成相同 Task。
7.  记录命中/未命中结果。
8.  使用事务或 Outbox 确保 Event 状态和 Task 创建一致。

## 6. 管理 API

- `GET /api/v1/rules`
- `GET /api/v1/rules/{id}`
- `POST /api/v1/rules`
- `PUT /api/v1/rules/{id}`
- `POST /api/v1/rules/{id}/enable`
- `POST /api/v1/rules/{id}/disable`
- `POST /api/v1/rules/validate`（只验证，不保存）

管理 API 必须有身份验证和权限检查。

## 7. 前端范围

创建规则列表和规则详情基础页面： - 名称、项目、事件来源、事件类型。 -
目标 Agent、Skill、Model Profile。 - 并发、超时、最大尝试次数。 -
启用状态和版本。 - 校验错误提示。

不要在第一版做复杂可视化拖拽编辑器。

## 8. 验收标准

- [ ] 可以创建、读取、修改、启用、禁用规则。
- [ ] 非法规则 JSON 被拒绝。
- [ ] 未启用 Agent/Skill/ModelProfile 的规则不能启用。
- [ ] 一个事件可命中零条、一条或多条规则。
- [ ] 命中规则可以生成 Queued/Pending Task。
- [ ] 重复事件不会生成重复逻辑任务。
- [ ] Task 保留 RuleId 和 RuleVersion。
- [ ] 规则匹配顺序确定且有测试。
- [ ] 未匹配事件被保留，不丢失。

## 9. 交付

创建
`docs/reports/AOS-004-DELIVERY-REPORT.md`，附规则样例、匹配测试和数据库变更。
