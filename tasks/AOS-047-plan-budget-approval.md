# AOS-047：立项预算、版本与启动审批

状态：Completed
优先级：P1
依赖：AOS-045、AOS-046

## 1. 范围

- 项目规划 Token 预算与使用量统计。
- 规划会记录模型调用次数。
- 修订时保留上一版最终方案，前端可对比。
- 启动执行增加可选的二次审批门槛。
- 新建计划可配置 Token 预算和是否需要二次启动审批。

## 2. API

```text
POST /api/v1/project-plans/{id}/approve-release
```

`release` 在 `requiresReleaseApproval=true` 且未二次批准时返回 409。

## 3. 验收

- [x] TokenBudget / TokensUsed / ModelCalls 持久化。
- [x] UI 显示 Token 使用量和模型调用次数。
- [x] 修订保留 PreviousFinalPlanJson / PreviousRevision。
- [x] UI“上一版”标签页。
- [x] 二次启动审批 API 与 UI。
- [x] 未审批时 release 返回 409。
