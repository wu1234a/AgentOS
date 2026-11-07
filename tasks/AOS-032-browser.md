# AOS-032：浏览器自动化适配器（第一阶段）

状态：Completed
优先级：P1
依赖：现有 API/开发 Key
来源：Kimi Planner 草案 `01a1b7cd-0820-77db-86b5-47dd67ed3d1b`（已批准）

## 1. 范围

- Playwright 适配器：navigate/extracttext、screenshot、click。
- 配置 `BrowserAutomation`：Enabled（默认 false）、Headless、TimeoutSeconds、AllowedHosts（空=拒绝）、ScreenshotDirectory。
- API `POST /api/v1/browser/execute`，开发 Key 认证。
- 仅允许 http/https，主机白名单精确匹配；默认关闭。
- 文本截断 20k；截图写本地目录。

## 2. 不在范围

- 录制回放、多浏览器、凭据/登录管理、下载/上传。
- 前端、队列化浏览器任务、资源池。
- CI 启动真实浏览器。

## 3. 安全

- 默认关闭；空白名单拒绝。
- 仅 http/https；主机精确匹配，降低 SSRF。
- 无凭据存储；不记录页面内容。
- Playwright 包仅编译，CI 不下载浏览器。

## 4. 验收

- [x] 禁用返回配置错误。
- [x] 白名单外拒绝。
- [x] 后端全量测试、CI 通过。
