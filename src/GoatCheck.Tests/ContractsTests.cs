using Xunit;
using System.Text.Json;
using FluentAssertions;
using GoatCheck.Agent.Contracts;

namespace GoatCheck.Tests;

public class ContractsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void GoatRequest_IsImmutableRecord()
    {
        var request = CreateSampleRequest();
        var request2 = CreateSampleRequest();
        request.Should().BeEquivalentTo(request2);
    }

    [Fact]
    public void GoatRequest_JsonRoundtrip()
    {
        var request = CreateSampleRequest();
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GoatRequest>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Candidate.CandidateId.Should().Be("mj-23");
        deserialized.Category.Should().Be("NBA players of the 1990s");
        deserialized.Metadata.ScoringWeights.Should().HaveCount(5);
        deserialized.PeerCandidates.Should().HaveCount(2);
    }

    [Fact]
    public void PerCandidateEvaluation_JsonRoundtrip()
    {
        var evaluation = CreateSampleEvaluation();
        var json = JsonSerializer.Serialize(evaluation, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<PerCandidateEvaluation>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Candidate.CandidateId.Should().Be("mj-23");
        deserialized.WeightedScore.Should().BeApproximately(0.95, 0.001);
        deserialized.BestForSummary.Should().Be("The GOAT of GOATs!");
    }

    [Fact]
    public void FieldEvaluation_IsImmutableRecord()
    {
        var eval1 = new FieldEvaluation(EvaluationDimension.StatisticalAchievements, 0.95, ["6 rings"], ["demanding style"], ["study his footwork"]);
        var eval2 = new FieldEvaluation(EvaluationDimension.StatisticalAchievements, 0.95, ["6 rings"], ["demanding style"], ["study his footwork"]);
        eval1.Should().BeEquivalentTo(eval2);
    }

    private static GoatRequest CreateSampleRequest() =>
        new(
            new GoatMetadata(new List<ScoringWeight>
            {
                new("StatisticalAchievements", 0.25),
                new("PeerRecognition", 0.20),
                new("DominanceWindow", 0.20),
                new("HeadToHead", 0.20),
                new("CulturalImpact", 0.15)
            }),
            new CandidateRef("mj-23", "Michael Jordan"),
            "NBA players of the 1990s",
            new List<CandidateRef>
            {
                new("magic-32", "Magic Johnson"),
                new("bird-33", "Larry Bird")
            });

    private static PerCandidateEvaluation CreateSampleEvaluation()
    {
        var candidate = new CandidateRef("mj-23", "Michael Jordan");
        var fieldEvaluations = new List<FieldEvaluation>
        {
            new(EvaluationDimension.StatisticalAchievements, 0.95, ["6 championships"], ["demanding"], ["study his work ethic"])
        };
        var narrative = new PerCandidateNarrative(candidate, ["pro1"], ["con1"], ["strategy1"], []);
        var audienceNarrative = new PerCandidateAudienceNarrative(
            ["hot take pro"], ["hot take con"], ["hot take strategy"],
            "The GOAT of GOATs!", "Everyone compares themselves to him.");

        return new PerCandidateEvaluation(
            candidate, "NBA players of the 1990s", fieldEvaluations, 0.95,
            narrative, audienceNarrative,
            "The GOAT of GOATs!", "Everyone compares themselves to him.",
            false, [], []);
    }
}
