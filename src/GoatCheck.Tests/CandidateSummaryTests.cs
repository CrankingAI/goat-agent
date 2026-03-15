using Xunit;
using FluentAssertions;
using GoatCheck.Agent.Contracts;

namespace GoatCheck.Tests;

public class CandidateSummaryTests
{
    private const string SampleCategory = "NBA players of the 1990s";

    [Fact]
    public void FromContext_ExtractsCandidateId()
    {
        var ctx = CreateContext();
        var summary = CandidateSummary.FromContext(ctx);
        summary.CandidateId.Should().Be("mj-23");
    }

    [Fact]
    public void FromContext_ExtractsDisplayName()
    {
        var ctx = CreateContext();
        var summary = CandidateSummary.FromContext(ctx);
        summary.DisplayName.Should().Be("Michael Jordan");
    }

    [Fact]
    public void FromContext_ExtractsCategory()
    {
        var ctx = CreateContext();
        var summary = CandidateSummary.FromContext(ctx);
        summary.Category.Should().Be(SampleCategory);
    }

    [Fact]
    public void FromContext_ExtractsPeerNames()
    {
        var ctx = CreateContext();
        var summary = CandidateSummary.FromContext(ctx);
        summary.PeerNames.Should().Contain("Magic Johnson");
        summary.PeerNames.Should().Contain("Larry Bird");
    }

    [Fact]
    public void FromContext_GeneratesChecksum()
    {
        var ctx = CreateContext();
        var summary = CandidateSummary.FromContext(ctx);

        summary.Checksum.Should().NotBeNullOrEmpty();
        summary.Checksum.Should().HaveLength(8);
    }

    [Fact]
    public void FromContext_ChecksumIsStable()
    {
        var ctx = CreateContext();
        var summary1 = CandidateSummary.FromContext(ctx);
        var summary2 = CandidateSummary.FromContext(ctx);

        summary1.Checksum.Should().Be(summary2.Checksum);
    }

    [Fact]
    public void FromContext_ChecksumDiffersForDifferentCandidate()
    {
        var ctx1 = CreateContext();
        var ctx2 = new ResolvedGoatContext(
            new GoatMetadata([]),
            new CandidateRef("kobe-24", "Kobe Bryant"),
            SampleCategory,
            [new CandidateRef("magic-32", "Magic Johnson")]);

        var summary1 = CandidateSummary.FromContext(ctx1);
        var summary2 = CandidateSummary.FromContext(ctx2);

        summary1.Checksum.Should().NotBe(summary2.Checksum);
    }

    private static ResolvedGoatContext CreateContext() =>
        new(
            new GoatMetadata([]),
            new CandidateRef("mj-23", "Michael Jordan"),
            SampleCategory,
            [
                new CandidateRef("magic-32", "Magic Johnson"),
                new CandidateRef("bird-33", "Larry Bird")
            ]);
}
