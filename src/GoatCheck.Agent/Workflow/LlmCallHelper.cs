using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Observability;
using GoatCheck.Agent.Options;

namespace GoatCheck.Agent.Workflow;

internal record ScorerJsonResponse(
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("pros")] string[] Pros,
    [property: JsonPropertyName("cons")] string[] Cons,
    [property: JsonPropertyName("successStrategies")] string[] SuccessStrategies);

internal record NarrativeJsonResponse(
    [property: JsonPropertyName("pros")] string[] Pros,
    [property: JsonPropertyName("cons")] string[] Cons,
    [property: JsonPropertyName("successStrategies")] string[] SuccessStrategies,
    [property: JsonPropertyName("contradictions")] string[] Contradictions);

internal record VoiceJsonResponse(
    [property: JsonPropertyName("hotTakePros")] string[] HotTakePros,
    [property: JsonPropertyName("hotTakeCons")] string[] HotTakeCons,
    [property: JsonPropertyName("hotTakeSuccessFactors")] string[] HotTakeSuccessFactors,
    [property: JsonPropertyName("bestForSummary")] string BestForSummary,
    [property: JsonPropertyName("watchOutForSummary")] string WatchOutForSummary);

internal readonly record struct RetryDelayDecision(
    double DelayMs,
    string Source,
    int? RetryAfterSeconds);

