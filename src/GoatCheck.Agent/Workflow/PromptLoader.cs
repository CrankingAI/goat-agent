using System.Reflection;

namespace GoatCheck.Agent.Workflow;

public static class PromptLoader
{
    private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();

    public static string Load(string name)
    {
        var resourceName = $"GoatCheck.Agent.Prompts.System.{name}.md";
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
