using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Observability;
using GoatCheck.Agent.Options;

namespace GoatCheck.Agent.Workflow;

internal sealed class PeerRecognitionScorerExecutor(
    IChatClient chatClient,
    GoatCheckOptions opts,
    GoatCheckMetrics metrics,
    ILogger<PeerRecognitionScorerExecutor> logger)
    : Executor<CandidateEvaluationRequest, ScoredField>("peer_recognition_scorer")
{
    private static readonly string SystemPrompt = PromptLoader.Load("PeerRecognitionScorer");
    private readonly IChatClient _chatClient = chatClient;

    public override async ValueTask<ScoredField> HandleAsync(
        CandidateEvaluationRequest req,
        IWorkflowContext ctx,
        CancellationToken ct)
    {
        var summary = CandidateSummary.FromContext(req.GoatContext);
        var userPrompt = BuildUserPrompt(summary, req.Candidate);
        var result = await LlmCallHelper.ExecuteScorerAsync(
            _chatClient, SystemPrompt, userPrompt, EvaluationDimension.PeerRecognition, req.Candidate,
            opts.DeploymentName, opts.Resilience, opts.Observability, metrics, logger, ct);
        return new ScoredField(req, result, EvaluationDimension.PeerRecognition);
    }

    private static string BuildUserPrompt(CandidateSummary summary, CandidateRef candidate) =>
        $"""
        Candidate: {candidate.DisplayName}
        Category: {summary.Category}
        Peers in this category: {string.Join(", ", summary.PeerNames)}
        Profile Checksum: {summary.Checksum}
        """;
}
