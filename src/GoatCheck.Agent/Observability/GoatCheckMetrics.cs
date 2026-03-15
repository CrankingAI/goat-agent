using System.Diagnostics.Metrics;

namespace GoatCheck.Agent.Observability;

public sealed class GoatCheckMetrics : IDisposable
{
    private readonly Meter _meter = new("GoatCheck.Agent", "1.0.0");

    public readonly Counter<long> LlmAttemptsTotal;
    public readonly Counter<long> LlmTimeoutsTotal;
    public readonly Counter<long> LlmRetriesTotal;
    public readonly Counter<long> Llm5xxTotal;
    public readonly Counter<long> LlmParseErrorsTotal;
    public readonly Counter<long> LlmErrorsTotal;
    public readonly Histogram<double> LlmLatencyMs;
    public readonly Histogram<double> EndToEndLatencyMs;
    public readonly UpDownCounter<long> LlmInflight;
    public readonly Counter<long> DegradedCandidatesTotal;

    public GoatCheckMetrics()
    {
        LlmAttemptsTotal = _meter.CreateCounter<long>("goatcheck_llm_attempts_total", description: "Total LLM call attempts");
        LlmTimeoutsTotal = _meter.CreateCounter<long>("goatcheck_llm_timeouts_total", description: "Total LLM timeouts");
        LlmRetriesTotal = _meter.CreateCounter<long>("goatcheck_llm_retries_total", description: "Total LLM retries due to 429 rate limiting");
        Llm5xxTotal = _meter.CreateCounter<long>("goatcheck_llm_5xx_total", description: "Total LLM 5xx server errors");
        LlmParseErrorsTotal = _meter.CreateCounter<long>("goatcheck_llm_parse_errors_total", description: "Total LLM response parse failures");
        LlmErrorsTotal = _meter.CreateCounter<long>("goatcheck_llm_errors_total", description: "Total LLM unknown/transport errors");
        LlmLatencyMs = _meter.CreateHistogram<double>("goatcheck_llm_latency_ms", unit: "ms", description: "LLM call latency");
        EndToEndLatencyMs = _meter.CreateHistogram<double>("goatcheck_end_to_end_latency_ms", unit: "ms", description: "End-to-end workflow latency");
        LlmInflight = _meter.CreateUpDownCounter<long>("goatcheck_llm_inflight", description: "In-flight LLM calls");
        DegradedCandidatesTotal = _meter.CreateCounter<long>("goatcheck_degraded_candidates_total", description: "Candidates with degraded results");
    }

    public void Dispose() => _meter.Dispose();
}
