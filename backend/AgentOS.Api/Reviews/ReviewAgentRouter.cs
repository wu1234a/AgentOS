using AgentOS.Application.Reviews;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Reviews;

public sealed class ReviewAgentRouter(
    CodexReviewAgent codex,
    ApiReviewAgent api,
    IOptions<ReviewOptions> options) : IReviewAgent
{
    public Task<ReviewAgentResult> ReviewAsync(ReviewAgentRequest request, CancellationToken cancellationToken)
        => string.Equals(options.Value.Provider, "codex-cli", StringComparison.OrdinalIgnoreCase)
            ? codex.ReviewAsync(request, cancellationToken)
            : api.ReviewAsync(request, cancellationToken);
}
