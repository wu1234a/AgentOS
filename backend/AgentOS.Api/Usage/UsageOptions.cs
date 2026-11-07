namespace AgentOS.Api.Usage;

public sealed class UsagePricingOptions
{
    public decimal InputPerMillionTokens { get; set; }
    public decimal OutputPerMillionTokens { get; set; }
    public string Currency { get; set; } = "USD";
    public bool Enabled { get; set; } = true;
}

public sealed class UsageOptions
{
    public Dictionary<string, UsagePricingOptions> Pricing { get; set; } = [];
}
