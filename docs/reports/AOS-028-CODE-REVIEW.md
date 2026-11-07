# AOS-028 独立代码审查报告

**审查对象：** commit `c1e19ae`
**审查方式：** Codex CLI
**结论：** 1 个 P1 + 3 个 P2，全部修复。

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | 领取 UPDATE 的子查询 `[ProjectId]` 解析到内层 running 表，跨项目计数 | UPDATE 使用 `target` 别名，显式按 `target.ProjectId` 计数 |
| 2 | P2 | PATCH 省略配额时把配额清成 NULL | 仅当请求显式提供配额时更新，省略保留原值 |
| 3 | P2 | 停用项目与领取竞态 | 领取 UPDATE 增加 `Projects.IsEnabled=1` 条件 |
| 4 | P2 | 并发创建同 slug 返回 500 | Repository `TryAddAsync` 捕获唯一冲突，返回 409 |

## 验证

- 后端全量测试：**318 通过 / 5 跳过 / 0 失败**。
- CI：修复提交后确认。
