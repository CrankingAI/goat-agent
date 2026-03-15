using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Observability;
using GoatCheck.Agent.Options;

namespace GoatCheck.Agent.Workflow;

/// <summary>
/// Receives individual ScoredField messages from the fan-in barrier.
/// The barrier guarantees all 5 scorers complete before any messages are delivered,
/// so this accumulator is safe: all 5 calls happen within a single superstep.
/// Returns null for the first 4 calls (framework skips null); emits CandidateWithScores on the 5th.
/// </summary>
internal sealed class PerCandidateScoreRollupExecutor(
    GoatCheckOptions opts,
    GoatCheckMetrics metrics,
    ILogger<PerCandidateScoreRollupExecutor> logger)
    : Executor<ScoredField, CandidateWithScores>("per_candidate_score_rollup")
{
    private const int ExpectedScorerCount = 5;
    private readonly List<ScoredField> _received = new(capacity: ExpectedScorerCount);

    public override ValueTask<CandidateWithScores> HandleAsync(
        ScoredField msg,
        IWorkflowContext ctx,
        CancellationToken ct)
    {
        _received.Add(msg);

        if (_received.Count < ExpectedScorerCount)
            return ValueTask.FromResult<CandidateWithScores>(null!);

        return ValueTask.FromResult(ComputeRollup(_received));
    }

    private CandidateWithScores ComputeRollup(List<ScoredField> scoredFields)
    {
        var req = scoredFields[0].CandidateRequest;
        var candidate = req.Candidate;

        var isStrict = string.Equals(opts.Resilience.PartialFailureMode, "Strict", StringComparison.OrdinalIgnoreCase);
        var failedFields = scoredFields.Where(sf => !sf.ScorerResult.Success).ToList();

        if (isStrict && failedFields.Count > 0)
        {
            var first = failedFields[0];
            throw new InvalidOperationException(
                $"Scorer failed in Strict mode for {candidate.DisplayName} [{first.Dimension}]: {first.ScorerResult.FailureMessage}");
        }

        var fieldEvaluations = new List<FieldEvaluation>();
        foreach (var sf in scoredFields)
        {
            if (sf.ScorerResult.Success && sf.ScorerResult.Result is not null)
            {
                fieldEvaluations.Add(sf.ScorerResult.Result);
            }
            else
            {
                fieldEvaluations.Add(new FieldEvaluation(
                    sf.Dimension,
                    0.0,
                    [],
                    [],
                    [$"[DEGRADED] Scorer failed: {sf.ScorerResult.FailureMessage}"]));
                logger.LogWarning("Scorer degraded for candidate={Candidate} dimension={Dimension}: {Message}",
                    candidate.DisplayName, sf.Dimension, sf.ScorerResult.FailureMessage);
            }
        }

        if (failedFields.Count > 0)
            metrics.DegradedCandidatesTotal.Add(1);

        var weightedScore = CalculateWeightedScore(fieldEvaluations, req.GoatContext.Metadata);

        return new CandidateWithScores(req, fieldEvaluations, scoredFields, weightedScore);
    }

    private static double CalculateWeightedScore(IReadOnlyList<FieldEvaluation> evals, GoatMetadata metadata)
    {
        var totalWeight = 0.0;
        var weightedSum = 0.0;

        foreach (var eval in evals)
        {
            var topicName = eval.Dimension.ToString();
            var weight = metadata.ScoringWeights
                .FirstOrDefault(w => string.Equals(w.Topic, topicName, StringComparison.OrdinalIgnoreCase))?.Weight ?? 0.0;
            weightedSum += eval.Score * weight;
            totalWeight += weight;
        }

        return totalWeight > 0 ? weightedSum / totalWeight : 0.0;
    }
}
