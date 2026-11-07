# AOS-016b 独立代码审查报告

**审查对象：** commit `fd7371a`
**审查方式：** Codex CLI `codex exec review`
**结论：** 2 个 P1、2 个 P2，全部修复。

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | 仅有应用层 phaseKey 查询，无数据库唯一索引 | 迁移的 PhaseKey 改为 nvarchar(64)，增加 `(ParentTaskId, PhaseKey)` 过滤唯一索引 |
| 2 | P1 | 依赖只按 ID 查询，跨项目依赖可通过 | `GetDependencyStatusesAsync`/`GetDependencyIdsAsync` 增加 ProjectId 过滤 |
| 3 | P2 | Description 超过 2000 会数据库截断 500 | 创建前校验并返回 400 |
| 4 | P2 | `UnresolvedDependencyIds` 包含已成功依赖 | 按依赖状态过滤，保留全部 `DependsOnTaskIds` |

## 验证

- 后端全量测试：**306 通过 / 5 跳过 / 0 失败**。
- CI：修复提交后确认。
