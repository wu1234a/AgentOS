# AOS-051 Delivery Report：依赖图校验、Epic 启动与版本对比

- 日期：2026-11-13
- 状态：Completed
- 范围：依赖环校验、进度依赖明细、Epic 启动、关键路径、版本对比

## 1. 变更文件

后端：

- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanService.cs`
- `backend/AgentOS.Api/Endpoints/ProjectPlanEndpoints.cs`

前端：

- `frontend/agentos-web/src/api/projectPlans.js`
- `frontend/agentos-web/src/views/ProjectPlansView.vue`

测试：

- `tests/AgentOS.Api.Tests/ProjectPlanServiceTests.cs`

文档：

- `tasks/AOS-051-dependency-graph-and-diff.md`（新增）
- `docs/reports/AOS-051-DELIVERY-REPORT.md`（本文件）

## 2. 依赖图校验

批准计划前执行：

- taskKey 必须唯一
- dependsOn 必须存在
- 依赖图不能有环

失败返回：

```text
plan-dependency-invalid
```

测试：

```text
missing dependency → 400
dependency cycle → 400
```

## 3. 进度依赖明细

`GET /progress` 的 items 新增：

```json
{
  "taskKey": "T2",
  "dependsOn": ["T1"]
}
```

前端 Backlog 明细显示 Key 和依赖。

## 4. 按 Epic 启动

`POST /release` 支持：

```json
{ "epicKeys": ["M2"] }
```

- 只启动该 Epic 下 Hold 项。
- 没有可启动项返回 `plan-epics-not-held`。
- 仍受二次启动审批约束。

前端 Epic 汇总表新增“启动 Epic”。

## 5. 关键路径

前端根据 taskKey/dependsOn 计算最长依赖链，显示：

```text
关键路径：T1 → T3 → T7
```

这是依赖深度，不是带工期估算的关键路径。

## 6. 版本对比

版本历史新增“对比”：

- 对比目标版本与当前版本的任务
- 列出新增、删除、变更任务
- 按 taskKey 匹配

## 7. 验证

```text
dotnet build backend/AgentOS.slnx → 0 errors
AgentOS tests → 309 passed / 64 skipped / 0 failed
frontend build → 1731 modules transformed
Playwright → 对比按钮可见，0 page errors
```

## 8. 已知限制

- 环校验只在批准时执行；生成阶段仍可能产出环。
- 关键路径没有工期权重。
- 版本对比按 taskKey，旧任务没有 key 时按标题。
- 没有依赖失败自动重排。
- 没有真实登录身份；X-AgentOS-Actor 可伪造。

## 9. 后续

1. 生成阶段让 Chair 自检依赖环。
2. 任务工期/成本权重关键路径。
3. 依赖失败自动重排。
4. 登录身份与权限。
