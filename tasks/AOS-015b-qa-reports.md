# AOS-015b：QA 用例级结果与覆盖率

状态：Completed
优先级：P1
依赖：AOS-015
来源：Kimi Planner 草案 `01a1b4bb-becf-7467-b3f1-4d400075101d`（已批准）

## 0. 修订

Kimi 草案要求重新重跑报告上传/QA 摄入；本阶段沿用 AOS-015 的“只读既有 Artifact”：
- Runner 在 validationCommands 后收集固定位置的 `junit/*.xml`、`coverage.cobertura.xml` 并上传。
- QA 创建时解析这些 Artifact，不重跑测试、不新增上传 API。
- 报告缺失不改变命令级结论；解析失败记录 issue。

## 1. 范围

- Runner 收集 `junit/*.xml`（最多 20 个，单文件 ≤2MB）和根目录
  `coverage.cobertura.xml`（≤4MB），命名为 `junit-N.xml` 上传。
- `QaReportParser`：
  - XmlReader 禁用 DTD/外部实体，限制文档大小；
  - JUnit `testsuite/testcase`、嵌套、failure/error/skipped、time→ms；
  - Cobertura 根节点 line-rate/branch-rate（0–1 → 百分比）。
- `QaTestCases` 表；`QaRuns` 增加 ReportStatus、LineCoverage、BranchCoverage、ReportIssues。
- QA 创建时解析并持久化；状态 `not_provided/success/partial/failed`。
- QA 面板展示用例通过数、失败/错误数、行/分支覆盖率。

## 2. 不在范围

- 不重跑测试、不生成报告、不下载原始 XML。
- 不解析包/类/文件级覆盖率、不做趋势/flaky/LLM 分析。
- 不自动修复/合并。

## 3. 验收

- [x] Runner 上传 JUnit/Cobertura Artifact。
- [x] 解析多状态用例、耗时、失败详情。
- [x] 解析 Cobertura 覆盖率；缺失显示 `—`。
- [x] 无报告保持 not_provided；损坏报告记录 issue 且不误判成功。
- [x] 后端全量测试、前端 lint/build、CI 通过。
