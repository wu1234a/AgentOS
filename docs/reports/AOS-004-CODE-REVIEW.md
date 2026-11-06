# AOS-004 独立代码审查报告

**审查对象：** commit `2700396`（AOS-004 Rules Engine）
**审查方式：** DSH 原生 Codex 规范审查（独立只读子 Agent）
**审查运行：** `6040e446-7115-4923-bcca-1fd03451851a`
**结论：** 无 Blocker；2 个 P1（High）、4 个 P2、4 个 P3。全部修复或已明确记录。

> 说明：本次先尝试 `codex exec review --commit 2700396`（Codex CLI），但 Codex 的本地
> 命令执行被工作区沙箱拦截（`CreateProcessAsUserW` Win32 5），GitHub MCP 也因网络中断
> 无法读取提交，Codex 自述“无法审查”。因此改用 DSH 原生审查完成。

## 1. 发现与处理

| # | 级别 | 问题 | 处理 |
|---|---|---|---|
| 1 | P1 | `sourcePattern` 校验上限 120，但列宽 `nvarchar(64)`；65–120 字符的输入会通过校验并在写库时截断 → 500 | **已修复**：`RuleValidator` 对 `sourcePattern` 单独限制 64；新增测试 `Source_pattern_longer_than_the_column_is_rejected` |
| 2 | P1 | 前端列表页“编辑”用 list 投影填充表单，`matchJson`/`dispatchJson` 被重置为默认值，PUT 会清空规则条件 | **已修复**：`openEdit` 先 `fetchRule(id)`，用详情回填两个 JSON 文档 |
| 3 | P2 | `{"subjectTypes":null}` 反序列化后属性为 null，`.Any()` 抛 NRE → 500 | **已修复**：解析时把 null 归一为空数组；新增测试 `Null_condition_arrays_are_treated_as_empty` |
| 4 | P2 | `Skills` 唯一索引配置缺 `HasFilter`，与迁移/快照不一致，下一次迁移会生成漂移 | **已修复**：配置补 `HasFilter("[ProjectId] IS NOT NULL")`；用 `has-pending-model-changes` 验证模型与快照一致 |
| 5 | P2 | “乱序输入顺序稳定”测试用 `Name.GetHashCode()`，所有 name 相同 → 实际未乱序，断言恒真 | **已修复**：改为固定置换数组 |
| 6 | P2 | 真实 `AutomationRuleRepository` / `RuleReferenceLookup` 无测试，CRUD 与项目归属只在内存假实现上验证 | **已修复**：新增 SQL 测试 `Rule_service_round_trips_through_the_real_repository`（create/get/list/update + 项目过滤）。该测试还发现了下面的 EF 投影缺陷 |
| 7 | P2（测试发现） | `RuleDetail` 含 `JsonElement`，在 EF `Select` 中投影导致 `InvalidOperationException`（Equal 未定义） | **已修复**：先物化匿名投影（纯标量），再在内存里解析 JSON 构造 DTO |
| 8 | P3 | `FindSkillAsync` 排序缺 Id 兜底，同一时间戳的全局 Skill 可能解析不稳定 | **已修复**：追加 `.ThenBy(skill => skill.Id)` |
| 9 | P3 | PUT 省略 `isEnabled` 会把启用中的规则静默禁用 | **已修复**：省略时保留当前状态，并按当前状态决定是否校验引用；文档已注明 PUT 语义；SQL 测试断言 version=2 且仍启用 |
| 10 | P3 | `ParentTaskId` 无外键/索引 | **记录为推迟**：`DATABASE-ER` §3.16 明确留给 AOS-016；AOS-004 不设置该字段 |
| 11 | P3 | JSON 解析错误把 `JsonException.Message`（含内部类型名）回传给调用方 | **已修复**：改为固定文案，不拼接异常消息 |
| 12 | P3 | “Received 事件重派发”分支无断言 | **已修复**：新增 `Redelivery_of_a_received_event_retries_dispatch_and_reports_created_tasks` |

## 2. 修复后验证

- 后端：`dotnet build` 0 错误；全量测试 **137 通过 / 0 失败**（含真实 SQL Server）。
- 模型一致性：`dotnet ef migrations has-pending-model-changes` → `No changes`。
- 前端：`npm run lint`、`npm run build` 通过。
- CI：见 GitHub Actions run（AOS-004 提交）。

## 3. 审查未覆盖项

- 审查为只读，且本机 testhost 仍受工作区沙箱限制，审查者未能独立运行 `dotnet test`；
  测试结论来自代码阅读。本次修复后的实际执行结果见上文。
