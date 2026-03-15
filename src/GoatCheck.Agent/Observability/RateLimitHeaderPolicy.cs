using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Globalization;

namespace GoatCheck.Agent.Observability;

/// <summary>
/// Pipeline policy that reads rate-limit and Retry-After headers from each response
/// and records them on the active OTel span. Viable only because AzureOpenAIClient is a singleton.
/// </summary>
public class RateLimitHeaderPolicy : PipelinePolicy
{
    public static readonly AsyncLocal<int?> LastRetryAfterSeconds = new();

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
        this.RecordRateLimitHeaders(message.Response);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex);
        this.RecordRateLimitHeaders(message.Response);
    }

    private void RecordRateLimitHeaders(PipelineResponse? response)
    {
        if (response is null) return;
        var activity = Activity.Current;

        LastRetryAfterSeconds.Value = null;

        if (response.Headers.TryGetValue("x-ratelimit-remaining-tokens", out var tokens))
            activity?.SetTag("ratelimit.remaining_tokens", tokens);
        if (response.Headers.TryGetValue("x-ratelimit-remaining-requests", out var requests))
            activity?.SetTag("ratelimit.remaining_requests", requests);
        if (response.Headers.TryGetValue("x-ratelimit-limit-tokens", out var limitTokens))
            activity?.SetTag("ratelimit.limit_tokens", limitTokens);
        if (response.Headers.TryGetValue("x-ratelimit-limit-requests", out var limitRequests))
            activity?.SetTag("ratelimit.limit_requests", limitRequests);
        if (response.Headers.TryGetValue("x-ratelimit-reset-tokens", out var resetTokens))
            activity?.SetTag("ratelimit.reset_tokens", resetTokens);
        if (response.Headers.TryGetValue("x-ratelimit-reset-requests", out var resetRequests))
            activity?.SetTag("ratelimit.reset_requests", resetRequests);

        // Azure OpenAI sends retry-after-ms (ms) or x-ms-retry-after-ms rather than Retry-After (seconds)
        string? retryAfterRaw = null;
        int? retryAfterSeconds = null;

        if (response.Headers.TryGetValue("retry-after-ms", out var retryAfterMsRaw))
        {
            retryAfterRaw = retryAfterMsRaw;
            retryAfterSeconds = ParseRetryAfterMs(retryAfterMsRaw);
        }
        else if (response.Headers.TryGetValue("x-ms-retry-after-ms", out var xMsRetryAfterMsRaw))
        {
            retryAfterRaw = xMsRetryAfterMsRaw;
            retryAfterSeconds = ParseRetryAfterMs(xMsRetryAfterMsRaw);
        }
        else if (response.Headers.TryGetValue("Retry-After", out var retryAfterStandardRaw))
        {
            retryAfterRaw = retryAfterStandardRaw;
            retryAfterSeconds = ParseRetryAfter(retryAfterStandardRaw);
        }

        if (retryAfterSeconds.HasValue)
        {
            LastRetryAfterSeconds.Value = retryAfterSeconds.Value;
            activity?.SetTag("ratelimit.retry_after_seconds", retryAfterSeconds.Value);
        }

        Capture429HeaderSnapshot(response, activity, retryAfterRaw, requests, limitRequests, resetRequests);
    }

    private static void Capture429HeaderSnapshot(
        PipelineResponse response,
        Activity? activity,
        string? retryAfterRaw,
        string? remainingRequests,
        string? limitRequests,
        string? resetRequests)
    {
        if (response.Status != 429)
            return;

        var headerNames = string.Join(",", response.Headers.Select(header => header.Key).OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

        activity?.SetTag("ratelimit.status_code", response.Status);
        activity?.SetTag("ratelimit.header_names", headerNames);
        activity?.SetTag("ratelimit.header_count", response.Headers.Count());
        activity?.SetTag("ratelimit.retry_after_raw", retryAfterRaw);
        activity?.SetTag("ratelimit.remaining_requests_raw", remainingRequests);
        activity?.SetTag("ratelimit.limit_requests_raw", limitRequests);
        activity?.SetTag("ratelimit.reset_requests_raw", resetRequests);
    }

    internal static int? ParseRetryAfterMs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
            return (int)Math.Ceiling(ms / 1000.0);
        return null;
    }

    internal static int? ParseRetryAfter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return seconds;

        if (DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            var delta = (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds);
            return Math.Max(delta, 0);
        }

        return null;
    }
}
