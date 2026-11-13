# AOS-052 Delivery Report：生成阶段依赖校验与模型级价格

- 日期：2026-11-13
- 状态：Completed
- 范围：Chair 输出依赖校验、模型 profile 级价格

## 1. 变更文件

后端：

- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanWorker.cs`
- `backend/AgentOS.Api/ProjectPlanning/ProjectPlanModelRunner.cs`

文档：

- `tasks/AOS-052-generation-check-profile-pricing.md`（新增）
- `docs/reports/AOS-052-DELIVERY-REPORT.md`（本文件）

## 2. 生成阶段校验

Chair 返回后立即：

- 解析任务
- 校验 taskKey 唯一
- 校验 dependsOn 存在
- 校验无环

失败直接：

```text
Plan.Status = Failed
Error = Planner chair returned an invalid dependency graph: ...
```

不再等到人工批准才暴露依赖错误。

## 3. 模型级价格

ModelProfile.ConfigurationJson 可增加：

```json
{
  "baseUrl": "https://api.deepseek.com/v1",
  "model": "deepseek-v4-pro",
  "inputPricePerMillion": 0.14,
  "outputPricePerMillion": 0.28
}
```

估算优先级：

```text
profile 价格 > Planner 全局默认价格
```

全局默认仍为：

```text
输入 0.5 USD / 1M
输出 2.0 USD / 1M
```

## 4. 验证

```text
dotnet build backend/AgentOS.slnx → 0 errors
AgentOS tests → 309 passed / 64 skipped / 0 failed
```

## 5. 已知限制

- 依赖校验只检查结构，不检查业务合理性。
- 价格是配置值，不是供应商实时账单。
- 没有按模型自动拉取价格表。
- 没有真实登录身份。
