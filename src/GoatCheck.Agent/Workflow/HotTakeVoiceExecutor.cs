using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Observability;
using GoatCheck.Agent.Options;

namespace GoatCheck.Agent.Workflow;

internal sealed class HotTakeVoiceExecutor(
    IChatClient chatClient,
    GoatCheckOptions opts,
    GoatCheckMetrics metrics,
    ILogger<HotTakeVoiceExecutor> logger)
    : Executor<CandidateWithNarrative, CandidateWithAudienceNarrative>("hot_take_voice")
{
    private static readonly string SystemPrompt = PromptLoader.Load("HotTakeVoice");
    private readonly IChatClient _chatClient = chatClient;

    public override async ValueTask<CandidateWithAudienceNarrative> HandleAsync(
        CandidateWithNarrative msg,
        IWorkflowContext ctx,
        CancellationToken ct)
    {
        var candidate = msg.Scores.Request.Candidate;
        var userPrompt = BuildUserPrompt(candidate, msg.CanonicalNarrative);

        var result = await LlmCallHelper.ExecuteVoiceAsync(
            _chatClient, SystemPrompt, userPrompt, candidate,
            opts.DeploymentName, opts.Resilience, opts.Observability, metrics, logger, ct);

        PerCandidateAudienceNarrative audienceNarrative;
        if (result.Success && result.Result is not null)
        {
            audienceNarrative = new PerCandidateAudienceNarrative(
                result.Result.HotTakePros,
                result.Result.HotTakeCons,
                result.Result.HotTakeSuccessFactors,
                result.Result.BestForSummary,
                result.Result.WatchOutForSummary);
        }
        else
        {
            logger.LogWarning("Hot take voice failed for {Candidate}: {Message}", candidate.DisplayName, result.FailureMessage);
            audienceNarrative = new PerCandidateAudienceNarrative(
                [],
                [],
                [],
                $"A strong contender for GOAT status: {candidate.DisplayName}.",
                "Review the full analysis for the strongest counter-argument.");
        }

        return new CandidateWithAudienceNarrative(msg, audienceNarrative);
    }

    private static string BuildUserPrompt(CandidateRef candidate, PerCandidateNarrative narrative)
    {
        var narrativeJson = JsonSerializer.Serialize(new
        {
            pros = narrative.Pros,
            cons = narrative.Cons,
            successStrategies = narrative.SuccessStrategies,
            contradictions = narrative.Contradictions
        });

        return $"""
            Candidate: {candidate.DisplayName}
            Canonical Narrative: {narrativeJson}
            """;
    }
}
