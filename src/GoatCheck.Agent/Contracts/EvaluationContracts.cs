namespace GoatCheck.Agent.Contracts;

public enum EvaluationDimension { StatisticalAchievements, PeerRecognition, DominanceWindow, HeadToHead, CulturalImpact }
public enum FailureCode { Timeout, RateLimit, Transport, Parse, Unknown }

public record FieldEvaluation(
    EvaluationDimension Dimension,
    double Score,
    IReadOnlyList<string> Pros,
    IReadOnlyList<string> Cons,
    IReadOnlyList<string> SuccessStrategies);

public record ScorerRunResult(
    bool Success,
    FieldEvaluation? Result,
    FailureCode FailureCode,
    string? FailureMessage,
    int AttemptsUsed,
    long ElapsedMs);

public record ScorerDiagnostic(
    EvaluationDimension Dimension,
    FailureCode FailureCode,
    string? FailureMessage,
    int AttemptsUsed,
    long ElapsedMs);

/// <summary>
/// Generic failure-isolation wrapper for non-scorer agent calls (narrative, voice).
/// </summary>
public record AgentCallRunResult<T>(
    bool Success,
    T? Result,
    FailureCode FailureCode,
    string? FailureMessage,
    int AttemptsUsed,
    long ElapsedMs) where T : class;

public record PerCandidateNarrative(
    CandidateRef Candidate,
    IReadOnlyList<string> Pros,
    IReadOnlyList<string> Cons,
    IReadOnlyList<string> SuccessStrategies,
    IReadOnlyList<string> Contradictions);

public record PerCandidateAudienceNarrative(
    IReadOnlyList<string> HotTakePros,
    IReadOnlyList<string> HotTakeCons,
    IReadOnlyList<string> HotTakeSuccessFactors,
    string BestForSummary,
    string WatchOutForSummary);

public record PerCandidateEvaluation(
    CandidateRef Candidate,
    string Category,
    IReadOnlyList<FieldEvaluation> FieldEvaluations,
    double WeightedScore,
    PerCandidateNarrative CanonicalNarrative,
    PerCandidateAudienceNarrative HotTakeNarrative,
    string BestForSummary,
    string WatchOutForSummary,
    bool IsDegraded,
    string[] FailedDimensions,
    ScorerDiagnostic[] ScorerDiagnostics);

// Compact summary for scorer agents
public record CandidateSummary(
    string CandidateId,
    string DisplayName,
    string Category,
    IReadOnlyList<string> PeerNames,
    string Checksum)
{
    public static CandidateSummary FromContext(ResolvedGoatContext ctx)
    {
        var content = $"{ctx.Candidate.CandidateId}|{ctx.Category}|{string.Join(",", ctx.PeerCandidates.Select(p => p.DisplayName))}";
        var checksum = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(content)))[..8];
        return new CandidateSummary(
            ctx.Candidate.CandidateId,
            ctx.Candidate.DisplayName,
            ctx.Category,
            ctx.PeerCandidates.Select(p => p.DisplayName).ToList(),
            checksum);
    }
}
