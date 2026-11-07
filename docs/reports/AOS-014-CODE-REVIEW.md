# AOS-014 独立代码审查报告

**审查对象：** commit `6fb1cc1`（AOS-014 Review Agent）
**审查方式：** Codex CLI `codex exec review`
**结论：** 2 个 P1、4 个 P2，全部修复。

## 1. 发现与处理

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | Review 传入 read-only policy，但 Codex Adapter 只读配置，`workspace-write` 配置下仍可写 | Adapter 改为取配置与请求策略的**更严格**沙箱；请求 read-only 永远只读，不会扩权 |
| 2 | P1 | 前端 `createReview` 沿用 Axios 10s 超时，同步审查必然中断 | Review 请求单独设置 620s 超时 |
| 3 | P2 | 客户端取消后 Review 留在 Running，后续无法重试 | 取消时用独立 token 持久化 `Failed/review-cancelled`，允许重试 |
| 4 | P2 | UI 创建 Run 没有 baseRevision 输入，无法生成变更集 | 创建表单新增 Base revision 并随请求发送 |
| 5 | P2 | `{}`/`findings` 非数组被当成 NoIssues | 强制 root object + summary + findings 数组，否则 `review-output-invalid` |
| 6 | P2 | 并发创建/重试可能触发唯一键或 RowVersion 异常 | ReviewService 按 CodingRunId 串行化同一进程内的创建/重试 |

## 2. 修复后验证

- 后端全量测试：**292 通过 / 5 跳过 / 0 失败**。
- 前端 lint/build：通过。
- 迁移：无 pending changes。
- CI：修复提交后确认。

## 3. 说明

- 审查为只读环境；以上为修复后本地实际结果。
- 多实例并发仍依赖数据库唯一键兜底；当前部署为单 API 实例。
- Review 同步最长 600s，未改异步队列。
