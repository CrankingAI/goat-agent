using Xunit;
using FluentAssertions;
using GoatCheck.Agent.Workflow;

namespace GoatCheck.Tests;

public class PromptLoaderTests
{
    private static readonly string[] ExpectedPrompts =
    [
        "StatisticalAchievementsScorer",
        "PeerRecognitionScorer",
        "DominanceWindowScorer",
        "HeadToHeadScorer",
        "CulturalImpactScorer",
        "PerCandidateNarrativeRollup",
        "HotTakeVoice"
    ];

    [Theory]
    [InlineData("StatisticalAchievementsScorer")]
    [InlineData("PeerRecognitionScorer")]
    [InlineData("DominanceWindowScorer")]
    [InlineData("HeadToHeadScorer")]
    [InlineData("CulturalImpactScorer")]
    [InlineData("PerCandidateNarrativeRollup")]
    [InlineData("HotTakeVoice")]
    public void Load_ReturnsNonEmptyContent(string promptName)
    {
        var content = PromptLoader.Load(promptName);
        content.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Load_AllSevenPromptsExist()
    {
        foreach (var name in ExpectedPrompts)
        {
            var content = PromptLoader.Load(name);
            content.Should().NotBeNullOrWhiteSpace($"Prompt '{name}' should be loadable");
        }
    }

    [Fact]
    public void Load_ThrowsForUnknownPrompt()
    {
        var act = () => PromptLoader.Load("NonExistentPrompt");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NonExistentPrompt*");
    }
}
