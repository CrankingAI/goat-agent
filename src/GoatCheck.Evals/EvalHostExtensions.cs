using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using GoatCheck.Agent.Extensions;

namespace GoatCheck.Evals;

internal static class EvalHostExtensions
{
    public static void ConfigureEvalServices(HostApplicationBuilder builder)
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);

        builder.Configuration
            .AddJsonFile(Path.Combine(repoRoot, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(repoRoot, "appsettings.Development.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        builder.Services.AddGoatCheckAgent(builder.Configuration);
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoatCheck.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the current execution path.");
    }
}
