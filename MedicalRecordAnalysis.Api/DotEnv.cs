namespace MedicalRecordAnalysis.Api;

/**
 * Loads KEY=value pairs from the nearest .env file at or above a directory into the process environment, for
 * local development. Variables that are already set take precedence.
 */
public static class DotEnv
{
    public static void Load(string startDirectory)
    {
        FileInfo? file = Find(startDirectory);
        if (file is null)
        {
            return;
        }

        foreach (string rawLine in File.ReadAllLines(file.FullName))
        {
            string line = rawLine.Trim();
            int separator = line.IndexOf('=');
            if (line.Length == 0 || line.StartsWith('#') || separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static FileInfo? Find(string startDirectory)
    {
        for (DirectoryInfo? directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
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
