# AOS-053：项目目标与项目空间

状态：Completed
优先级：P0
依赖：AOS-045、AOS-049

## 1. 范围

- Project 增加 Goal/Brief/PersonalNotes/AcceptanceCriteria/Repository/Status。
- 支持人工注册项目时填写目标和简报。
- 支持从目标生成项目草稿，人工确认后创建。
- 项目详情抽屉展示目标、简报、个人想法、验收标准。
- 项目状态支持 Draft/Active/OnHold/Completed/Archived。
- Project Plan 创建时复用项目 Goal/Brief/Repository。

## 2. 兼容性

- 旧项目 Status 默认 Active，其他字段为空。
- 原有创建/编辑/启停 API 保持可用。
- ProjectPlan API 保持兼容；未传 goal/title/repository 时从项目读取。

## 3. 验收

- [x] 项目可设置目标/简报/个人想法/验收标准/仓库/状态。
- [x] 从目标生成草稿并预填创建表单。
- [x] 项目详情可查看完整内容。
- [x] 项目状态可编辑。
- [x] 项目计划创建可复用项目字段。
- [x] 旧项目不受影响。
