# AOS-008：Execution Logs & Observability

状态：Completed  
优先级：P0  
依赖：AOS-007

## 1. 目标

建立任务执行可观测性：用户可以从管理后台查看事件、任务、Attempt、Runner、日志和产物。系统能够追踪一次任务从事件触发到执行完成的完整链路。

## 2. 数据库范围

完善： - TaskLogs - Artifacts - TaskAttempts - Workers -
UsageRecords（如当前 Adapter 能提供可信用量；无法提供时允许为空） -
OutboxMessages

日志和产物必须与 TaskId、AttemptId 关联。

## 3. 日志规范

结构化日志字段： - TimestampUtc - Level - ProjectId - EventId（可空） -
TaskId - AttemptId - WorkerId - Component - Message - Properties

要求： - 支持按 Sequence 排序。 - 支持按级别过滤。 - 大段 stdout/stderr
可存为 Artifact，数据库只保存摘要和引用。 -
日志写入失败不能让任务被错误标记为成功。 - 对
Token、密码、Cookie、连接字符串和个人敏感数据脱敏。

## 4. Artifact 管理

第一版使用本地受控 Artifact Root： - 按 Project/Task/Attempt
分层目录。 - 文件名安全化。 - 保存 SHA-256、大小、MediaType。 -
下载通过授权 API。 - 不允许客户端提交任意 StoragePath。 -
上传文件大小限制和类型限制。 -
清理按保留策略执行，不允许任务随意删除其他任务产物。

## 5. 管理后台页面

完善： - Dashboard：事件数、待执行任务、运行任务、失败任务、在线
Runner。 - Event List /
Detail：事件来源、类型、摘要、处理状态、关联任务。 - Task List /
Detail：状态、优先级、Agent、Skill、项目、创建时间。 - Attempt
Timeline：每次尝试的开始/结束、Runner、退出码。 - Live/Recent
Logs：分页查看日志；可用 SignalR 推送新日志。 -
Artifacts：产物名称、大小、哈希、下载入口。 - Runner
List：在线状态、最后心跳、版本、能力。

前端仍然使用 JavaScript，不引入 TypeScript。

## 6. SignalR

- 仅用于状态推送和 UI 更新。
- 数据库是最终事实来源。
- 断线重连后前端重新拉取 REST API。
- SignalR 不承担可靠任务队列职责。
- 必须验证连接身份和项目访问权限。

## 7. 统计指标

至少显示： - 总事件数、重复事件数。 -
总任务数、Queued、Running、Succeeded、Failed。 - 成功率。 -
平均等待时间。 - 平均执行时间。 - Runner 在线数。 - 租约超时次数。 -
最近失败任务。

成本数据如不完整，显示“未提供/估算”，不能伪造精确费用。

## 8. 验收标准

- [ ] 可以从 Event 找到关联 Task。
- [ ] 可以从 Task 查看所有 Attempts。
- [ ] 可以查看每次 Attempt 的日志。
- [ ] 可以查看和授权下载 Artifact。
- [ ] 日志脱敏测试通过。
- [ ] SignalR 断线后可通过 REST 恢复状态。
- [ ] Dashboard 统计与数据库结果一致。
- [ ] 大日志分页读取，不一次性加载全部内容。
- [ ] Runner 状态和心跳时间正确。
- [ ] 前端 lint/build、后端 build/test 全部通过。

## 9. 交付

创建
`docs/reports/AOS-008-DELIVERY-REPORT.md`，附页面清单、日志示例、产物存储策略和端到端测试结果。
