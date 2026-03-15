var builder = DistributedApplication.CreateBuilder(args);

var appInsightsCs = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] ?? string.Empty;

var api = builder.AddProject<Projects.GoatCheck_Api>("goatcheck-api");

if (!string.IsNullOrEmpty(appInsightsCs))
    api.WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsightsCs);

builder.Build().Run();