internal static class LlmCallHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<ScorerRunResult> ExecuteScorerAsync(
        IChatClient client,
        string systemPrompt,
        string userPrompt,
        EvaluationDimension dimension,
        CandidateRef candidate,
        string deploymentName,
        ResilienceOptions resilience,
        ObservabilityOptions observability,
        GoatCheckMetrics metrics,
        ILogger logger,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var totalAttempts = 0;
        var lastFailureCode = FailureCode.Unknown;
        string? lastFailureMessage = null;

        var agentName = $"{dimension}_scorer";
        using var scorerActivity = GoatCheckActivitySource.Source.StartActivity($"invoke_agent {agentName}");
        scorerActivity?.SetTag("gen_ai.operation.name", "invoke_agent");
        scorerActivity?.SetTag("gen_ai.provider.name", "azure.ai.openai");
        scorerActivity?.SetTag("gen_ai.agent.name", agentName);
        scorerActivity?.SetTag("gen_ai.request.model", deploymentName);
        scorerActivity?.SetTag("gen_ai.output.type", "json");
        scorerActivity?.SetTag("candidate.id", candidate.CandidateId);
        scorerActivity?.SetTag("candidate.name", candidate.DisplayName);
        scorerActivity?.SetTag("agent.name", agentName);
        scorerActivity?.SetTag("dimension", dimension.ToString());
        scorerActivity?.SetTag("model.deployment", deploymentName);
        scorerActivity?.SetTag("timeout.ms", resilience.NetworkTimeoutSeconds * 1000);
        scorerActivity?.SetTag("payload.capture.mode", observability.PayloadCaptureMode);

        if (observability.EmitPromptHashes)
        {
            scorerActivity?.SetTag("prompt.hash", ComputeHash(systemPrompt + userPrompt));
        }

        long totalInputTokens = 0, totalOutputTokens = 0;

        for (int attempt = 1; attempt <= resilience.MaxRetryAttempts + 1; attempt++)
        {
            totalAttempts++;
            using var attemptCts = ct.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : new CancellationTokenSource();
            attemptCts.CancelAfter(TimeSpan.FromSeconds(resilience.NetworkTimeoutSeconds));

            using var attemptActivity = GoatCheckActivitySource.Source.StartActivity("llm.call.attempt");
            attemptActivity?.SetTag("candidate.id", candidate.CandidateId);
            attemptActivity?.SetTag("candidate.name", candidate.DisplayName);
            attemptActivity?.SetTag("agent.name", $"{dimension}_scorer");
            attemptActivity?.SetTag("dimension", dimension.ToString());
            attemptActivity?.SetTag("model.deployment", deploymentName);
            attemptActivity?.SetTag("attempt.number", attempt);
            attemptActivity?.SetTag("attempt.max", resilience.MaxRetryAttempts + 1);
            attemptActivity?.SetTag("timeout.ms", resilience.NetworkTimeoutSeconds * 1000);
            attemptActivity?.SetTag("payload.capture.mode", observability.PayloadCaptureMode);

            var attemptSw = Stopwatch.StartNew();
            metrics.LlmInflight.Add(1);
            metrics.LlmAttemptsTotal.Add(1, new TagList { { "agent", $"{dimension}_scorer" }, { "dimension", dimension.ToString() }, { "outcome", "attempt" } });

            try
            {
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, systemPrompt),
                    new(ChatRole.User, userPrompt)
                };

                var response = await client.GetResponseAsync(messages, null, attemptCts.Token);
                totalInputTokens += response.Usage?.InputTokenCount ?? 0;
                totalOutputTokens += response.Usage?.OutputTokenCount ?? 0;
                var content = response.Text ?? "";

                var successRetryAfter = RateLimitHeaderPolicy.LastRetryAfterSeconds.Value;
                if (successRetryAfter.HasValue)
                    attemptActivity?.SetTag("retry_after.seconds", successRetryAfter.Value);

                if (observability.EmitPromptHashes)
                {
                    attemptActivity?.SetTag("response.hash", ComputeHash(content));
                    scorerActivity?.SetTag("response.hash", ComputeHash(content));
                }

                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmLatencyMs.Record(attemptSw.ElapsedMilliseconds);

                var parsed = TryParseScorer(content, dimension);
                if (parsed is null)
                {
                    lastFailureCode = FailureCode.Parse;
                    lastFailureMessage = $"Failed to parse JSON response: {content[..Math.Min(200, content.Length)]}";
                    attemptActivity?.SetTag("result.status", "parse_error");
                    attemptActivity?.SetTag("error.type", "ParseException");
                    metrics.LlmParseErrorsTotal.Add(1);
                    logger.LogWarning(
                        "LlmCallAttempt ParseError TraceId={TraceId} CandidateId={CandidateId} Dimension={Dimension} Attempt={Attempt} ElapsedMs={ElapsedMs}",
                        Activity.Current?.TraceId.ToString(), candidate.CandidateId, dimension, attempt, attemptSw.ElapsedMilliseconds);
                    scorerActivity?.SetTag("result.status", "parse_error");
                    scorerActivity?.SetTag("error.type", "ParseException");
                    scorerActivity?.SetTag("degraded", true);
                    return new ScorerRunResult(false, null, FailureCode.Parse, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
                }

                attemptActivity?.SetTag("result.status", "ok");
                scorerActivity?.SetTag("result.status", "ok");
                scorerActivity?.SetTag("degraded", false);
                scorerActivity?.SetTag("gen_ai.usage.input_tokens", totalInputTokens);
                scorerActivity?.SetTag("gen_ai.usage.output_tokens", totalOutputTokens);

                logger.LogInformation(
                    "LlmCallAttempt Success TraceId={TraceId} CandidateId={CandidateId} Dimension={Dimension} Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=success",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, dimension, attempt, attemptSw.ElapsedMilliseconds);

                return new ScorerRunResult(true, parsed, FailureCode.Unknown, null, totalAttempts, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmTimeoutsTotal.Add(1);
                lastFailureCode = FailureCode.Timeout;
                lastFailureMessage = $"Timeout after {resilience.NetworkTimeoutSeconds}s on attempt {attempt}";
                attemptActivity?.SetTag("result.status", "timeout");
                attemptActivity?.SetTag("error.type", "System.OperationCanceledException");

                logger.LogWarning(
                    "LlmCallAttempt Timeout TraceId={TraceId} CandidateId={CandidateId} Dimension={Dimension} Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=timeout",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, dimension, attempt, attemptSw.ElapsedMilliseconds);

                if (!resilience.RetryOnTimeout || attempt > resilience.MaxRetryAttempts)
                {
                    scorerActivity?.SetTag("result.status", "timeout");
                    scorerActivity?.SetTag("error.type", "System.OperationCanceledException");
                    scorerActivity?.SetTag("degraded", true);
                    return new ScorerRunResult(false, null, FailureCode.Timeout, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex) when ((ex is Azure.RequestFailedException rfe && rfe.Status == 429) ||
                                       (ex is System.ClientModel.ClientResultException cre && cre.Status == 429))
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmRetriesTotal.Add(1);
                lastFailureCode = FailureCode.RateLimit;
                lastFailureMessage = $"Rate limited (429) on attempt {attempt}: {ex.Message}";
                attemptActivity?.SetTag("result.status", "rate_limited");
                attemptActivity?.SetTag("error.type", "Azure.RequestFailedException");

                var retryAfter = RateLimitHeaderPolicy.LastRetryAfterSeconds.Value;
                if (retryAfter.HasValue)
                {
                    attemptActivity?.SetTag("retry_after.source", "server");
                    attemptActivity?.SetTag("retry_after.seconds", retryAfter.Value);
                }

                logger.LogWarning(
                    "LlmCallAttempt RateLimit TraceId={TraceId} CandidateId={CandidateId} Dimension={Dimension} Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=rate_limited RetryAfterSeconds={RetryAfterSeconds}",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, dimension, attempt, attemptSw.ElapsedMilliseconds, retryAfter);

                if (!resilience.RetryOn429 || attempt > resilience.MaxRetryAttempts)
                {
                    scorerActivity?.SetTag("result.status", "rate_limited");
                    scorerActivity?.SetTag("error.type", "Azure.RequestFailedException");
                    scorerActivity?.SetTag("degraded", true);
                    return new ScorerRunResult(false, null, FailureCode.RateLimit, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status >= 500)
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.Llm5xxTotal.Add(1);
                lastFailureCode = FailureCode.Transport;
                lastFailureMessage = $"Server error ({ex.Status}) on attempt {attempt}: {ex.Message}";
                attemptActivity?.SetTag("result.status", "transport_error");
                attemptActivity?.SetTag("error.type", "Azure.RequestFailedException");

                logger.LogWarning(
                    "LlmCallAttempt TransportError TraceId={TraceId} CandidateId={CandidateId} Dimension={Dimension} Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=transport_error",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, dimension, attempt, attemptSw.ElapsedMilliseconds);

                if (!resilience.RetryOn5xx || attempt > resilience.MaxRetryAttempts)
                {
                    scorerActivity?.SetTag("result.status", "transport_error");
                    scorerActivity?.SetTag("error.type", "Azure.RequestFailedException");
                    scorerActivity?.SetTag("degraded", true);
                    return new ScorerRunResult(false, null, FailureCode.Transport, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmErrorsTotal.Add(1);
                lastFailureCode = FailureCode.Unknown;
                lastFailureMessage = $"Unknown error on attempt {attempt}: {ex.Message}";
                attemptActivity?.SetTag("result.status", "transport_error");
                attemptActivity?.SetTag("error.type", ex.GetType().FullName ?? ex.GetType().Name);

                logger.LogError(ex,
                    "LlmCallAttempt UnknownError TraceId={TraceId} CandidateId={CandidateId} Dimension={Dimension} Attempt={Attempt} ElapsedMs={ElapsedMs}",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, dimension, attempt, attemptSw.ElapsedMilliseconds);

                scorerActivity?.SetTag("result.status", "transport_error");
                scorerActivity?.SetTag("error.type", ex.GetType().FullName ?? ex.GetType().Name);
                scorerActivity?.SetTag("degraded", true);
                return new ScorerRunResult(false, null, FailureCode.Unknown, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
            }

            if (attempt <= resilience.MaxRetryAttempts)
            {
                await DelayBeforeRetryAsync(resilience, attempt, attemptActivity, logger, candidate, dimension.ToString(), ct);
            }
        }

        scorerActivity?.SetTag("result.status", lastFailureCode.ToString().ToLowerInvariant());
        scorerActivity?.SetTag("degraded", true);
        return new ScorerRunResult(false, null, lastFailureCode, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
    }

    public static async Task<AgentCallRunResult<NarrativeJsonResponse>> ExecuteNarrativeAsync(
        IChatClient client,
        string systemPrompt,
        string userPrompt,
        CandidateRef candidate,
        string deploymentName,
        ResilienceOptions resilience,
        ObservabilityOptions observability,
        GoatCheckMetrics metrics,
        ILogger logger,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var totalAttempts = 0;
        var lastFailureCode = FailureCode.Unknown;
        string? lastFailureMessage = null;

        using var activity = GoatCheckActivitySource.Source.StartActivity("invoke_agent per_candidate_narrative_rollup");
        activity?.SetTag("gen_ai.operation.name", "invoke_agent");
        activity?.SetTag("gen_ai.provider.name", "azure.ai.openai");
        activity?.SetTag("gen_ai.agent.name", "per_candidate_narrative_rollup");
        activity?.SetTag("gen_ai.request.model", deploymentName);
        activity?.SetTag("gen_ai.output.type", "json");
        activity?.SetTag("candidate.id", candidate.CandidateId);
        activity?.SetTag("candidate.name", candidate.DisplayName);
        activity?.SetTag("agent.name", "per_candidate_narrative_rollup");
        activity?.SetTag("dimension", "narrative");
        activity?.SetTag("model.deployment", deploymentName);
        activity?.SetTag("timeout.ms", resilience.NetworkTimeoutSeconds * 1000);
        activity?.SetTag("payload.capture.mode", observability.PayloadCaptureMode);

        if (observability.EmitPromptHashes)
            activity?.SetTag("prompt.hash", ComputeHash(systemPrompt + userPrompt));

        long totalInputTokens = 0, totalOutputTokens = 0;

        for (int attempt = 1; attempt <= resilience.MaxRetryAttempts + 1; attempt++)
        {
            totalAttempts++;
            using var attemptCts = ct.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : new CancellationTokenSource();
            attemptCts.CancelAfter(TimeSpan.FromSeconds(resilience.NetworkTimeoutSeconds));

            using var attemptActivity = GoatCheckActivitySource.Source.StartActivity("llm.call.attempt");
            attemptActivity?.SetTag("candidate.id", candidate.CandidateId);
            attemptActivity?.SetTag("candidate.name", candidate.DisplayName);
            attemptActivity?.SetTag("agent.name", "per_candidate_narrative_rollup");
            attemptActivity?.SetTag("dimension", "narrative");
            attemptActivity?.SetTag("model.deployment", deploymentName);
            attemptActivity?.SetTag("attempt.number", attempt);
            attemptActivity?.SetTag("attempt.max", resilience.MaxRetryAttempts + 1);
            attemptActivity?.SetTag("timeout.ms", resilience.NetworkTimeoutSeconds * 1000);
            attemptActivity?.SetTag("payload.capture.mode", observability.PayloadCaptureMode);

            var attemptSw = Stopwatch.StartNew();
            metrics.LlmInflight.Add(1);
            metrics.LlmAttemptsTotal.Add(1, new TagList { { "agent", "narrative_rollup" }, { "dimension", "narrative" }, { "outcome", "attempt" } });

            try
            {
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, systemPrompt),
                    new(ChatRole.User, userPrompt)
                };

                var response = await client.GetResponseAsync(messages, null, attemptCts.Token);
                totalInputTokens += response.Usage?.InputTokenCount ?? 0;
                totalOutputTokens += response.Usage?.OutputTokenCount ?? 0;
                var content = response.Text ?? "";

                var successRetryAfter = RateLimitHeaderPolicy.LastRetryAfterSeconds.Value;
                if (successRetryAfter.HasValue)
                    attemptActivity?.SetTag("retry_after.seconds", successRetryAfter.Value);

                if (observability.EmitPromptHashes)
                {
                    attemptActivity?.SetTag("response.hash", ComputeHash(content));
                    activity?.SetTag("response.hash", ComputeHash(content));
                }

                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmLatencyMs.Record(attemptSw.ElapsedMilliseconds);

                var result = TryParseJson<NarrativeJsonResponse>(content);
                if (result is not null)
                {
                    attemptActivity?.SetTag("result.status", "ok");
                    activity?.SetTag("result.status", "ok");
                    activity?.SetTag("degraded", false);
                    activity?.SetTag("gen_ai.usage.input_tokens", totalInputTokens);
                    activity?.SetTag("gen_ai.usage.output_tokens", totalOutputTokens);

                    logger.LogInformation(
                        "LlmCallAttempt Success TraceId={TraceId} CandidateId={CandidateId} Dimension=narrative Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=success",
                        Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds);

                    return new AgentCallRunResult<NarrativeJsonResponse>(true, result, FailureCode.Unknown, null, totalAttempts, sw.ElapsedMilliseconds);
                }

                lastFailureCode = FailureCode.Parse;
                lastFailureMessage = $"Failed to parse narrative JSON: {content[..Math.Min(200, content.Length)]}";
                attemptActivity?.SetTag("result.status", "parse_error");
                attemptActivity?.SetTag("error.type", "ParseException");
                metrics.LlmParseErrorsTotal.Add(1);
                logger.LogWarning(
                    "LlmCallAttempt ParseError TraceId={TraceId} CandidateId={CandidateId} Dimension=narrative Attempt={Attempt} ElapsedMs={ElapsedMs}",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds);
                activity?.SetTag("result.status", "parse_error");
                activity?.SetTag("error.type", "ParseException");
                activity?.SetTag("degraded", true);
                return new AgentCallRunResult<NarrativeJsonResponse>(false, null, FailureCode.Parse, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmTimeoutsTotal.Add(1);
                lastFailureCode = FailureCode.Timeout;
                lastFailureMessage = $"Timeout after {resilience.NetworkTimeoutSeconds}s on attempt {attempt}";
                attemptActivity?.SetTag("result.status", "timeout");
                attemptActivity?.SetTag("error.type", "System.OperationCanceledException");
                logger.LogWarning(
                    "LlmCallAttempt Timeout TraceId={TraceId} CandidateId={CandidateId} Dimension=narrative Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=timeout",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds);
                if (!resilience.RetryOnTimeout || attempt > resilience.MaxRetryAttempts)
                {
                    activity?.SetTag("result.status", "timeout");
                    activity?.SetTag("error.type", "System.OperationCanceledException");
                    activity?.SetTag("degraded", true);
                    return new AgentCallRunResult<NarrativeJsonResponse>(false, null, FailureCode.Timeout, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex) when ((ex is Azure.RequestFailedException rfe && rfe.Status == 429) ||
                                       (ex is System.ClientModel.ClientResultException cre && cre.Status == 429))
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmRetriesTotal.Add(1);
                lastFailureCode = FailureCode.RateLimit;
                lastFailureMessage = $"Rate limited (429) on attempt {attempt}: {ex.Message}";
                attemptActivity?.SetTag("result.status", "rate_limited");
                attemptActivity?.SetTag("error.type", "Azure.RequestFailedException");
                var retryAfter = RateLimitHeaderPolicy.LastRetryAfterSeconds.Value;
                if (retryAfter.HasValue)
                {
                    attemptActivity?.SetTag("retry_after.source", "server");
                    attemptActivity?.SetTag("retry_after.seconds", retryAfter.Value);
                }
                logger.LogWarning(
                    "LlmCallAttempt RateLimit TraceId={TraceId} CandidateId={CandidateId} Dimension=narrative Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=rate_limited RetryAfterSeconds={RetryAfterSeconds}",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds, retryAfter);
                if (!resilience.RetryOn429 || attempt > resilience.MaxRetryAttempts)
                {
                    activity?.SetTag("result.status", "rate_limited");
                    activity?.SetTag("error.type", "Azure.RequestFailedException");
                    activity?.SetTag("degraded", true);
                    return new AgentCallRunResult<NarrativeJsonResponse>(false, null, FailureCode.RateLimit, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status >= 500)
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.Llm5xxTotal.Add(1);
                lastFailureCode = FailureCode.Transport;
                lastFailureMessage = $"Server error ({ex.Status}) on attempt {attempt}: {ex.Message}";
                attemptActivity?.SetTag("result.status", "transport_error");
                attemptActivity?.SetTag("error.type", "Azure.RequestFailedException");
                logger.LogWarning(
                    "LlmCallAttempt TransportError TraceId={TraceId} CandidateId={CandidateId} Dimension=narrative Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=transport_error",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds);
                if (!resilience.RetryOn5xx || attempt > resilience.MaxRetryAttempts)
                {
                    activity?.SetTag("result.status", "transport_error");
                    activity?.SetTag("error.type", "Azure.RequestFailedException");
                    activity?.SetTag("degraded", true);
                    return new AgentCallRunResult<NarrativeJsonResponse>(false, null, FailureCode.Transport, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmErrorsTotal.Add(1);
                lastFailureCode = FailureCode.Unknown;
                lastFailureMessage = $"Unknown error on attempt {attempt}: {ex.Message}";
                attemptActivity?.SetTag("result.status", "transport_error");
                attemptActivity?.SetTag("error.type", ex.GetType().FullName ?? ex.GetType().Name);
                logger.LogError(ex,
                    "LlmCallAttempt UnknownError TraceId={TraceId} CandidateId={CandidateId} Dimension=narrative Attempt={Attempt} ElapsedMs={ElapsedMs}",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds);
                activity?.SetTag("result.status", "transport_error");
                activity?.SetTag("error.type", ex.GetType().FullName ?? ex.GetType().Name);
                activity?.SetTag("degraded", true);
                return new AgentCallRunResult<NarrativeJsonResponse>(false, null, FailureCode.Unknown, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
            }

            if (attempt <= resilience.MaxRetryAttempts)
                await DelayBeforeRetryAsync(resilience, attempt, attemptActivity, logger, candidate, "narrative", ct);
        }

        activity?.SetTag("result.status", lastFailureCode.ToString().ToLowerInvariant());
        activity?.SetTag("degraded", true);
        return new AgentCallRunResult<NarrativeJsonResponse>(false, null, lastFailureCode, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
    }

    public static async Task<AgentCallRunResult<VoiceJsonResponse>> ExecuteVoiceAsync(
        IChatClient client,
        string systemPrompt,
        string userPrompt,
        CandidateRef candidate,
        string deploymentName,
        ResilienceOptions resilience,
        ObservabilityOptions observability,
        GoatCheckMetrics metrics,
        ILogger logger,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var totalAttempts = 0;
        var lastFailureCode = FailureCode.Unknown;
        string? lastFailureMessage = null;

        using var activity = GoatCheckActivitySource.Source.StartActivity("invoke_agent hot_take_voice");
        activity?.SetTag("gen_ai.operation.name", "invoke_agent");
        activity?.SetTag("gen_ai.provider.name", "azure.ai.openai");
        activity?.SetTag("gen_ai.agent.name", "hot_take_voice");
        activity?.SetTag("gen_ai.request.model", deploymentName);
        activity?.SetTag("gen_ai.output.type", "json");
        activity?.SetTag("candidate.id", candidate.CandidateId);
        activity?.SetTag("candidate.name", candidate.DisplayName);
        activity?.SetTag("agent.name", "hot_take_voice");
        activity?.SetTag("dimension", "voice");
        activity?.SetTag("model.deployment", deploymentName);
        activity?.SetTag("timeout.ms", resilience.NetworkTimeoutSeconds * 1000);
        activity?.SetTag("payload.capture.mode", observability.PayloadCaptureMode);

        if (observability.EmitPromptHashes)
            activity?.SetTag("prompt.hash", ComputeHash(systemPrompt + userPrompt));

        long totalInputTokens = 0, totalOutputTokens = 0;

        for (int attempt = 1; attempt <= resilience.MaxRetryAttempts + 1; attempt++)
        {
            totalAttempts++;
            using var attemptCts = ct.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : new CancellationTokenSource();
            attemptCts.CancelAfter(TimeSpan.FromSeconds(resilience.NetworkTimeoutSeconds));

            using var attemptActivity = GoatCheckActivitySource.Source.StartActivity("llm.call.attempt");
            attemptActivity?.SetTag("candidate.id", candidate.CandidateId);
            attemptActivity?.SetTag("candidate.name", candidate.DisplayName);
            attemptActivity?.SetTag("agent.name", "hot_take_voice");
            attemptActivity?.SetTag("dimension", "voice");
            attemptActivity?.SetTag("model.deployment", deploymentName);
            attemptActivity?.SetTag("attempt.number", attempt);
            attemptActivity?.SetTag("attempt.max", resilience.MaxRetryAttempts + 1);
            attemptActivity?.SetTag("timeout.ms", resilience.NetworkTimeoutSeconds * 1000);
            attemptActivity?.SetTag("payload.capture.mode", observability.PayloadCaptureMode);

            var attemptSw = Stopwatch.StartNew();
            metrics.LlmInflight.Add(1);
            metrics.LlmAttemptsTotal.Add(1, new TagList { { "agent", "hot_take_voice" }, { "dimension", "voice" }, { "outcome", "attempt" } });

            try
            {
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, systemPrompt),
                    new(ChatRole.User, userPrompt)
                };

                var response = await client.GetResponseAsync(messages, null, attemptCts.Token);
                totalInputTokens += response.Usage?.InputTokenCount ?? 0;
                totalOutputTokens += response.Usage?.OutputTokenCount ?? 0;
                var content = response.Text ?? "";

                var successRetryAfter = RateLimitHeaderPolicy.LastRetryAfterSeconds.Value;
                if (successRetryAfter.HasValue)
                    attemptActivity?.SetTag("retry_after.seconds", successRetryAfter.Value);

                if (observability.EmitPromptHashes)
                {
                    attemptActivity?.SetTag("response.hash", ComputeHash(content));
                    activity?.SetTag("response.hash", ComputeHash(content));
                }

                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmLatencyMs.Record(attemptSw.ElapsedMilliseconds);

                var result = TryParseJson<VoiceJsonResponse>(content);
                if (result is not null)
                {
                    attemptActivity?.SetTag("result.status", "ok");
                    activity?.SetTag("result.status", "ok");
                    activity?.SetTag("degraded", false);
                    activity?.SetTag("gen_ai.usage.input_tokens", totalInputTokens);
                    activity?.SetTag("gen_ai.usage.output_tokens", totalOutputTokens);

                    logger.LogInformation(
                        "LlmCallAttempt Success TraceId={TraceId} CandidateId={CandidateId} Dimension=voice Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=success",
                        Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds);

                    return new AgentCallRunResult<VoiceJsonResponse>(true, result, FailureCode.Unknown, null, totalAttempts, sw.ElapsedMilliseconds);
                }

                lastFailureCode = FailureCode.Parse;
                lastFailureMessage = $"Failed to parse voice JSON: {content[..Math.Min(200, content.Length)]}";
                attemptActivity?.SetTag("result.status", "parse_error");
                attemptActivity?.SetTag("error.type", "ParseException");
                metrics.LlmParseErrorsTotal.Add(1);
                logger.LogWarning(
                    "LlmCallAttempt ParseError TraceId={TraceId} CandidateId={CandidateId} Dimension=voice Attempt={Attempt} ElapsedMs={ElapsedMs}",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds);
                activity?.SetTag("result.status", "parse_error");
                activity?.SetTag("error.type", "ParseException");
                activity?.SetTag("degraded", true);
                return new AgentCallRunResult<VoiceJsonResponse>(false, null, FailureCode.Parse, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmTimeoutsTotal.Add(1);
                lastFailureCode = FailureCode.Timeout;
                lastFailureMessage = $"Timeout after {resilience.NetworkTimeoutSeconds}s on attempt {attempt}";
                attemptActivity?.SetTag("result.status", "timeout");
                attemptActivity?.SetTag("error.type", "System.OperationCanceledException");
                logger.LogWarning(
                    "LlmCallAttempt Timeout TraceId={TraceId} CandidateId={CandidateId} Dimension=voice Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=timeout",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds);
                if (!resilience.RetryOnTimeout || attempt > resilience.MaxRetryAttempts)
                {
                    activity?.SetTag("result.status", "timeout");
                    activity?.SetTag("error.type", "System.OperationCanceledException");
                    activity?.SetTag("degraded", true);
                    return new AgentCallRunResult<VoiceJsonResponse>(false, null, FailureCode.Timeout, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex) when ((ex is Azure.RequestFailedException rfe && rfe.Status == 429) ||
                                       (ex is System.ClientModel.ClientResultException cre && cre.Status == 429))
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmRetriesTotal.Add(1);
                lastFailureCode = FailureCode.RateLimit;
                lastFailureMessage = $"Rate limited (429) on attempt {attempt}: {ex.Message}";
                attemptActivity?.SetTag("result.status", "rate_limited");
                attemptActivity?.SetTag("error.type", "Azure.RequestFailedException");
                var retryAfter = RateLimitHeaderPolicy.LastRetryAfterSeconds.Value;
                if (retryAfter.HasValue)
                {
                    attemptActivity?.SetTag("retry_after.source", "server");
                    attemptActivity?.SetTag("retry_after.seconds", retryAfter.Value);
                }
                logger.LogWarning(
                    "LlmCallAttempt RateLimit TraceId={TraceId} CandidateId={CandidateId} Dimension=voice Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=rate_limited RetryAfterSeconds={RetryAfterSeconds}",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds, retryAfter);
                if (!resilience.RetryOn429 || attempt > resilience.MaxRetryAttempts)
                {
                    activity?.SetTag("result.status", "rate_limited");
                    activity?.SetTag("error.type", "Azure.RequestFailedException");
                    activity?.SetTag("degraded", true);
                    return new AgentCallRunResult<VoiceJsonResponse>(false, null, FailureCode.RateLimit, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status >= 500)
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.Llm5xxTotal.Add(1);
                lastFailureCode = FailureCode.Transport;
                lastFailureMessage = $"Server error ({ex.Status}) on attempt {attempt}: {ex.Message}";
                attemptActivity?.SetTag("result.status", "transport_error");
                attemptActivity?.SetTag("error.type", "Azure.RequestFailedException");
                logger.LogWarning(
                    "LlmCallAttempt TransportError TraceId={TraceId} CandidateId={CandidateId} Dimension=voice Attempt={Attempt} ElapsedMs={ElapsedMs} Outcome=transport_error",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds);
                if (!resilience.RetryOn5xx || attempt > resilience.MaxRetryAttempts)
                {
                    activity?.SetTag("result.status", "transport_error");
                    activity?.SetTag("error.type", "Azure.RequestFailedException");
                    activity?.SetTag("degraded", true);
                    return new AgentCallRunResult<VoiceJsonResponse>(false, null, FailureCode.Transport, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                attemptSw.Stop();
                metrics.LlmInflight.Add(-1);
                metrics.LlmErrorsTotal.Add(1);
                lastFailureCode = FailureCode.Unknown;
                lastFailureMessage = $"Unknown error on attempt {attempt}: {ex.Message}";
                attemptActivity?.SetTag("result.status", "transport_error");
                attemptActivity?.SetTag("error.type", ex.GetType().FullName ?? ex.GetType().Name);
                logger.LogError(ex,
                    "LlmCallAttempt UnknownError TraceId={TraceId} CandidateId={CandidateId} Dimension=voice Attempt={Attempt} ElapsedMs={ElapsedMs}",
                    Activity.Current?.TraceId.ToString(), candidate.CandidateId, attempt, attemptSw.ElapsedMilliseconds);
                activity?.SetTag("result.status", "transport_error");
                activity?.SetTag("error.type", ex.GetType().FullName ?? ex.GetType().Name);
                activity?.SetTag("degraded", true);
                return new AgentCallRunResult<VoiceJsonResponse>(false, null, FailureCode.Unknown, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
            }

            if (attempt <= resilience.MaxRetryAttempts)
                await DelayBeforeRetryAsync(resilience, attempt, attemptActivity, logger, candidate, "voice", ct);
        }

        activity?.SetTag("result.status", lastFailureCode.ToString().ToLowerInvariant());
        activity?.SetTag("degraded", true);
        return new AgentCallRunResult<VoiceJsonResponse>(false, null, lastFailureCode, lastFailureMessage, totalAttempts, sw.ElapsedMilliseconds);
    }

    private static RetryDelayDecision ComputeRetryDelay(ResilienceOptions resilience, int attempt)
    {
        var delay = Math.Min(
            resilience.RetryBaseDelayMs * Math.Pow(2, attempt - 1),
            resilience.RetryMaxDelayMs);
        if (resilience.RetryJitter)
            delay *= 0.5 + Random.Shared.NextDouble() * 0.5;

        var serverRetryAfter = RateLimitHeaderPolicy.LastRetryAfterSeconds.Value;
        if (serverRetryAfter.HasValue)
        {
            var serverDelayMs = serverRetryAfter.Value * 1000.0;
            if (serverDelayMs > delay)
                return new RetryDelayDecision(serverDelayMs, "server_retry_after", serverRetryAfter.Value);
        }

        return new RetryDelayDecision(delay, "local_backoff", serverRetryAfter);
    }

    private static async Task DelayBeforeRetryAsync(
        ResilienceOptions resilience,
        int attempt,
        Activity? attemptActivity,
        ILogger logger,
        CandidateRef candidate,
        string dimension,
        CancellationToken ct)
    {
        var decision = ComputeRetryDelay(resilience, attempt);
        attemptActivity?.SetTag("retry_delay_ms", decision.DelayMs);
        attemptActivity?.SetTag("retry_delay_source", decision.Source);
        if (decision.RetryAfterSeconds.HasValue)
            attemptActivity?.SetTag("retry_after.seconds", decision.RetryAfterSeconds.Value);

        logger.LogInformation(
            "LlmRetryDelay TraceId={TraceId} CandidateId={CandidateId} Dimension={Dimension} Attempt={Attempt} DelayMs={DelayMs} RetryDelaySource={RetryDelaySource} RetryAfterSeconds={RetryAfterSeconds}",
            Activity.Current?.TraceId.ToString(), candidate.CandidateId, dimension, attempt,
            decision.DelayMs, decision.Source, decision.RetryAfterSeconds);

        await Task.Delay((int)decision.DelayMs, ct);
    }

    private static FieldEvaluation? TryParseScorer(string content, EvaluationDimension dimension)
    {
        var json = StripMarkdownFences(content);
        try
        {
            var r = JsonSerializer.Deserialize<ScorerJsonResponse>(json, JsonOptions);
            if (r is null) return null;
            return new FieldEvaluation(dimension, r.Score, r.Pros, r.Cons, r.SuccessStrategies);
        }
        catch { return null; }
    }

    private static T? TryParseJson<T>(string content) where T : class
    {
        var json = StripMarkdownFences(content);
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch { return null; }
    }

    private static string StripMarkdownFences(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
            var endFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (endFence >= 0) trimmed = trimmed[..endFence];
        }
        else if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed[3..];
            var endFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (endFence >= 0) trimmed = trimmed[..endFence];
        }
        return trimmed.Trim();
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}
