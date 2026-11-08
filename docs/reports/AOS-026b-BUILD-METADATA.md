# AOS-026b：CI 构建元数据注入

状态：Completed

## 变更

- GitHub Actions 在构建前写入 `backend/AgentOS.Api/build-info.json`：
  - `commit` = `GITHUB_SHA`
  - `buildTimeUtc` = UTC 时间
- `AgentOS.Api.csproj` 将 `build-info.json` 复制到输出目录（存在时）。
- `BuildMetadata` 优先读取 `build-info.json`，回退到 `GITHUB_SHA`/`AGENTOS_GIT_COMMIT` 与 `AGENTOS_BUILD_TIME`/文件时间。
- 本地无文件时行为不变。

## 验证

- 后端构建：✅ 0 错误
- 后端全量测试：✅ **330 通过 / 5 跳过**
- CI：推送后确认

## 已知限制

1. 文件只包含 commit/buildTime，不包含版本号（版本仍取程序集）。
2. 仅 GitHub Actions 写入；其他 CI 需自行生成。
