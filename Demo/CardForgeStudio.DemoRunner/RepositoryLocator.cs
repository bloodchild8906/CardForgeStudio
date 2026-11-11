namespace CardForgeStudio.DemoRunner;

/// <summary>Finds the repository root by walking up from a starting directory.</summary>
public static class RepositoryLocator
{
    /// <summary>Returns the first ancestor directory that contains both Server and Shared folders.</summary>
    public static string Find(string startingDirectory)
    {
        var directory = new DirectoryInfo(startingDirectory);
        while (directory is not null)
        {
            var hasServerFolder = Directory.Exists(Path.Combine(directory.FullName, "Server"));
            var hasSharedFolder = Directory.Exists(Path.Combine(directory.FullName, "Shared"));
            if (hasServerFolder && hasSharedFolder) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the CardForgeStudio repository.");
    }
}
