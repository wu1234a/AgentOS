# AOS-048：子集启动、费用估算与任务依赖

状态：Completed
优先级：P1
依赖：AOS-047

## 1. 范围

- release 支持指定 BacklogItemIds，只启动选中项。
- 进度接口返回 Backlog 明细，前端支持多选。
- 规划 Token 成本估算（USD）。
- Chair 任务增加 taskKey / dependsOn。
- 前端展示任务依赖。

## 2. 验收

- [x] release 空 body 启动全部 Hold。
- [x] release 指定 IDs 只启动选中项。
- [x] 非本计划/非 Hold ID 返回 400。
- [x] 前端复选框选择并启动选中。
- [x] 显示 TokensUsed / TokenBudget / EstimatedCost。
- [x] 任务表展示 Key 和依赖。
- [x] 依赖仅展示，尚未在执行层强制。
