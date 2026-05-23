using System.Text.RegularExpressions;

namespace Honua.Mobile.Smoke.Tests;

public sealed partial class FieldCollectionWorkflowRouteSourceTests
{
    [Fact]
    public void PilotCriticalRoutes_AreBackedByWorkflowPages()
    {
        var root = FindRepositoryRoot();
        var shell = File.ReadAllText(Path.Combine(
            root,
            "apps",
            "Honua.Mobile.FieldCollection",
            "AppShell.xaml.cs"));
        var pages = File.ReadAllText(Path.Combine(
            root,
            "apps",
            "Honua.Mobile.FieldCollection",
            "Views",
            "WorkflowPages.cs"));

        var routeTargets = RouteRegistrationRegex()
            .Matches(shell)
            .ToDictionary(
                match => match.Groups["route"].Value,
                match => match.Groups["page"].Value,
                StringComparer.Ordinal);

        var expectedRoutes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["record-detail"] = "RecordDetailPage",
            ["record-edit"] = "RecordEditPage",
            ["record-create"] = "RecordEditPage",
            ["authentication"] = "AuthenticationPage",
            ["diagnostics"] = "DiagnosticsPage",
            ["map/layer-settings"] = "LayerSettingsPage",
            ["map/feature-detail"] = "FeatureDetailPage",
            ["sync/conflict-resolution"] = "ConflictResolutionPage",
            ["sync/sync-history"] = "SyncHistoryPage",
            ["settings/server-config"] = "ServerConfigPage",
            ["settings/user-profile"] = "UserProfilePage",
            ["settings/about"] = "AboutPage"
        };

        foreach (var expectedRoute in expectedRoutes)
        {
            Assert.True(routeTargets.TryGetValue(expectedRoute.Key, out var page), $"Missing route {expectedRoute.Key}.");
            Assert.Equal(expectedRoute.Value, page);
            Assert.Contains($"sealed class {page} : WorkflowPage<", pages, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("PlaceholderPage", pages, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Honua.Mobile.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Honua.Mobile.sln from test output directory.");
    }

    [GeneratedRegex("""Routing\.RegisterRoute\("(?<route>[^"]+)", typeof\((?<page>[^)]+)\)\);""")]
    private static partial Regex RouteRegistrationRegex();
}
