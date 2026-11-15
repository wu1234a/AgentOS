# AOS-053 Delivery Report：项目目标与项目空间

- 日期：2026-11-15
- 状态：Completed
- 范围：Phase 1 项目目标、简报、状态、从目标创建

## 1. 变更文件

后端：

- `backend/AgentOS.Domain/Projects/Project.cs`
- `backend/AgentOS.Infrastructure/Persistence/Configurations/ProjectConfiguration.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261115105032_AddProjectCharter.cs`（新增）
- `backend/AgentOS.Application/Projects/ProjectService.cs`
- `backend/AgentOS.Api/Endpoints/ProjectsEndpoints.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanService.cs`

前端：

- `frontend/agentos-web/src/api/projects.js`
- `frontend/agentos-web/src/views/ProjectsView.vue`

文档：

- `docs/PROJECT-OPERATING-MODEL.md`
- `tasks/AOS-053-project-charter.md`（新增）
- `docs/reports/AOS-053-DELIVERY-REPORT.md`（本文件）

## 2. Project 新字段

```text
Goal                 一句话目标
Brief                项目简报
PersonalNotes        个人想法
AcceptanceCriteria   验收标准
Repository           owner/repo
Status               Draft/Active/OnHold/Completed/Archived
```

旧项目迁移后：

```text
Status = Active
其他字段 = NULL
```

## 3. API

新增：

```text
POST /api/v1/projects/suggest
```

请求：

```json
{
  "goal": "为 3dERP 增加只读报表模块，首期库存，预算有限。",
  "notes": "尽量复用库存模块。",
  "repository": "wu1234a/3dERP"
}
```

返回：

```json
{
  "name": "...",
  "slug": "project-xxxxxxxx",
  "goal": "...",
  "brief": "...",
  "acceptanceCriteria": "...",
  "repository": "wu1234a/3dERP",
  "status": "Draft"
}
```

创建/更新：

```text
POST  /api/v1/projects
PATCH /api/v1/projects/{id}
```

支持 goal/brief/personalNotes/acceptanceCriteria/repository/status。

## 4. Project Plan 复用

创建 ProjectPlan 时：

```text
title 未传 → 项目名 + " 立项"
goal 未传 → Project.Goal
constraints 未传 → Project.Brief
delivery.repository 未传 → Project.Repository
```

## 5. 前端

Projects 页面：

- 表格显示目标、项目状态、仓库
- 详情抽屉展示目标/简报/个人想法/验收标准
- 创建/编辑对话框支持全部字段
- “从目标创建”生成草稿并预填创建表单
- 项目状态可编辑

## 6. 验证

```text
POST /projects/suggest → Draft 项目草稿
POST /projects → 创建 Draft 项目
PATCH /projects/{id} → status/brief/acceptance 更新成功
Playwright projects can be created and disabled → passed
Playwright 从目标创建 → 创建表单预填成功
dotnet build backend/AgentOS.slnx → 0 errors
AgentOS tests → 309 passed / 64 skipped / 0 failed
```

## 7. 说明

- 当前“从目标创建”是确定性草稿生成，不是 LLM 自动命名。
- slug 使用随机 `project-xxxxxxxx`，人工可在创建前修改。
- 项目 Goal/Brief 目前不会自动生成 Markdown 文档，Phase 2 实现。
- 项目状态目前是人工编辑，没有自动状态机。

## 8. 后续

- Phase 2：ProjectDocument/Markdown/版本。
- Phase 3：角色队列与 Skill 自动选择。
- Phase 4：质量证据与 Skill 提案。
- Phase 5：里程碑与项目验收。
