# AOS-013 独立代码审查报告

**审查对象：** commit `578f9bc`（AOS-013 Coding Run 编排）
**审查方式：** Codex CLI `codex exec review`
**结论：** 2 个 P1、6 个 P2，全部修复。

## 1. 发现与处理

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | 任务直接继承 Planner 的 API 模型 Profile，Runner 会以 `model-provider-unsupported` 拒绝执行 | 任务不再设置 `ModelProfileId`，由 AOS-010b 解析 Agent/规则默认 Profile；Planner Profile 只保留在快照中作为来源 |
| 2 | P1 | 未校验请求项目与草案项目一致，可能把 A 项目草案在 B 项目工作区执行 | 草案有 ProjectId 时必须与请求一致，否则 400 `project-mismatch` |
| 3 | P2 | 列表不投影任务状态，筛选/计数使用旧状态 | 列表前批量投影未终态 Run 的状态 |
| 4 | P2 | 重试前未投影，任务已失败但 Run 仍显示 Queued 时拒绝重试 | 重试前先投影，再判断是否终态 |
| 5 | P2 | 并发读取可能同时分配事件序号/触发 RowVersion 冲突，返回 500 | 按 Run 加进程内串行锁；读取侧捕获并发异常并降级为返回当前状态 |
| 6 | P2 | 重试丢失原任务的自定义优先级/超时 | 复制上一任务的 Priority/TimeoutSeconds |
| 7 | P2 | 重试未重新校验 Agent，禁用后仍可能入队 | 重试前检查 Agent 启用且属于原项目 |
| 8 | P2 | Provider 错误体可能回显非 `sk-` 格式的 Key | 先按请求中的实际 API Key 精确替换，再执行前缀正则脱敏 |

## 2. 修复后验证

- 后端全量测试：**278 通过 / 5 跳过 / 0 失败**。
- 新增用例：项目不匹配、任务不继承 Planner Profile、Provider Key 精确脱敏。
- 前端 lint/build：通过。
- 迁移：无 pending changes。
- CI：修复提交后确认。

## 3. 说明

- 审查为只读环境；以上为修复后本地实际结果。
- AOS-013 第一阶段仍未实现 Runner 内校验命令、独立工作区与细粒度状态机，见交付报告“已知限制”。
