namespace GoatCheck.Agent.Contracts;

public record GoatRequest(
    GoatMetadata Metadata,
    CandidateRef Candidate,
    string Category,
    IReadOnlyList<CandidateRef> PeerCandidates);

public record GoatMetadata(IReadOnlyList<ScoringWeight> ScoringWeights);
public record ScoringWeight(string Topic, double Weight);
public record CandidateRef(string CandidateId, string DisplayName);

public record ResolvedGoatContext(
    GoatMetadata Metadata,
    CandidateRef Candidate,
    string Category,
    IReadOnlyList<CandidateRef> PeerCandidates);

public record CandidateEvaluationRequest(
    CandidateRef Candidate,
    ResolvedGoatContext GoatContext);

// Scorer output — carries CandidateRequest for fan-in aggregation
public record ScoredField(
    CandidateEvaluationRequest CandidateRequest,
    ScorerRunResult ScorerResult,
    EvaluationDimension Dimension);

// After score rollup (fan-in from 5 scorers)
public record CandidateWithScores(
    CandidateEvaluationRequest Request,
    IReadOnlyList<FieldEvaluation> FieldEvaluations,
    IReadOnlyList<ScoredField> ScoredFields,
    double WeightedScore);

// After narrative rollup
public record CandidateWithNarrative(
    CandidateWithScores Scores,
    PerCandidateNarrative CanonicalNarrative);

// After hot take voice
public record CandidateWithAudienceNarrative(
    CandidateWithNarrative Narrative,
    PerCandidateAudienceNarrative AudienceNarrative);
