using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Extensions;
using GoatCheck.Agent.Workflow;
using Microsoft.Agents.AI.Workflows;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: goatcheck-cli <request.json> [output.json]");
    return 1;
}

var requestFilePath = args[0];
Console.Error.WriteLine($"Loading request from: {Path.GetFullPath(requestFilePath)}");

GoatRequest request;
try
{
    var json = await File.ReadAllTextAsync(requestFilePath);
    request = JsonSerializer.Deserialize<GoatRequest>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }) ?? throw new InvalidOperationException("Failed to deserialize request.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load request: {ex.Message}");
    return 1;
}

var hostBuilder = Host.CreateApplicationBuilder(args);
hostBuilder.Configuration.AddEnvironmentVariables();
hostBuilder.AddServiceDefaults();
hostBuilder.Services.AddSingleton(new ActivitySource("GoatCheck.Console"));
hostBuilder.Services.AddGoatCheckAgent(hostBuilder.Configuration);

hostBuilder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

using var host = hostBuilder.Build();
await host.StartAsync();

try
{
    var consoleActivitySource = host.Services.GetRequiredService<ActivitySource>();
    var factory = host.Services.GetRequiredService<GoatWorkflowFactory>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var configuration = host.Services.GetRequiredService<IConfiguration>();
    var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
        ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
    var otlpProtocol = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]
        ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL")
        ?? "grpc";
    var serviceName = configuration["OTEL_SERVICE_NAME"]
        ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
        ?? "goatcheck-console";

    if (string.IsNullOrWhiteSpace(otlpEndpoint))
    {
        logger.LogWarning("OpenTelemetry exporter is not configured. OTEL_EXPORTER_OTLP_ENDPOINT is missing for service {ServiceName}.", serviceName);
    }
    else
    {
        logger.LogInformation("OpenTelemetry exporter configured for service {ServiceName} -> {OtlpEndpoint} ({OtlpProtocol}).", serviceName, otlpEndpoint, otlpProtocol);
    }

    logger.LogInformation("Starting GOAT Check for {Candidate} in {Category}...", request.Candidate.DisplayName, request.Category);

    /*OTEL*/
    using var consoleActivity = consoleActivitySource.StartActivity("console.run");
    consoleActivity?.SetTag("service.name", serviceName);
    consoleActivity?.SetTag("candidate.id", request.Candidate.CandidateId);
    consoleActivity?.SetTag("candidate.name", request.Candidate.DisplayName);
    consoleActivity?.SetTag("category", request.Category);
    consoleActivity?.SetTag("input.file", Path.GetFullPath(requestFilePath));
    consoleActivity?.SetTag("otlp.protocol", otlpProtocol);

    var workflow = factory.CreateWorkflow();

    PerCandidateEvaluation? result = null;
    Exception? error = null;

    await using var run = await InProcessExecution.RunStreamingAsync(workflow, request);
    await foreach (var evt in run.WatchStreamAsync())
    {
        switch (evt)
        {
            case WorkflowStartedEvent:
                logger.LogInformation("Workflow started.");
                break;
            case ExecutorInvokedEvent invoked:
                logger.LogDebug("Executor invoked: {ExecutorId}", invoked.ExecutorId);
                break;
            case WorkflowOutputEvent output when output.Data is PerCandidateEvaluation evaluation:
                result = evaluation;
                consoleActivity?.SetTag("result.status", "ok");
                break;
            case WorkflowErrorEvent err:
                error = err.Exception;
                consoleActivity?.SetStatus(ActivityStatusCode.Error, err.Exception?.Message ?? "Workflow error.");
                consoleActivity?.SetTag("result.status", "error");
                logger.LogError(err.Exception, "Workflow error.");
                break;
        }
    }

    if (error is not null)
    {
        consoleActivity?.SetStatus(ActivityStatusCode.Error, error.Message);
        Console.Error.WriteLine($"Workflow failed: {error.Message}");
        return 1;
    }

    if (result is null)
    {
        consoleActivity?.SetStatus(ActivityStatusCode.Error, "Workflow completed with no output.");
        consoleActivity?.SetTag("result.status", "empty_output");
        Console.Error.WriteLine("Workflow completed with no output.");
        return 1;
    }

    Console.Error.WriteLine();
    Console.Error.WriteLine("=== GOAT CHECK RESULTS ===");
    Console.Error.WriteLine();
    Console.Error.WriteLine($"## {result.Candidate.DisplayName} ({result.Category})");
    Console.Error.WriteLine($"   Score: {result.WeightedScore:F2}");
    Console.Error.WriteLine($"   Best For: {result.BestForSummary}");
    Console.Error.WriteLine($"   Watch Out For: {result.WatchOutForSummary}");
    if (result.IsDegraded)
        Console.Error.WriteLine($"   [DEGRADED] Failed dimensions: {string.Join(", ", result.FailedDimensions)}");

    var outputJson = JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    Console.WriteLine(outputJson);

    return 0;
}
finally
{
    await host.StopAsync();
}
