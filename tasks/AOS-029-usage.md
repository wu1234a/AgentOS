# AOS-029：模型用量、Token 和费用统计

状态：Completed
优先级：P1
依赖：现有 UsageRecords、Tasks、ModelProfile
来源：Kimi Planner 草案 `01a1b793-e7d3-7de1-b9de-4db0a7cf0fdb`（已批准）

## 0. 修订

Kimi 草案含价格校验、权限、分页、数据库下推。本阶段先交付：

1. 只读统计 API，按项目/UTC 日期/Provider/Model 聚合。
2. 调用次数、输入/输出/总 Token、按配置估算费用。
3. 缺失/禁用价格返回 `estimatedCost:null` + `MissingPrice`。
4. 不做数据库迁移、前端、历史价格、执行链路改动。

## 1. 范围

- 配置 `Usage:Pricing`：key `provider|model`，字段 InputPerMillionTokens/OutputPerMillionTokens/Currency/Enabled。
- `GET /api/v1/usage/statistics?projectId=&from=&to=`：
  - 默认最近 30 天；`[from,to)`；最大 366 天。
  - 按 UTC 日期、项目、Provider、Model 排序。
  - 费用 = input/1e6*inputPrice + output/1e6*outputPrice，保留 6 位。
- 无数据库迁移；仅读取 UsageRecords join Tasks。

## 2. 不在范围

- 外部账单/汇率/发票、实时预算拦截、价格管理 UI。
- 前端图表、历史价格版本、历史回填。
- 修改执行/UsageRecord 写入链路。

## 3. 安全

- 开发 Key 认证；projectId 必须存在。
- 只读；不记录 prompt/凭据。
- 费用明确为估算，缺失价格不伪装 0。

## 4. 验收

- [x] 聚合调用次数/Token/费用。
- [x] 时间范围/projectId 过滤。
- [x] 缺失价格 null + MissingPrice。
- [x] 后端全量测试、CI 通过。
