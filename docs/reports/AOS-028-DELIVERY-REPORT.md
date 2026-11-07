# AOS-028 交付报告：多项目注册与配额

**状态：Completed（第一阶段）**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-028-multi-project.md`

## 1. 能力

- `GET/POST/PATCH /api/v1/projects`、`/enable`、`/disable`。
- `MaxConcurrentTasks`（NULL 不限，1–10000）。
- TaskClaimer 候选与原子领取都检查项目配额；项目停用不可领取。
- 迁移 `AddProjectQuota`。

## 2. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **318 通过 / 5 跳过 / 0 失败** |
| 项目/配额用例 | ✅ CRUD、重复 slug、配额阻止领取 |
| CI | 见对应 run |

### 2.1 独立代码审查\n\nCodex CLI 审查发现 1 个 P1（配额 SQL 跨项目计数）与 3 个 P2（PATCH 清空配额、停用竞态、slug 并发 500），全部修复，详见 `docs/reports/AOS-028-CODE-REVIEW.md`。\n\n## 3. 已知限制

1. 未实现 Task/Agent/Rule/CodingRun 的 `projectId` 查询过滤与资源归属校验。
2. 无前端项目管理页。
3. 配额只限制活动任务领取，不限制创建排队任务/CodingRun。
4. 无审计日志、多租户/成员/计费。
5. Claimer 配额在并发领取下依赖 SQL 条件，但未做高并发压力测试。

## 4. 变更文件

`Project.cs`、迁移、`ProjectService`/`ProjectRepository`、`ProjectsEndpoints`、
`TaskClaimer`、DI/Program、`SqlProjectTests.cs`、文档。
