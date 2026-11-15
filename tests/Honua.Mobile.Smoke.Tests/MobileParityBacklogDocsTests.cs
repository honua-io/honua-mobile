namespace Honua.Mobile.Smoke.Tests;

public sealed class MobileParityBacklogDocsTests
{
    [Fact]
    public void MobileSdkBacklogRoadmap_LinksCurrentParityBacklog()
    {
        var root = FindRepositoryRoot();
        var roadmapPath = Path.Combine(root, "docs", "guides", "mobile-sdk-backlog-roadmap.md");
        var roadmap = File.ReadAllText(roadmapPath);

        Assert.Contains("## Status Vocabulary", roadmap);
        Assert.Contains("## Field Collection Parity Backlog Index", roadmap);

        for (var issue = 208; issue <= 226; issue++)
        {
            Assert.Contains($"honua-io/honua-mobile/issues/{issue}", roadmap);
        }
    }

    [Fact]
    public void MobileSdkBacklogRoadmap_TracksBackOfficeDependencyOwners()
    {
        var root = FindRepositoryRoot();
        var roadmapPath = Path.Combine(root, "docs", "guides", "mobile-sdk-backlog-roadmap.md");
        var roadmap = File.ReadAllText(roadmapPath);

        Assert.Contains("## Back-Office Dependency Handoff", roadmap);
        Assert.Contains("Mobile should consume the resulting `Honua.Sdk.*` contracts", roadmap);

        foreach (var issue in new[] { 1158, 1159, 1160, 348, 346, 349, 350, 507, 509 })
        {
            Assert.Contains($"honua-io/honua-server/issues/{issue}", roadmap);
        }
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
