using System.Text.Json;
using System.Xml.Linq;

namespace Honua.Mobile.Sdk.Tests;

public sealed class SdkTrainReleaseEvidenceTests
{
    private const string ExpectedSdkTrainVersion = "0.1.16-alpha.1";
    private const string SdkTrainVersionExpression = "$(HonuaSdkDotNetTrainVersion)";
    private const string EvidencePath = "quality/release-evidence/honua-2026-05-preview-mobile-dotnet-sdk-train.json";

    [Fact]
    public void DirectoryBuildProps_Declares202605PreviewSdkTrain()
    {
        var props = LoadBuildProps();

        Assert.Equal(ExpectedSdkTrainVersion, ReadProperty(props, "HonuaSdkDotNetTrainVersion"));
        Assert.Equal("dotnet-sdk-v$(HonuaSdkDotNetTrainVersion)", ReadProperty(props, "HonuaSdkDotNetTrainTag"));
        Assert.Equal("honua-io/honua-sdk-dotnet", ReadProperty(props, "HonuaSdkDotNetTrainRepository"));
        Assert.Equal(
            "https://github.com/honua-io/honua-sdk-dotnet/releases/tag/$(HonuaSdkDotNetTrainTag)",
            ReadProperty(props, "HonuaSdkDotNetTrainReleaseUrl"));
        Assert.Equal(
            "f31cfeb6c21af896b96194688a393638461f3d8a",
            ReadProperty(props, "HonuaSdkDotNetTrainReleaseCommit"));
        Assert.Equal("2026-05-07T08:52:04Z", ReadProperty(props, "HonuaSdkDotNetTrainReleasePublishedAt"));
    }

    [Fact]
    public void HonuaSdkPackageReferences_UseCentralReleaseTrainProperty()
    {
        var violations = ProjectFiles()
            .SelectMany(project => XDocument.Load(project)
                .Descendants("PackageReference")
                .Select(reference => new
                {
                    Project = Relative(project),
                    Include = reference.Attribute("Include")?.Value,
                    Version = reference.Attribute("Version")?.Value,
                }))
            .Where(reference => reference.Include?.StartsWith("Honua.Sdk.", StringComparison.Ordinal) == true)
            .Where(reference => reference.Version != SdkTrainVersionExpression)
            .Select(reference => $"{reference.Project}: {reference.Include} uses {reference.Version ?? "<missing>"}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void MobileProjects_DoNotReferenceHonuaSdkDotNetSourceProjects()
    {
        var violations = ProjectFiles()
            .SelectMany(project => XDocument.Load(project)
                .Descendants("ProjectReference")
                .Select(reference => new
                {
                    Project = Relative(project),
                    Include = reference.Attribute("Include")?.Value,
                }))
            .Where(reference => IsHonuaSdkDotNetProjectReference(reference.Include))
            .Select(reference => $"{reference.Project}: {reference.Include}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ReleaseEvidenceDocument_MatchesCentralSdkTrain()
    {
        var props = LoadBuildProps();
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), EvidencePath)));
        var root = evidence.RootElement;
        var sdkTrain = root.GetProperty("sdkTrain");
        var mobileValidation = root.GetProperty("mobileValidation");

        Assert.Equal("honua-2026-05-preview", root.GetProperty("releaseId").GetString());
        Assert.Equal("mobile-dotnet-sdk-train", root.GetProperty("repositoryLaneId").GetString());
        Assert.Equal(ReadProperty(props, "HonuaSdkDotNetTrainRepository"), sdkTrain.GetProperty("repository").GetString());
        Assert.Equal(ReadProperty(props, "HonuaSdkDotNetTrainVersion"), sdkTrain.GetProperty("packageVersion").GetString());
        Assert.Equal($"dotnet-sdk-v{ExpectedSdkTrainVersion}", sdkTrain.GetProperty("releaseTag").GetString());
        Assert.Equal(ReadProperty(props, "HonuaSdkDotNetTrainReleaseCommit"), sdkTrain.GetProperty("releaseCommit").GetString());
        Assert.Equal(
            ReadProperty(props, "HonuaSdkDotNetTrainReleasePublishedAt"),
            sdkTrain.GetProperty("releasePublishedAt").GetString());
        Assert.Equal("HonuaSdkDotNetTrainVersion", mobileValidation.GetProperty("packageReferenceProperty").GetString());
        Assert.Equal(SdkTrainVersionExpression, mobileValidation.GetProperty("packageReferenceVersionExpression").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("waiver").ValueKind);
    }

    private static XDocument LoadBuildProps()
        => XDocument.Load(Path.Combine(FindRepositoryRoot(), "Directory.Build.props"));

    private static string ReadProperty(XDocument document, string name)
        => document.Descendants(name).Single().Value;

    private static IEnumerable<string> ProjectFiles()
        => Directory.EnumerateFiles(FindRepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static bool IsHonuaSdkDotNetProjectReference(string? include)
    {
        if (string.IsNullOrWhiteSpace(include))
        {
            return false;
        }

        return include.Contains("honua-sdk-dotnet", StringComparison.OrdinalIgnoreCase) ||
            include.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part.StartsWith("Honua.Sdk.", StringComparison.Ordinal));
    }

    private static string Relative(string path)
        => Path.GetRelativePath(FindRepositoryRoot(), path).Replace(Path.DirectorySeparatorChar, '/');

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Honua.Mobile.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Honua.Mobile.sln from the test output directory.");
    }
}
