namespace GoatCheck.Agent.Options;

public class ResilienceOptions
{
    public int NetworkTimeoutSeconds { get; set; } = 180;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;
    public int RetryMaxDelayMs { get; set; } = 15000;
    public bool RetryJitter { get; set; } = true;
    public bool RetryOnTimeout { get; set; } = true;
    public bool RetryOn429 { get; set; } = true;
    public bool RetryOn5xx { get; set; } = true;
    public int MaxConcurrentLlmCalls { get; set; } = 2;
    public string PartialFailureMode { get; set; } = "BestEffort"; // "Strict" | "BestEffort"
}
