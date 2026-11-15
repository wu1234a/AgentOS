# AOS-054 Delivery Report：正式项目文档与计划文档化

- 日期：2026-11-15
- 状态：Completed
- 范围：Phase 2 Markdown 项目文档、版本、API、前端查看

## 1. 变更文件

后端：

- `backend/AgentOS.Domain/Projects/ProjectDocument.cs`（新增）
- `backend/AgentOS.Infrastructure/Persistence/Configurations/ProjectDocumentConfiguration.cs`（新增）
- `backend/AgentOS.Infrastructure/Persistence/AgentOSDbContext.cs`
- `backend/AgentOS.Infrastructure/Migrations/20261115105733_AddProjectDocuments.cs`（新增）
- `backend/AgentOS.Api/ProjectDocuments/ProjectDocumentService.cs`（新增）
- `backend/AgentOS.Api/Endpoints/ProjectDocumentEndpoints.cs`（新增）
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanService.cs`
- `backend/AgentOS.Api/Program.cs`

前端：

- `frontend/agentos-web/src/api/projectDocuments.js`（新增）
- `frontend/agentos-web/src/views/ProjectsView.vue`

文档：

- `docs/PROJECT-OPERATING-MODEL.md`
- `tasks/AOS-054-project-documents.md`（新增）
- `docs/reports/AOS-054-DELIVERY-REPORT.md`（本文件）

## 2. 文档模型

`ProjectDocuments`：

```text
Id
ProjectId
PlanId
PlanRevision
DocumentType
Title
Version
ContentMarkdown
Status = Approved/Superseded
CreatedAtUtc
```

唯一索引：

```text
(ProjectId, DocumentType, Version)
```

## 3. 生成时机

计划批准时：

```text
Approve
→ 校验依赖
→ 校验 delivery
→ 生成 5 类文档
→ 创建 Backlog
→ Plan Approved
```

五类：

```text
PROJECT       项目章程
BRAINSTORM    头脑风暴与评审
ROADMAP       路线图
ARCHITECTURE  技术方案
ACCEPTANCE    验收标准
```

内容来源：

- 项目 Goal/Brief/PersonalNotes/AcceptanceCriteria/Repository
- Plan Proposals/Critiques
- Plan FinalPlanJson 的 recommendedOption/roadmap/risks/acceptanceMetrics/tasks

## 4. 版本

每次批准/修订后批准：

```text
同类型 max(version) + 1
旧 Approved → Superseded
新文档 → Approved
```

同一 PlanRevision 已生成则跳过，避免重复版本。

## 5. API

```text
GET /api/v1/projects/{projectId}/documents
GET /api/v1/projects/{projectId}/documents/{documentId}
```

## 6. 前端

项目详情抽屉：

- 项目文档表
- 类型/标题/版本/状态
- 点击查看 Markdown
- 无文档时显示空状态

## 7. 验证

使用合成 PendingReview 计划验证：

```text
POST approve
→ 生成 5 份文档
→ ROADMAP 内容正确
→ 文档版本 v1
→ Backlog 创建
```

测试数据已清理。

```text
dotnet build → 0 errors
AgentOS tests → 309 passed / 64 skipped / 0 failed
frontend build → 1733 modules transformed
Playwright → 项目文档区域可见，0 page errors
```

## 8. 已知限制

- 文档存储在数据库，不自动写入项目仓库。
- Markdown 只做只读展示，没有编辑器。
- BRAINSTORM 内容来自已存提案/评审，不是重新生成。
- 文档版本对比尚未实现，仅按版本列表。
- 项目归档/完成流程尚未与文档联动。

## 9. 后续

- Phase 3：任务文档、角色队列、Skill 自动选择。
- Phase 4：质量证据、Skill 提案。
- Phase 5：里程碑/项目验收。
