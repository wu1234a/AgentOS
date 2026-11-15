# AOS-055：任务文档、里程碑与人工验收门

状态：Completed（MVP）
优先级：P0
依赖：AOS-053、AOS-054

## 1. 范围

- BacklogItem 增加 RequiredRole/RiskLevel/TaskDocumentMarkdown。
- 计划批准时为每个任务生成 Markdown 任务文档。
- 项目详情展示任务文档。
- 计划批准时按 roadmap 生成 ProjectMilestone。
- 里程碑人工验证。
- 全部里程碑验证后项目进入 Acceptance。
- 提供最终验收 API，生成验收报告并关闭项目。
- 系统停在人工最终验收，不自动关闭。

## 2. 验收

- [x] 任务文档生成并含角色/风险/依赖/验收标准。
- [x] Backlog 视图返回任务文档。
- [x] 里程碑生成。
- [x] 里程碑人工验证。
- [x] 全部验证进入 Acceptance。
- [x] 最终验收 API 存在。
- [x] 前端展示里程碑和最终验收按钮。
- [ ] 独立角色队列（仅记录 RequiredRole，尚未按角色分派 Agent）。
- [ ] 独立 Security/Performance Agent。
