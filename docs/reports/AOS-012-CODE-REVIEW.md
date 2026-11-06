# AOS-012 独立代码审查报告

**审查对象：** commit `39501a8`（AOS-012 Planner Agent 与 Kimi 接入）
**审查方式：** Codex CLI `codex exec review`
**结论：** 1 个 P1、7 个 P2，全部修复。

## 1. 发现与处理

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | 迁移整理时误删了 `AddSkillGlobalVersionIndex`，未升级数据库会丢失全局 Skill 版本唯一索引 | 从 `e7873e0` 恢复 `.cs` 与 `.Designer.cs`，重新生成 Planning 迁移，`has-pending-model-changes` 通过；本地测试库重建后全量通过 |
| 2 | P2 | 批准/拒绝并发时两个请求都能读到 PendingReview，后写覆盖先写 | 改为条件更新 `WHERE Status = PendingReview`，只有一个成功，另一个返回 409；新增并发决策测试 |
| 3 | P2 | Planner 生成的 HTTP 请求继承 HttpClient 默认 100s 超时，配置更长超时时会 500 | 命名客户端设为无限超时，由每请求 CTS 控制；取消路径返回 provider timeout |
| 4 | P2 | Provider 返回 `{"choices":null}`/`[null]` 会抛未处理异常 | 访问前校验 Array/Object/String 的 ValueKind，返回 502 错误 |
| 5 | P2 | `temperature`/`maxTokens` 为字符串/空值时 `TryGetDouble` 抛异常 | 仅当 ValueKind=Number 时转换，否则用默认值 |
| 6 | P2 | 前端生成草案沿用 Axios 10s 超时，慢模型请求提前失败 | 生成请求单独设置 120s 超时 |
| 7 | P2 | Planner HTTP 客户端自动跟随 307/308，可能绕过主机白名单 | 禁用 AllowAutoRedirect，重定向视为失败 |
| 8 | P2 | 前端草案列表只有第一页 | 增加 total/page/pageSize 与分页控件 |
| 9 | P2 | Context 长度校验写死 8000，不遵守配置 | 改为 `Planner:MaxContextCharacters` |

## 2. 修复后验证

- 后端全量测试：**271 通过 / 5 跳过 / 0 失败**（测试库已重建，历史迁移顺序验证通过）。
- 前端 lint / build：通过。
- 迁移：无 pending changes。
- CI：修复提交后确认。

## 3. 说明

- 审查为只读环境；以上为修复后本地实际结果。
- 未执行真实 Kimi 调用，Provider 行为由模拟 HTTP 响应覆盖。
