namespace GoatCheck.Agent.Options;

public class ObservabilityOptions
{
    public string PayloadCaptureMode { get; set; } = "Metadata"; // None | Metadata | Sampled | Full
    public double PayloadSampleRate { get; set; } = 0.05;
    public bool EmitPromptHashes { get; set; } = true;
    public bool EmitTokenEstimates { get; set; } = true;
    public bool EnableAppInsightsParity { get; set; } = true;
    public bool IncludeSensitivePayloads { get; set; } = false;
}
