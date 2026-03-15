using Azure.AI.OpenAI;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GoatCheck.Agent.Observability;
using GoatCheck.Agent.Options;
using GoatCheck.Agent.Workflow;

namespace GoatCheck.Agent.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGoatCheckAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GoatCheckOptions>(configuration.GetSection(GoatCheckOptions.SectionName));

        services.AddSingleton<GoatCheckMetrics>();

        // Singleton AzureOpenAIClient — NEVER per-call
        services.AddSingleton<AzureOpenAIClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<GoatCheckOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<AzureOpenAIClient>>();

            var clientOptions = new AzureOpenAIClientOptions();
            clientOptions.NetworkTimeout = TimeSpan.FromSeconds(opts.Resilience.NetworkTimeoutSeconds);
            // Disable SDK built-in retries — LlmCallHelper owns all retry logic.
            clientOptions.RetryPolicy = new ClientRetryPolicy(maxRetries: 0);
            clientOptions.AddPolicy(new RateLimitHeaderPolicy(), PipelinePosition.PerTry);

            logger.LogInformation(
                "AzureOpenAIClient configured: endpoint={Endpoint}, deployment={Deployment}, timeout={Timeout}s",
                opts.AzureOpenAIEndpoint, opts.DeploymentName, opts.Resilience.NetworkTimeoutSeconds);

            return new AzureOpenAIClient(
                new Uri(opts.AzureOpenAIEndpoint),
                new Azure.AzureKeyCredential(opts.AzureOpenAIApiKey),
                clientOptions);
        });

        services.AddSingleton<IChatClient>(sp =>
        {
            var azureClient = sp.GetRequiredService<AzureOpenAIClient>();
            var opts = sp.GetRequiredService<IOptions<GoatCheckOptions>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return azureClient
                .GetChatClient(opts.DeploymentName)
                .AsIChatClient()
                .AsBuilder()
                .UseOpenTelemetry(loggerFactory, "azure.ai.openai")
                .Build();
        });

        services.AddSingleton<GoatWorkflowFactory>();

        // Validate PayloadCaptureMode guardrail at startup
        services.AddOptions<GoatCheckOptions>()
            .PostConfigure<ILogger<GoatCheckOptions>>((opts, logger) =>
            {
                if (opts.Observability.PayloadCaptureMode == "Full" && !opts.Observability.IncludeSensitivePayloads)
                {
                    logger.LogWarning(
                        "PayloadCaptureMode=Full requires IncludeSensitivePayloads=true. Falling back to Metadata.");
                    opts.Observability.PayloadCaptureMode = "Metadata";
                }
            });

        return services;
    }
}
