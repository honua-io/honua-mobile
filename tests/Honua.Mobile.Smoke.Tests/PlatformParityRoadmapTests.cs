namespace Honua.Mobile.Smoke.Tests;

public sealed class PlatformParityRoadmapTests
{
    [Fact]
    public void GuideIndex_LinksPlatformParityRoadmap()
    {
        var root = FindRepositoryRoot();
        var indexPath = Path.Combine(root, "docs", "guides", "README.md");
        var index = File.ReadAllText(indexPath);

        Assert.Contains("[Mobile Platform Parity Tracks](mobile-platform-parity-tracks.md)", index);
    }

    [Fact]
    public void PlatformParityRoadmap_DefinesIssue22AcceptanceSurface()
    {
        var root = FindRepositoryRoot();
        var guidePath = Path.Combine(root, "docs", "guides", "mobile-platform-parity-tracks.md");
        var guide = File.ReadAllText(guidePath);

        Assert.Contains("Issue #22", guide);
        Assert.Contains("## Scope Boundaries", guide);
        Assert.Contains("## Platform Parity Matrix", guide);
        Assert.Contains("## Priority Client Feature Map", guide);
        Assert.Contains("## Build and Release Requirements", guide);
        Assert.Contains("## Phased Rollout Plan", guide);

        Assert.Contains("| Native Android target |", guide);
        Assert.Contains("| Native iOS target |", guide);
        Assert.Contains("| Flutter target |", guide);
        Assert.Contains("honua-sdk-dotnet", guide);
        Assert.Contains("no SDK-neutral clients/contracts or copied SDK source", guide);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var agentsPath = Path.Combine(directory.FullName, "AGENTS.md");
            var docsPath = Path.Combine(directory.FullName, "docs");

            if (File.Exists(agentsPath) && Directory.Exists(docsPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }
}
