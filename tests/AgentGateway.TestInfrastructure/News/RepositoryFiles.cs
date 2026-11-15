namespace AgentGateway.TestInfrastructure.News;

/// <summary>Finds files committed in the repository (fixtures shared with other projects) from the test output folder.</summary>
public static class RepositoryFiles
{
    public static string Read(string relativePath) => File.ReadAllText(PathOf(relativePath));

    public static string PathOf(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AgentGateway.slnx")))
                return Path.Combine(directory.FullName, relativePath);
        throw new FileNotFoundException("The repository root (AgentGateway.slnx) was not found above the test output.", relativePath);
    }
}
