# AOS-003 独立代码审查报告

**审查对象：** AOS-003 未提交变更（含未跟踪新文件）
**审查方式：** `dsh_code_review`（Codex rubric，独立只读子 Agent）
**审查运行：** `57406aea-39f0-4573-8e59-f19dcd3f1dc6`
**结论：** 无 Blocker；2 个 P2、2 个 P3、1 个交付项。全部处理完毕或已在交付报告中说明。

## 1. 审查结论摘要

核心设计通过审查：唯一冲突确实由数据库 `UX_Events_Source_DedupeKey` 兜底，不存在“先查后插”；
SQL 2601/2627 捕获、失败后 `Detach`、二次查询取获胜行与 SQL Server 唯一键锁语义一致；迁移的
空键/重复键修复不删行且顺序正确；PayloadHash 规范化策略确定性良好并与文档一致。

## 2. 发现与处理

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P2 | `DuplicateEventLogSampler` 的 `MaxTrackedKeys` 并非真正上限，且超限后每次调用都做全量 `Prune`，热路径 O(n) | **已修复**：改为硬上限 + 摊还清理，超过上限时先淘汰过期窗口、再淘汰最旧窗口至一半容量，并用 `Interlocked` 保证同一时刻只有一次清理；新增“洪水键有界”和“过期窗口优先淘汰”测试 |
| 2 | P2 | 冲突日志与重复日志共用同一采样计数，409 可能一条日志都没有 | **已修复**：采样键按 `duplicate` / `conflict` 分通道计数，每次窗口内第一条冲突必定记录 |
| 3 | P3 | `FakeEventRepository` 用 `Ordinal` 比较，与 SQL Server 默认排序规则（不区分大小写、忽略尾随空格）不一致 | **已修复**：改为 `TrimEnd()` + `OrdinalIgnoreCase`，并在注释中说明真实权威仍是数据库排序规则 |
| 4 | P3 | 历史行 `PayloadHash` 惰性回填的 SQL 写路径没有真实数据库覆盖 | **已修复**：新增 `Legacy_row_without_hash_is_backfilled_on_the_first_duplicate` SQL 集成测试（迁移前写入旧行 → 命中重复 → 断言回填 64 位小写十六进制且不新增行） |
| 5 | P2 | 缺少 `docs/reports/AOS-003-DELIVERY-REPORT.md` | **已补齐**：见 `docs/reports/AOS-003-DELIVERY-REPORT.md` |

## 3. 审查未覆盖项（已记录）

- 审查为只读，未运行 `dotnet build` / `dotnet test`；构建与测试由交付流程单独执行。
- 审查给出的“验收标准核对”表格中，除交付报告一项外均为满足；本次交付已补齐报告并附执行结果。
