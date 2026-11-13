# AOS-052：生成阶段依赖校验与模型级价格

状态：Completed
优先级：P1
依赖：AOS-050、AOS-051

## 1. 范围

- Chair 生成后立即校验依赖图，非法计划直接 Failed。
- 模型 profile 可配置 input/output 每百万 Token 价格。
- 费用估算优先使用 profile 价格，否则使用 Planner 全局默认。

## 2. 验收

- [x] 生成阶段缺依赖/环直接失败。
- [x] profile 配置支持 inputPricePerMillion / outputPricePerMillion。
- [x] 估算费用优先使用 profile 价格。
- [x] 默认价格仍可用。
