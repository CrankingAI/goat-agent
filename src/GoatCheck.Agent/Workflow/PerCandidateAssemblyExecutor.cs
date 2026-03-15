using System.Diagnostics;
using Microsoft.Agents.AI.Workflows;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Observability;

namespace GoatCheck.Agent.Workflow;

internal sealed class PerCandidateAssemblyExecutor(GoatCheckMetrics metrics)
    : Executor<CandidateWithAudienceNarrative, PerCandidateEvaluation>("per_candidate_assembly")
{
    public override ValueTask<PerCandidateEvaluation> HandleAsync(
        CandidateWithAudienceNarrative msg,
        IWorkflowContext ctx,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var scores = msg.Narrative.Scores;
        var candidate = scores.Request.Candidate;
        var category = scores.Request.GoatContext.Category;

        var failedFields = scores.ScoredFields.Where(sf => !sf.ScorerResult.Success).ToList();
        var isDegraded = failedFields.Count > 0;
        var failedDimensions = failedFields.Select(sf => sf.Dimension.ToString()).ToArray();
        var scorerDiagnostics = failedFields.Select(sf => new ScorerDiagnostic(
            sf.Dimension,
            sf.ScorerResult.FailureCode,
            sf.ScorerResult.FailureMessage,
            sf.ScorerResult.AttemptsUsed,
            sf.ScorerResult.ElapsedMs)).ToArray();

        var evaluation = new PerCandidateEvaluation(
            candidate,
            category,
            scores.FieldEvaluations,
            scores.WeightedScore,
            msg.Narrative.CanonicalNarrative,
            msg.AudienceNarrative,
            msg.AudienceNarrative.BestForSummary,
            msg.AudienceNarrative.WatchOutForSummary,
            isDegraded,
            failedDimensions,
            scorerDiagnostics);

        sw.Stop();
        metrics.EndToEndLatencyMs.Record(sw.ElapsedMilliseconds);

        return ValueTask.FromResult(evaluation);
    }
}
