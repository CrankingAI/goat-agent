using Xunit;
using FluentAssertions;
using GoatCheck.Agent.Contracts;

namespace GoatCheck.Tests;

public class ScoreRollupTests
{
    [Fact]
    public void WeightedScore_CalculatesCorrectly()
    {
        var weights = new List<ScoringWeight>
        {
            new("StatisticalAchievements", 0.50),
            new("PeerRecognition", 0.20),
            new("DominanceWindow", 0.10),
            new("HeadToHead", 0.10),
            new("CulturalImpact", 0.10)
        };

        var evals = new List<FieldEvaluation>
        {
            new(EvaluationDimension.StatisticalAchievements, 1.0, [], [], []),
            new(EvaluationDimension.PeerRecognition, 0.5, [], [], []),
            new(EvaluationDimension.DominanceWindow, 0.5, [], [], []),
            new(EvaluationDimension.HeadToHead, 0.5, [], [], []),
            new(EvaluationDimension.CulturalImpact, 0.5, [], [], []),
        };

        // Expected: (1.0*0.50 + 0.5*0.20 + 0.5*0.10 + 0.5*0.10 + 0.5*0.10) / 1.0 = 0.75
        var result = CalculateWeightedScore(evals, weights);
        result.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public void WeightedScore_WithZeroScore_ContributesDegradedResult()
    {
        var weights = new List<ScoringWeight>
        {
            new("StatisticalAchievements", 0.50),
            new("PeerRecognition", 0.50),
        };

        var evals = new List<FieldEvaluation>
        {
            new(EvaluationDimension.StatisticalAchievements, 0.0, [], [], []),
            new(EvaluationDimension.PeerRecognition, 1.0, [], [], []),
        };

        // (0.0*0.50 + 1.0*0.50) / 1.0 = 0.50
        var result = CalculateWeightedScore(evals, weights);
        result.Should().BeApproximately(0.50, 0.001);
    }

    [Fact]
    public void WeightedScore_WithAllPerfect_ReturnsOne()
    {
        var weights = new List<ScoringWeight>
        {
            new("StatisticalAchievements", 0.25),
            new("PeerRecognition", 0.25),
            new("DominanceWindow", 0.20),
            new("HeadToHead", 0.20),
            new("CulturalImpact", 0.10),
        };

        var evals = weights.Select(w =>
            new FieldEvaluation(
                Enum.Parse<EvaluationDimension>(w.Topic),
                1.0, [], [], [])).ToList();

        var result = CalculateWeightedScore(evals, weights);
        result.Should().BeApproximately(1.0, 0.001);
    }

    private static double CalculateWeightedScore(IReadOnlyList<FieldEvaluation> evals, IReadOnlyList<ScoringWeight> scoringWeights)
    {
        var totalWeight = 0.0;
        var weightedSum = 0.0;

        foreach (var eval in evals)
        {
            var topicName = eval.Dimension.ToString();
            var weight = scoringWeights
                .FirstOrDefault(w => string.Equals(w.Topic, topicName, StringComparison.OrdinalIgnoreCase))?.Weight ?? 0.0;
            weightedSum += eval.Score * weight;
            totalWeight += weight;
        }

        return totalWeight > 0 ? weightedSum / totalWeight : 0.0;
    }
}
