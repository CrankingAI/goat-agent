using Xunit;
using FluentAssertions;
using GoatCheck.Agent.Contracts;

namespace GoatCheck.Tests;

public class ScorerRunResultTests
{
    [Fact]
    public void FallbackFieldEvaluation_HasZeroScore()
    {
        var failureMessage = "Scorer failed: test error";
        var fallback = new FieldEvaluation(
            EvaluationDimension.StatisticalAchievements,
            0.0,
            [],
            [],
            [$"[DEGRADED] Scorer failed: {failureMessage}"]);

        fallback.Score.Should().Be(0.0);
        fallback.Pros.Should().BeEmpty();
        fallback.Cons.Should().BeEmpty();
        fallback.SuccessStrategies.Should().HaveCount(1);
        fallback.SuccessStrategies[0].Should().StartWith("[DEGRADED]");
    }

    [Fact]
    public void ScorerRunResult_FailedResult_HasCorrectStructure()
    {
        var result = new ScorerRunResult(
            false,
            null,
            FailureCode.Timeout,
            "Timed out after 180s",
            3,
            5400);

        result.Success.Should().BeFalse();
        result.Result.Should().BeNull();
        result.FailureCode.Should().Be(FailureCode.Timeout);
        result.FailureMessage.Should().Contain("180s");
        result.AttemptsUsed.Should().Be(3);
        result.ElapsedMs.Should().Be(5400);
    }

    [Fact]
    public void ScorerRunResult_SuccessResult_HasFieldEvaluation()
    {
        var eval = new FieldEvaluation(EvaluationDimension.CulturalImpact, 0.92, ["changed the game"], ["short window"], ["study his era"]);
        var result = new ScorerRunResult(true, eval, FailureCode.Unknown, null, 1, 1200);

        result.Success.Should().BeTrue();
        result.Result.Should().NotBeNull();
        result.Result!.Score.Should().BeApproximately(0.92, 0.001);
        result.Result.Dimension.Should().Be(EvaluationDimension.CulturalImpact);
    }

    [Theory]
    [InlineData(FailureCode.Timeout)]
    [InlineData(FailureCode.RateLimit)]
    [InlineData(FailureCode.Transport)]
    [InlineData(FailureCode.Parse)]
    [InlineData(FailureCode.Unknown)]
    public void FailureCode_AllValuesExist(FailureCode code)
    {
        Enum.IsDefined(typeof(FailureCode), code).Should().BeTrue();
    }

    [Fact]
    public void BestEffortMode_UsedFallbackForFailedScorers()
    {
        var failedResult = new ScorerRunResult(false, null, FailureCode.RateLimit, "Rate limited", 3, 10000);

        var fallback = CreateBestEffortFallback(EvaluationDimension.HeadToHead, failedResult);

        fallback.Dimension.Should().Be(EvaluationDimension.HeadToHead);
        fallback.Score.Should().Be(0.0);
        fallback.SuccessStrategies.Should().ContainSingle(s => s.Contains("[DEGRADED]"));
    }

    private static FieldEvaluation CreateBestEffortFallback(EvaluationDimension dimension, ScorerRunResult result)
    {
        return new FieldEvaluation(
            dimension,
            0.0,
            [],
            [],
            [$"[DEGRADED] Scorer failed: {result.FailureMessage}"]);
    }
}
