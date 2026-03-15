using Xunit;
using FluentAssertions;
using GoatCheck.Agent.Observability;

namespace GoatCheck.Tests;

public class MetricsTests
{
    [Fact]
    public void AllMetrics_Exist()
    {
        using var metrics = new GoatCheckMetrics();

        metrics.LlmAttemptsTotal.Should().NotBeNull();
        metrics.LlmTimeoutsTotal.Should().NotBeNull();
        metrics.LlmRetriesTotal.Should().NotBeNull();
        metrics.LlmLatencyMs.Should().NotBeNull();
        metrics.EndToEndLatencyMs.Should().NotBeNull();
        metrics.LlmInflight.Should().NotBeNull();
        metrics.DegradedCandidatesTotal.Should().NotBeNull();
        metrics.Llm5xxTotal.Should().NotBeNull();
        metrics.LlmParseErrorsTotal.Should().NotBeNull();
        metrics.LlmErrorsTotal.Should().NotBeNull();
    }

    [Fact]
    public void Metrics_HaveCorrectNames()
    {
        using var metrics = new GoatCheckMetrics();

        metrics.LlmAttemptsTotal.Name.Should().Be("goatcheck_llm_attempts_total");
        metrics.LlmTimeoutsTotal.Name.Should().Be("goatcheck_llm_timeouts_total");
        metrics.LlmRetriesTotal.Name.Should().Be("goatcheck_llm_retries_total");
        metrics.LlmLatencyMs.Name.Should().Be("goatcheck_llm_latency_ms");
        metrics.EndToEndLatencyMs.Name.Should().Be("goatcheck_end_to_end_latency_ms");
        metrics.LlmInflight.Name.Should().Be("goatcheck_llm_inflight");
        metrics.DegradedCandidatesTotal.Name.Should().Be("goatcheck_degraded_candidates_total");
        metrics.Llm5xxTotal.Name.Should().Be("goatcheck_llm_5xx_total");
        metrics.LlmParseErrorsTotal.Name.Should().Be("goatcheck_llm_parse_errors_total");
        metrics.LlmErrorsTotal.Name.Should().Be("goatcheck_llm_errors_total");
    }

    [Fact]
    public void Metrics_CanBeIncrementedWithoutException()
    {
        using var metrics = new GoatCheckMetrics();

        var act = () =>
        {
            metrics.LlmAttemptsTotal.Add(1);
            metrics.LlmTimeoutsTotal.Add(1);
            metrics.LlmRetriesTotal.Add(1);
            metrics.LlmLatencyMs.Record(150.0);
            metrics.EndToEndLatencyMs.Record(3000.0);
            metrics.LlmInflight.Add(1);
            metrics.LlmInflight.Add(-1);
            metrics.DegradedCandidatesTotal.Add(1);
            metrics.Llm5xxTotal.Add(1);
            metrics.LlmParseErrorsTotal.Add(1);
            metrics.LlmErrorsTotal.Add(1);
        };

        act.Should().NotThrow();
    }
}
