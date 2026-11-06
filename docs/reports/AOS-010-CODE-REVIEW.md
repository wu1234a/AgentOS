# AOS-010 独立代码审查报告

**审查对象：** commit `1d3248d`（AOS-010 Model Router / Provider Profiles）
**审查方式：** Codex CLI `codex exec review`
**结论：** 1 个 P1、2 个 P2，全部修复。

## 1. 发现与处理

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | 只检查 `ConfigurationJson` 顶层字段，嵌套对象/数组中的 `api_key`/`secret` 等可绕过校验并被持久化/返回 | 改为递归遍历对象与数组；新增嵌套 `credentials.api_key` 拒绝测试 |
| 2 | P2 | 仅用 `ruleId` 解析时不会使用 Rule 关联的 Agent 作为回退 | Rule 查询同时取 `AgentId`，未显式给 Agent 时用它继续回退；新增 rule-only 测试 |
| 3 | P2 | 前端编辑 Profile 时未提交 `maxCostPerTask`，服务端会把原值清空 | 表单加入成本上限字段并在更新 payload 中保留/提交；前端 lint/build 通过 |

## 2. 修复后验证

- 后端全量测试：**256 通过 / 4 跳过 / 0 失败**。
- 前端 lint / build：通过。
- CI：修复提交后确认。

## 3. 说明

- 审查为只读环境，未独立执行测试；以上为修复后本地实际结果。
