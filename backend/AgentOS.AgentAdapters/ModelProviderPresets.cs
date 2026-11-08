namespace AgentOS.AgentAdapters;

public sealed record ModelProviderPreset(
    string Code,
    string Kind,
    string BaseUrl,
    string? DefaultModel,
    string SecretEnvironmentVariable);

public static class ModelProviderPresets
{
    public static readonly IReadOnlyDictionary<string, ModelProviderPreset> All =
        new Dictionary<string, ModelProviderPreset>(StringComparer.OrdinalIgnoreCase)
        {
            ["kimi"] = new("kimi", "openai-compatible", "https://api.moonshot.cn/v1", "moonshot-v1-8k", "KIMI_API_KEY"),
            ["deepseek"] = new("deepseek", "openai-compatible", "https://api.deepseek.com/v1", "deepseek-chat", "DEEPSEEK_API_KEY"),
            ["glm"] = new("glm", "openai-compatible", "https://open.bigmodel.cn/api/paas/v4", "glm-4", "ZHIPU_API_KEY"),
            ["qwen"] = new("qwen", "openai-compatible", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", "DASHSCOPE_API_KEY"),
            ["doubao"] = new("doubao", "openai-compatible", "https://ark.cn-beijing.volces.com/api/v3", null, "ARK_API_KEY"),
            ["hunyuan"] = new("hunyuan", "openai-compatible", "https://api.hunyuan.cloud.tencent.com/v1", null, "HUNYUAN_API_KEY"),
            ["grok"] = new("grok", "openai-compatible", "https://api.x.ai/v1", "grok-2-latest", "XAI_API_KEY"),
            ["openai"] = new("openai", "openai-compatible", "https://api.openai.com/v1", "gpt-4o", "OPENAI_API_KEY"),
            ["openai-compatible"] = new("openai-compatible", "openai-compatible", "", null, "OPENAI_API_KEY"),
            ["anthropic"] = new("anthropic", "anthropic", "https://api.anthropic.com", "claude-sonnet-4-20250514", "ANTHROPIC_API_KEY"),
            ["codex-cli"] = new("codex-cli", "cli", "", null, "")
        };
}
