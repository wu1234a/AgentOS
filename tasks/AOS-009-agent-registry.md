# AOS-009：Agent 注册中心与生命周期

状态：Completed
优先级：P0
依赖：AOS-004（规则引用 Agent）、AOS-008（管理后台）

## 1. 目标

把 `Agents` 从"只能靠 SQL 预置"升级为可管理的注册中心：支持 Agent 的创建、查询、
编辑、启用/禁用、归档，并在规则引用和任务生成时保持一致性。Agent 是不可变标识
（Code）与可变配置（角色、权限、默认模型）的组合。

## 2. 实施范围

### 2.1 领域与数据库

- `Agent` 增加 `ArchivedAtUtc`（归档时间），归档为终态；保留 `IsEnabled` 兼容规则引擎。
- 迁移 `AddAgentLifecycle`，历史数据 `ArchivedAtUtc = NULL`。
- 沿用现有唯一约束：`(ProjectId, Code)` 项目内唯一，`ProjectId IS NULL` 时全局唯一。

### 2.2 生命周期

```
创建(Active) --disable--> Disabled --enable--> Active
    |                        |                  |
    +--------archive---------+------------------+--> Archived (终态)
```

- 创建后 `IsEnabled = true`。
- 禁用只阻止新任务派发，不影响已领取/运行中的任务。
- 归档必须没有未终态任务（Queued / Running / RetryWait），否则 409。
- 归档后不可更新、启用或再次归档。

### 2.3 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/v1/agents?projectId&role&includeArchived&limit&offset` | 分页列表，默认排除归档 |
| GET | `/api/v1/agents/{id}` | 详情 |
| POST | `/api/v1/agents` | 创建，201 |
| PUT | `/api/v1/agents/{id}` | 更新可变字段（Code 不可变） |
| POST | `/api/v1/agents/{id}/enable` | 启用 |
| POST | `/api/v1/agents/{id}/disable` | 禁用 |
| POST | `/api/v1/agents/{id}/archive` | 归档 |

### 2.4 校验

- Code：小写 slug（3–80），创建后不可改；项目内/全局唯一。
- DisplayName：必填，≤120。
- Role：`planner` / `coder` / `reviewer` / `qa`（规范化为小写）。
- PermissionProfile：`read-only` / `workspace-write`。
- Description：≤1000。
- SystemPromptPath：可空；必须是相对路径、无 `..`、无盘符/根路径、≤500。
- ProjectId：存在且启用。
- DefaultModelProfileId：存在且启用。

### 2.5 前端

- 新增 Agents 页面：列表、按项目/角色/是否归档过滤、创建/编辑对话框、
  启用/禁用/归档操作，显示生命周期状态。

## 3. 不在本任务范围

- Agent 版本化、A/B 灰度、能力标签市场。
- Model Router / Provider Profiles（AOS-010）。
- Skill 管理与版本（AOS-011）。
- 用户级权限（沿用开发 API Key）。

## 4. 安全与权限要求

- 所有写操作需要管理 API 凭证。
- 不允许客户端提交任意提示词文件路径；SystemPromptPath 只做相对路径校验，
  实际读取留给后续任务并必须限定在配置根目录。
- 归档不删除历史任务/日志/产物。

## 5. 验收标准

- [ ] 可创建、查询、编辑 Agent。
- [ ] Code 项目内唯一，重复返回 409。
- [ ] 禁用后规则校验拒绝新任务引用该 Agent。
- [ ] 有未终态任务时归档返回 409。
- [ ] 归档后不可更新/启用，默认列表不返回。
- [ ] 非法 Role / PermissionProfile / 路径 / 不存在项目或 ModelProfile 返回 400。
- [ ] 前端 lint/build 通过，后端 build/test（含 SQL）通过。
- [ ] 文档与交付报告更新。

## 6. 交付

创建 `docs/reports/AOS-009-DELIVERY-REPORT.md`。
