using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Observability;
using GoatCheck.Agent.Options;

namespace GoatCheck.Agent.Workflow;

internal sealed class DominanceWindowScorerExecutor(
    IChatClient chatClient,
    GoatCheckOptions opts,
    GoatCheckMetrics metrics,
    ILogger<DominanceWindowScorerExecutor> logger)
    : Executor<CandidateEvaluationRequest, ScoredField>("dominance_window_scorer")
{
    private static readonly string SystemPrompt = PromptLoader.Load("DominanceWindowScorer");
    private readonly IChatClient _chatClient = chatClient;

    public override async ValueTask<ScoredField> HandleAsync(
        CandidateEvaluationRequest req,
        IWorkflowContext ctx,
        CancellationToken ct)
    {
        var summary = CandidateSummary.FromContext(req.GoatContext);
        var userPrompt = BuildUserPrompt(summary, req.Candidate);
        var result = await LlmCallHelper.ExecuteScorerAsync(
            _chatClient, SystemPrompt, userPrompt, EvaluationDimension.DominanceWindow, req.Candidate,
            opts.DeploymentName, opts.Resilience, opts.Observability, metrics, logger, ct);
        return new ScoredField(req, result, EvaluationDimension.DominanceWindow);
    }

    private static string BuildUserPrompt(CandidateSummary summary, CandidateRef candidate) =>
        $"""
        Candidate: {candidate.DisplayName}
        Category: {summary.Category}
        Peers in this category: {string.Join(", ", summary.PeerNames)}
        Profile Checksum: {summary.Checksum}
        """;
}
