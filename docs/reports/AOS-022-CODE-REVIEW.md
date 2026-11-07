# AOS-022 独立代码审查报告

**审查对象：** commit `b3a366a`
**审查方式：** Codex CLI
**结论：** 1 个 P1 + 2 个 P2，全部修复。

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | GitHub 事件没有 Project，规则管线无法创建任务 | 新增 `RepositoryProjects` 映射；未映射仓库返回 503，事件携带项目 slug |
| 2 | P2 | 全局 Kestrel 请求上限 256KB，低于 Webhook 1MB 声明 | 读取体前将端点 `IHttpMaxRequestBodySizeFeature` 设为 1MB |
| 3 | P2 | 结构非法但签名正确的 payload 触发 500 | 规范化包裹异常处理，返回 400 `invalid-payload` |

## 验证

- 后端全量测试：**313 通过 / 5 跳过 / 0 失败**。
- CI：修复提交后确认。
