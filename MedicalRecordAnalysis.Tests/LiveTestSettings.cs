namespace MedicalRecordAnalysis.Tests;

/**
 * Live tests call real provider APIs and cost money, so they only run when RUN_LIVE_TESTS=1.
 * API keys are read from the environment, falling back to the .env file at the repository root.
 */
public static class LiveTestSettings
{
    private const string RunLiveTestsVariable = "RUN_LIVE_TESTS";

    static LiveTestSettings()
    {
        LoadDotEnv();
    }

    public static bool Enabled => Environment.GetEnvironmentVariable(RunLiveTestsVariable) == "1";

    public static string? SkipReason(string requiredKey)
    {
        if (!Enabled)
        {
            return $"Live test; set {RunLiveTestsVariable}=1 to run.";
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(requiredKey)))
        {
            return $"Live test; {requiredKey} is not set in the environment or .env.";
        }

        return null;
    }

    private static void LoadDotEnv()
    {
        FileInfo? dotEnv = FindDotEnv();
        if (dotEnv is null)
        {
            return;
        }

        foreach (string rawLine in File.ReadAllLines(dotEnv.FullName))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim().Trim('"', '\'');

            // Variables already set in the environment take precedence over .env.
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static FileInfo? FindDotEnv()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            FileInfo candidate = new FileInfo(Path.Combine(directory.FullName, ".env"));
            if (candidate.Exists)
            {
                return candidate;
            }
        }

        return null;
    }
}

/**
 * A fact that is skipped unless live tests are enabled and the given API key is available.
 */
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(string requiredKey)
    {
        string? reason = LiveTestSettings.SkipReason(requiredKey);
        if (reason is not null)
        {
            Skip = reason;
        }
    }
}
