using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GoatCheck.Agent.Extensions;
using GoatCheck.Agent.Options;

namespace GoatCheck.Tests;

public class PayloadCaptureModeGuardrailTests
{
    [Fact]
    public void WhenFullMode_WithoutSensitivePayloads_FallsBackToMetadata()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoatCheck:AzureOpenAIEndpoint"] = "https://test.example.com/",
                ["GoatCheck:AzureOpenAIApiKey"] = "test-key",
                ["GoatCheck:Observability:PayloadCaptureMode"] = "Full",
                ["GoatCheck:Observability:IncludeSensitivePayloads"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGoatCheckAgent(config);

        var provider = services.BuildServiceProvider();

        // Trigger PostConfigure by resolving the options
        var opts = provider.GetRequiredService<IOptions<GoatCheckOptions>>().Value;

        // After PostConfigure runs, Full should have been downgraded to Metadata
        opts.Observability.PayloadCaptureMode.Should().Be("Metadata");
    }

    [Fact]
    public void WhenFullMode_WithSensitivePayloads_StaysAsFull()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoatCheck:AzureOpenAIEndpoint"] = "https://test.example.com/",
                ["GoatCheck:AzureOpenAIApiKey"] = "test-key",
                ["GoatCheck:Observability:PayloadCaptureMode"] = "Full",
                ["GoatCheck:Observability:IncludeSensitivePayloads"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGoatCheckAgent(config);

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<GoatCheckOptions>>().Value;

        opts.Observability.PayloadCaptureMode.Should().Be("Full");
    }

    [Fact]
    public void WhenMetadataMode_DefaultBehavior_IsUnchanged()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoatCheck:AzureOpenAIEndpoint"] = "https://test.example.com/",
                ["GoatCheck:AzureOpenAIApiKey"] = "test-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGoatCheckAgent(config);

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<GoatCheckOptions>>().Value;

        opts.Observability.PayloadCaptureMode.Should().Be("Metadata");
    }
}
