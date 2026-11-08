# AOS-033b：知识库向量检索

状态：Completed

## 能力

- `KnowledgeDocument.EmbeddingJson` 保存向量，迁移 `AddKnowledgeEmbedding`。
- 可插拔 `IEmbeddingClient`：
  - `OpenAiEmbeddingClient`，OpenAI-compatible `/embeddings`。
  - 配置 `Knowledge:Embedding`（Enabled/BaseUrl/ApiKey/Model）。
  - 默认关闭，未配置时退化为关键词检索。
- 创建文档时写入向量；搜索时对 query 取向量，候选最多 500 条按余弦相似度排序。
- 无 embedding 或调用失败时自动回退关键词 `LIKE`。

## 配置

```json
"Knowledge": {
  "Embedding": {
    "Enabled": true,
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": null,
    "Model": "text-embedding-3-small"
  }
}
```
`ApiKey` 用环境变量 `Knowledge__Embedding__ApiKey`。

## 验证

- 后端构建：✅ 0 错误
- 后端全量测试：✅ **331 通过 / 5 跳过**
- 向量排序用例：✅ cat 文档排在 dog 前
- CI：推送后确认

## 已知限制

1. 搜索在内存计算余弦，候选上限 500；大数据量需向量库。
2. 文档更新无重新嵌入接口（当前只创建）。
3. 未做 embedding 维度校验/批量索引。
4. 外部 embedding 未在 CI 调用。
