using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Observability;
using GoatCheck.Agent.Options;

namespace GoatCheck.Agent.Workflow;

internal sealed class PerCandidateNarrativeRollupExecutor(
    IChatClient chatClient,
    GoatCheckOptions opts,
    GoatCheckMetrics metrics,
    ILogger<PerCandidateNarrativeRollupExecutor> logger)
    : Executor<CandidateWithScores, CandidateWithNarrative>("per_candidate_narrative_rollup")
{
    private static readonly string SystemPrompt = PromptLoader.Load("PerCandidateNarrativeRollup");
    private readonly IChatClient _chatClient = chatClient;

    public override async ValueTask<CandidateWithNarrative> HandleAsync(
        CandidateWithScores msg,
        IWorkflowContext ctx,
        CancellationToken ct)
    {
        var candidate = msg.Request.Candidate;
        var userPrompt = BuildUserPrompt(candidate, msg.Request.GoatContext, msg.FieldEvaluations);

        var result = await LlmCallHelper.ExecuteNarrativeAsync(
            _chatClient, SystemPrompt, userPrompt, candidate,
            opts.DeploymentName, opts.Resilience, opts.Observability, metrics, logger, ct);

        PerCandidateNarrative narrative;
        if (result.Success && result.Result is not null)
        {
            narrative = new PerCandidateNarrative(
                candidate,
                result.Result.Pros,
                result.Result.Cons,
                result.Result.SuccessStrategies,
                result.Result.Contradictions);
        }
        else
        {
            logger.LogWarning("Narrative rollup failed for {Candidate}: {Message}", candidate.DisplayName, result.FailureMessage);
            narrative = new PerCandidateNarrative(candidate, ["Unable to generate narrative."], [], [], []);
        }

        return new CandidateWithNarrative(msg, narrative);
    }

    private static string BuildUserPrompt(CandidateRef candidate, ResolvedGoatContext context, IReadOnlyList<FieldEvaluation> evals)
    {
        var weightsJson = JsonSerializer.Serialize(context.Metadata.ScoringWeights.Select(w => new { w.Topic, w.Weight }));
        var evalsJson = JsonSerializer.Serialize(evals.Select(e => new
        {
            dimension = e.Dimension.ToString(),
            score = e.Score,
            pros = e.Pros,
            cons = e.Cons,
            successStrategies = e.SuccessStrategies
        }));

        return $"""
            Candidate: {candidate.DisplayName}
            Category: {context.Category}
            Peers: {string.Join(", ", context.PeerCandidates.Select(p => p.DisplayName))}
            Scoring Weights: {weightsJson}
            Field Evaluations: {evalsJson}
            """;
    }
}
