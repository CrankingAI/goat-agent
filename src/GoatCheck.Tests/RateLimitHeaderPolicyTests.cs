using Xunit;
using FluentAssertions;
using GoatCheck.Agent.Observability;

namespace GoatCheck.Tests;

public class RateLimitHeaderPolicyTests
{
    [Theory]
    [InlineData("1000", 1)]    // 1000ms → 1s
    [InlineData("1500", 2)]    // 1500ms → 2s (ceiling)
    [InlineData("500", 1)]     // 500ms → 1s (ceiling)
    [InlineData("0", 0)]
    [InlineData("30000", 30)]
    public void ParseRetryAfterMs_ConvertsMillisecondsToSeconds(string input, int expected)
    {
        RateLimitHeaderPolicy.ParseRetryAfterMs(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    public void ParseRetryAfterMs_ReturnsNullForInvalidInput(string? input)
    {
        RateLimitHeaderPolicy.ParseRetryAfterMs(input).Should().BeNull();
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData("0", 0)]
    [InlineData("120", 120)]
    public void ParseRetryAfter_ParsesIntegerSeconds(string input, int expected)
    {
        RateLimitHeaderPolicy.ParseRetryAfter(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void ParseRetryAfter_ReturnsNullForInvalidInput(string? input)
    {
        RateLimitHeaderPolicy.ParseRetryAfter(input).Should().BeNull();
    }
}
