using System.Xml.Linq;

namespace Honua.Mobile.Smoke.Tests;

public sealed class RepoScaffoldingGateTests
{
    private static readonly string[] MobilePackageProjects =
    [
        "src/Honua.Mobile.Sdk/Honua.Mobile.Sdk.csproj",
        "src/Honua.Mobile.Field/Honua.Mobile.Field.csproj",
        "src/Honua.Mobile.Offline/Honua.Mobile.Offline.csproj",
        "src/Honua.Mobile.Maui/Honua.Mobile.Maui.csproj",
    ];

    [Fact]
    public void PublishWorkflow_IsLimitedToSignedMobilePackageReleaseTags()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-dotnet-mobile.yml"));

        Assert.Contains("- \"mobile-dotnet-v*\"", workflow);
        Assert.Contains("git verify-tag \"${GITHUB_REF_NAME}\"", workflow);
        Assert.Contains("Block manual publishing outside signed releases", workflow);
        Assert.Contains("github.event.inputs.dry_run != 'true'", workflow);
        Assert.Contains("Mobile .NET packages publish only from signed mobile-dotnet-v* release tags", workflow);
        Assert.Contains("if: ${{ github.event_name == 'push' }}", workflow);
        Assert.Contains("dotnet nuget push nupkgs/*.nupkg", workflow);

        foreach (var projectPath in MobilePackageProjects)
        {
            Assert.Contains(projectPath, workflow);
        }
    }

    [Fact]
    public void CiWorkflow_DefinesMobilePlatformSmokeMatrix()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        Assert.Contains("MAUI Android Build & Trim Smoke", workflow);
        Assert.Contains("dotnet workload install maui-android", workflow);
        Assert.Contains("--framework net10.0-android", workflow);
        Assert.Contains("Android trim smoke", workflow);
        Assert.Contains("/p:PublishTrimmed=true", workflow);
        Assert.Contains("/p:TrimMode=full", workflow);
        Assert.Contains("api-level: 33", workflow);
        Assert.Contains("reactivecircus/android-emulator-runner@v2", workflow);

        Assert.Contains("MAUI iOS Build & AOT Smoke", workflow);
        Assert.Contains("Verify iOS 17+ simulator runtime", workflow);
        Assert.Contains("iOS (1[7-9]|2[0-9])", workflow);
        Assert.Contains("--framework net10.0-ios", workflow);
        Assert.Contains("iOS trim and NativeAOT smoke", workflow);
        Assert.Contains("/p:PublishAot=true", workflow);
        Assert.Contains("/p:PublishAotUsingRuntimePack=true", workflow);

        Assert.Contains("HONUA_MOBILE_SMOKE_BASE_URL", workflow);
        Assert.Contains("HONUA_MOBILE_SMOKE_SERVICE_ID", workflow);
        Assert.Contains("HONUA_MOBILE_SMOKE_LAYER_ID", workflow);
    }

    [Fact]
    public void SecurityAndDependencyAutomation_CoverDependabotAndTrivy()
    {
        var root = FindRepositoryRoot();
        var dependabot = File.ReadAllText(Path.Combine(root, ".github", "dependabot.yml"));
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        Assert.Contains("package-ecosystem: \"nuget\"", dependabot);
        Assert.Contains("registries:", dependabot);
        Assert.Contains("github-honua", dependabot);
        Assert.Contains("package-ecosystem: \"npm\"", dependabot);
        Assert.Contains("directory: \"/src/Honua.Embed\"", dependabot);
        Assert.Contains("package-ecosystem: \"github-actions\"", dependabot);
        Assert.Contains("groups:", dependabot);

        Assert.Contains("aquasecurity/trivy-action@", ci);
        Assert.Contains("scan-type: fs", ci);
        Assert.Contains("format: sarif", ci);
        Assert.Contains("severity: CRITICAL,HIGH", ci);
        Assert.Contains("ignore-unfixed: true", ci);
        Assert.Contains("github/codeql-action/upload-sarif@", ci);
    }

    [Fact]
    public void ReadmeAndDocs_LinkServerRoadmapAndScaffoldingRunbook()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var guideIndex = File.ReadAllText(Path.Combine(root, "docs", "guides", "README.md"));
        var runbook = File.ReadAllText(Path.Combine(root, "docs", "guides", "repo-scaffolding-gates.md"));

        Assert.Contains("https://github.com/honua-io/honua-server/issues/811", readme);
        Assert.Contains("mobile SDK roadmap", readme);
        Assert.Contains("[Repo Scaffolding Gates](repo-scaffolding-gates.md)", guideIndex);
        Assert.Contains("honua-server #826", runbook);
        Assert.Contains("dotnet test tests/Honua.Mobile.Smoke.Tests/Honua.Mobile.Smoke.Tests.csproj --filter RepoScaffolding", runbook);
        Assert.Contains("bash scripts/verify-branch-protection.sh main", runbook);
    }

    [Fact]
    public void LicenseAndNugetPackageMetadata_AreReleaseReady()
    {
        var root = FindRepositoryRoot();
        var license = File.ReadAllText(Path.Combine(root, "LICENSE"));

        Assert.Contains("Apache License", license);
        Assert.Contains("Version 2.0, January 2004", license);

        foreach (var projectPath in MobilePackageProjects)
        {
            var document = XDocument.Load(Path.Combine(root, projectPath));

            Assert.Equal(Path.GetFileNameWithoutExtension(projectPath), GetProperty(document, "PackageId"));
            Assert.Equal("Honua", GetProperty(document, "Authors"));
            Assert.Equal("Apache-2.0", GetProperty(document, "PackageLicenseExpression"));
            Assert.Equal("README.md", GetProperty(document, "PackageReadmeFile"));
            Assert.Equal("https://github.com/honua-io/honua-mobile", GetProperty(document, "PackageProjectUrl"));
            Assert.Equal("https://github.com/honua-io/honua-mobile", GetProperty(document, "RepositoryUrl"));
            Assert.Equal("git", GetProperty(document, "RepositoryType"));

            var description = GetProperty(document, "Description");
            Assert.False(string.IsNullOrWhiteSpace(description));

            var tags = GetProperty(document, "PackageTags");
            Assert.Contains("honua", tags);
            Assert.Contains("mobile", tags);

            var readme = document
                .Descendants("None")
                .SingleOrDefault(element =>
                    string.Equals(element.Attribute("Include")?.Value, @"..\..\README.md", StringComparison.Ordinal));
            Assert.NotNull(readme);
            Assert.Equal("true", readme.Attribute("Pack")?.Value);
            Assert.Equal(@"\", readme.Attribute("PackagePath")?.Value);
        }
    }

    [Fact]
    public void BranchProtectionVerification_IsDocumentedAndRunnable()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "verify-branch-protection.sh"));
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var checklist = File.ReadAllText(Path.Combine(root, "quality", "release-checklist.md"));

        Assert.Contains("required_pull_request_reviews", script);
        Assert.Contains("required_status_checks", script);
        Assert.Contains("allow_force_pushes", script);
        Assert.Contains("gh api", script);
        Assert.Contains("HONUA_VERIFY_BRANCH_PROTECTION", ci);
        Assert.Contains("HONUA_BRANCH_PROTECTION_READ_TOKEN", ci);
        Assert.Contains("bash scripts/verify-branch-protection.sh \"${GITHUB_REF_NAME}\"", ci);
        Assert.Contains("branch protection confirmed", checklist);
    }

    private static string GetProperty(XDocument document, string propertyName)
    {
        return document
            .Descendants(propertyName)
            .Select(element => element.Value)
            .FirstOrDefault() ?? string.Empty;
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
