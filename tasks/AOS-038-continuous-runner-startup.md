# AOS-038：连续执行基础与开机自启

状态：Completed
优先级：P1
依赖：AOS-037 自动合并、Runner、Windows 任务计划

## 1. 范围

- Runner 在创建自动 worktree 前，先对干净的 base 工作区执行 `git pull --ff-only`。
- 自动合并后由 Watchdog 定期 fast-forward 3dERP main。
- Watchdog 同时保证 API、Runner、cloudflared 隧道在线。
- 新增登录时启动任务 `AgentOS Startup`。
- Runner 继续通过现有队列自动领取下一个任务。

## 2. 不在范围

- 自动生成 backlog/需求。
- 多 Runner 调度。
- 合并冲突自动 rebase。
- 任务失败后的自动修复生成。

## 3. 验收

- [x] worktree 创建前可同步 base main。
- [x] Watchdog 能启动 API 和 Runner。
- [x] Watchdog 能 fast-forward 3dERP main。
- [x] 登录启动任务已注册。
- [x] Runner 保持轮询并领取队列任务。
- [x] 构建和全量测试通过。
