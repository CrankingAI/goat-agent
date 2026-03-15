namespace GoatCheck.Agent.Options;

public class GoatCheckOptions
{
    public const string SectionName = "GoatCheck";
    public string AzureOpenAIEndpoint { get; set; } = "";
    public string AzureOpenAIApiKey { get; set; } = "";
    public string AzureOpenAIApiVersion { get; set; } = "2025-04-01-preview";
    public string DeploymentName { get; set; } = "gpt-5.4_min_tpm";
    public ResilienceOptions Resilience { get; set; } = new();
    public ObservabilityOptions Observability { get; set; } = new();
}
