namespace Honua.Mobile.Smoke.Tests;

public sealed class MobileParityBacklogDocsTests
{
    [Fact]
    public void PhaseZeroDocs_AreMarkedAsHistoricalPlanningBaselines()
    {
        var root = FindRepositoryRoot();
        var phaseZeroDocs = new[]
        {
            Path.Combine(root, "docs", "phase-0", "PARITY_SPEC.md"),
            Path.Combine(root, "docs", "phase-0", "PHASE_0_SUMMARY.md"),
            Path.Combine(root, "docs", "phase-0", "INNOVATION_SPEC.md"),
            Path.Combine(root, "docs", "phase-0", "TEST_STRATEGY.md"),
        };

        foreach (var path in phaseZeroDocs)
        {
            var contents = File.ReadAllText(path);

            Assert.Contains("Status note, May 2026", contents);
            Assert.Contains("docs/features/README.md", contents);
            Assert.Contains("docs/guides/validation-strategy.md", contents);
            Assert.Contains("docs/guides/mobile-sdk-backlog-roadmap.md", contents);
        }
    }

    [Fact]
    public void MobileSdkBacklogRoadmap_LinksCurrentParityBacklog()
    {
        var root = FindRepositoryRoot();
        var roadmapPath = Path.Combine(root, "docs", "guides", "mobile-sdk-backlog-roadmap.md");
        var roadmap = File.ReadAllText(roadmapPath);

        Assert.Contains("## Status Vocabulary", roadmap);
        Assert.Contains("## Fulcrum/Survey123 Parity Backlog Index", roadmap);

        for (var issue = 208; issue <= 226; issue++)
        {
            Assert.Contains($"honua-io/honua-mobile/issues/{issue}", roadmap);
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
