namespace ProdvisorAi.Demo.Services;

/// <summary>All on-disk locations used by the app, rooted in the configured data directory.</summary>
public sealed class StoragePaths
{
    public StoragePaths(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(Uploads);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Keys);
    }

    public string DataDirectory { get; }
    public string Database => Path.Combine(DataDirectory, "prodvisor-ai.db");
    public string Uploads => Path.Combine(DataDirectory, "uploads");
    public string Logs => Path.Combine(DataDirectory, "logs");
    public string Keys => Path.Combine(DataDirectory, "keys");

    /// <summary>Resolves a path stored in the DB (relative to the data directory).</summary>
    public string Resolve(string relativePath) => Path.Combine(DataDirectory, relativePath);
}
