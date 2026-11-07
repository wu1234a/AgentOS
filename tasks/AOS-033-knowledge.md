# AOS-033：知识库与检索（关键词 MVP）

状态：Completed
优先级：P1
依赖：现有 Projects/开发 Key
来源：Kimi Planner 草案 `01a1b7d2-c90b-75be-8df5-63b3969acc10`（已批准）

## 1. 范围

- `KnowledgeDocuments`：ProjectId?、Title、Content、TagsJson、Source、时间。
- API `/api/v1/knowledge`：创建、列表/关键词检索、详情、删除。
- 检索：标题/正文/标签 `Contains`，按更新时间倒序，limit 1–50，返回片段。
- 按项目过滤；无向量/embedding。
- 迁移 `AddKnowledgeDocuments`。

## 2. 不在范围

- 向量检索/embedding/外部模型、语义重排。
- 前端、文件导入、分块/索引任务。
- 权限模型（仅开发 Key）。

## 3. 验收

- [x] 创建/搜索/片段/项目过滤/删除。
- [x] 迁移与测试。
- [x] 后端全量测试、CI 通过。
