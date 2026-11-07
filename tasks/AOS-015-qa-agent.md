# AOS-015：QA Agent（命令级结果，第一阶段）

状态：Completed
优先级：P1
依赖：AOS-013b validationCommands、AOS-014 Review
来源：Kimi Planner 草案 `01a1b47b-6210-704f-9b9d-adb93421f51a`（已批准）

## 0. 对 Kimi 草案的修订

Kimi 草案假设 QA 会重新执行 validationCommands，并采集 JUnit/Cobertura；但：
- 重新执行会重复跑测试、放大 Runner 负载，且需要新的 Runner 命令通道；
- AOS-013b 目前只把校验结果写日志，没有结构化结果，也没有上传测试报告。

因此本阶段改为**复用既有校验结果**：
1. Runner 在 AOS-013b 校验后上传 `validation-results.json`（命令、状态、退出码、耗时、输出摘要）。
2. QA 不重跑命令，只读取该 Artifact 并标准化、持久化、展示；不接收命令/路径。
3. JUnit/Cobertura 解析与用例级结果放入 **AOS-015b**，本阶段只做命令级 QA。

## 1. 目标

为已终态（Succeeded/Failed）的 Coding Run 手动创建 QA Run，读取既有校验结果，生成
`Passed/Failed/Inconclusive` 结论，持久化命令结果并在 Coding Run 详情展示。
QA 不修复代码、不重跑测试、不自动合并、不改写 Review findings。

## 2. 范围

### database

- `QaRuns`：CodingRunId、AttemptNo、Status (`Completed/Faulted`)、Conclusion
  (`Passed/Failed/Inconclusive`)、CommandCount/Passed/Failed、ErrorSummary、CreatedAtUtc。
  唯一索引 `(CodingRunId, AttemptNo)`。
- `QaCommandResults`：QaRunId、Ordinal、CommandText、Status (`Passed/Failed/Timeout/Error`)、
  ExitCode、DurationMs、OutputExcerpt；随 QaRun 级联。
- 迁移 `AddQaAgent`。

### backend

- Runner `ValidationCommandRunner` 返回耗时；`CodexTaskHandler` 汇总校验结果并上传
  `validation-results.json`（最多 10 条，输出摘要 4000 字符，不含环境变量）。
- `QaService`：
  - 仅 Succeeded/Failed Coding Run 可创建 QA；
  - 读取任务 `validation-results.json` Artifact；缺失 → 409 `validation-results-missing`；
  - 解析失败/超限 → Faulted + Inconclusive；
  - 结论：全部命令 Passed → Passed；任一 Failed/Timeout/Error → Failed；无命令 →
    Inconclusive；
  - 每次 POST 生成新 AttemptNo，保留历史。
- 不新增 Runner 执行通道、不重跑命令、不读文件系统路径。

### API

- `POST /api/v1/coding-runs/{id}/qa-runs`：创建/执行 QA，返回 QA 摘要。
- `GET /api/v1/coding-runs/{id}/qa-runs/latest`：最近一次 QA（含命令结果）；无 QA 404。
- 409：Run 未终态、无 validation-results、QA 已在进行（本阶段同步，进行中窗口极短）。

### frontend

- `/coding-runs` 详情增加 QA 面板：运行 QA、状态/结论/Attempt、命令表格（状态/退出码/
  耗时/输出摘要）、空态与错误提示。
- 所有文本纯文本渲染，不使用 `v-html`；不提供修复/合并按钮。

## 3. 不在本阶段范围（AOS-015b）

- JUnit XML / Cobertura 解析、用例级结果、覆盖率。
- 重新执行 validationCommands、取消/重试队列、异步 QA Worker。
- CI 平台集成、flaky 检测、LLM 根因分析、自动修复/合并。

## 4. 安全

- 只读既有 Artifact；API 不接收命令或路径。
- 输出摘要限长；不采集环境变量/密钥；QA 不改变 Coding Run 或 Review。
- 同一 Run 的并发 QA 用 `(CodingRunId, AttemptNo)` 唯一索引和进程内锁保护。

## 5. 验收

- [ ] Runner 校验后上传 `validation-results.json`，成功/失败路径都有。
- [ ] QA 读取 Artifact 生成命令级结论；缺失/损坏安全失败。
- [ ] 每次 QA 生成新 Attempt，历史保留；GET latest 返回最近一次。
- [ ] 前端详情展示 QA 状态、结论、命令结果与错误。
- [ ] 后端全量测试、前端 lint/build、CI 通过。
- [ ] 不重跑测试、不修改代码/Review/Coding Run。

## 6. 交付

`docs/reports/AOS-015-DELIVERY-REPORT.md`。
