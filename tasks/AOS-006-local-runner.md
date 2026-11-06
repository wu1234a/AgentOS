# AOS-006：Local Agent Runner

状态：Completed  
优先级：P0  
依赖：AOS-005

## 1. 目标

开发一个运行在本地 Windows/WSL2 环境的常驻 AgentOS.Runner。Runner
主动连接 AgentOS API，注册自身、领取任务、发送心跳、执行受控 Handler
并回传状态。

AOS-006 先实现通用任务执行接口和 Demo Handler，不在本任务中完成 Codex
进程调用；Codex Adapter 在 AOS-007 实现。

## 2. Runner 能力

- Worker 注册（通过一次性注册凭证或管理员预配置的 Runner Client
  完成绑定）。
- Runner 使用独立 API Client 凭证，凭证只具有 Runner 所需 Scope。
- Worker 心跳。
- 任务轮询/领取。
- 任务开始通知。
- 任务进度和日志回传。
- 任务完成/失败回传。
- 任务取消。
- 断线重连。
- 本地配置加载。
- 项目和工作区白名单验证。
- 优雅关闭。

## 3. 配置

提供 `appsettings.example.json` 或环境变量示例： - AgentOS Base URL -
Worker Code - Display Name - Platform - Workspace Roots - Poll
Interval - Heartbeat Interval - Max Concurrent Tasks - Runner Version -
Authentication Secret Reference

不得把真实 Runner Secret 提交到 Git。

## 4. 网络模型

- Runner 只发起出站 HTTPS 请求。
- 不开放本地公网端口。
- API 不应主动连接 Runner。
- 使用指数退避处理连接错误。
- API 暂时不可用时，Runner 不应丢失本地执行状态；应安全停止领取新任务。
- 任务执行中的网络中断要考虑本地日志缓冲和租约失效处理。

## 5. 执行模型

建立 `ITaskHandler` 或等价抽象： - 输入：任务
DTO、工作区上下文、取消令牌。 - 输出：标准化结果、退出状态、产物清单。 -
第一版 Demo Handler 只做无副作用的测试任务。 - Handler
不得自行提升权限或访问未授权目录。

## 6. 工作区验证

- 项目必须在 Runner 注册的允许项目列表中。
- 解析并规范化路径。
- 确认路径位于 AllowedWorkspaceRoot 内。
- 拒绝路径穿越和符号链接逃逸。
- 对写任务要求独立工作区。
- 清理时检查未提交变更。

## 7. 安装和运行

提供： - 本地控制台运行方式。 - Windows
Service/后台服务运行说明（如实现）。 - WSL2 运行说明。 - 配置示例。 -
停止和升级说明。

不要求 AOS-006 结束时配置开机自启，但 Runner
必须能够稳定运行并可优雅关闭。

## 8. 验收标准

- [ ] Runner 可成功注册。
- [ ] API 能看到 Runner 在线状态。
- [ ] Runner 能领取测试任务。
- [ ] Runner 定时发送心跳。
- [ ] Runner 能回传日志和完成结果。
- [ ] Runner 断网后可重连。
- [ ] API 停机时 Runner 不会错误标记任务成功。
- [ ] Runner 只能访问白名单项目路径。
- [ ] 任务取消可以传递给 Handler。
- [ ] 多任务并发受 Max Concurrent Tasks 限制。

## 9. 交付

创建
`docs/reports/AOS-006-DELIVERY-REPORT.md`，提供安装、配置、启动、停止和故障排查说明。
