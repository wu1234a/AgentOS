# AOS-035 交付报告：业务事件接入

**状态：Completed**
**完成时间：2026-11-08**
**任务文件：** `tasks/AOS-035-business-events.md`

## 1. 能力

- `POST /api/v1/business-events`：3derp/emperor 统一入口。
- 组合 `source.eventType`，必填 project/dedupeKey。
- 走 EventService → Rules → Tasks，自动幂等。
- 响应 202/200/409/400。

## 2. 测试与构建

| 检查 | 结果 |
|---|---|
| 后端构建 | ✅ 0 错误 |
| 后端全量测试（含 SQL） | ✅ **328 通过 / 5 跳过 / 0 失败** |
| 业务事件用例 | ✅ 创建 + 重复 + 未知 source |
| CI | 见对应 run |

### 2.1 独立代码审查\n\nCodex CLI 审查发现 2 个 P2（可选字段默认值、无时区时间），全部修复，详见 `docs/reports/AOS-035-CODE-REVIEW.md`。\n\n## 3. 已知限制

1. 仅 3derp/emperor 两个 source；新增需改代码。
2. 无专用签名，复用开发 Key。
3. 无事件字段 schema 校验（仅 Event Hub 通用校验）。
4. 无前端调试/重放。
5. 业务系统需自行实现重试与 DedupeKey。

## 4. 变更文件

`BusinessEventService`、`BusinessEventEndpoints`、Program、`BusinessEventTests.cs`、文档。
