# AOS-029 交付报告：模型用量与费用统计

**状态：Completed**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-029-usage.md`

## 1. 能力

- `GET /api/v1/usage/statistics`：
  - 按项目、UTC 日期、Provider、Model 聚合
  - 调用次数、输入/输出/总 Token
  - 按 `Usage:Pricing` 估算费用
  - 缺失/禁用价格返回 `null` + `MissingPrice`
- 默认最近 30 天，最大 366 天，`[from,to)`。
- 无数据库迁移。

## 2. 配置

```json
"Usage": {
  "Pricing": {
    "codex|gpt-5-codex": {
      "InputPerMillionTokens": 1.25,
      "OutputPerMillionTokens": 10.0,
      "Currency": "USD",
      "Enabled": true
    }
  }
}
```

## 3. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **320 通过 / 5 跳过 / 0 失败** |
| 用量统计用例 | ✅ 1.5M token、$4 估算、调用次数 1 |
| CI | 见对应 run |

## 4. 已知限制

1. 无前端图表/仪表盘。
2. 无历史价格版本；按当前配置重估历史。
3. 无实时预算拦截；MaxCostPerTask 仍独立。
4. 无价格配置校验（负数/币种）启动校验。
5. 仅按 Task→Project 关联；无 CodingRun 直接用量。
6. 真实模型用量依赖适配器写入。

## 5. 变更文件

`UsageOptions`、`UsageStatisticsService`、`UsageEndpoints`、Program/appsettings、
`SqlUsageStatisticsTests.cs`、文档。
