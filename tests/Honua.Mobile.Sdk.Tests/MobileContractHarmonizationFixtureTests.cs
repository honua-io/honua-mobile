using System.Text.Json;

namespace Honua.Mobile.Sdk.Tests;

public sealed class MobileContractHarmonizationFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Fixture_CoversRequiredContractFamilies()
    {
        var fixture = LoadFixture();
        var familyIds = fixture.ModelFamilies
            .Select(family => family.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
        [
            "feature-edit",
            "feature-query",
            "form-feature-schema",
            "geometry",
            "geopackage-sync",
            "offline-sync-state",
            "routing",
            "scene-metadata",
        ], familyIds);
    }

    [Fact]
    public void Fixture_DeclaresSdkOwnedFeatureContracts()
    {
        var fixture = LoadFixture();

        var query = FindFamily(fixture, "feature-query");
        Assert.Equal("honua-sdk-dotnet", query.Owner);
        Assert.Equal("Honua.Sdk.Abstractions", query.AuthoritativePackage);
        Assert.Contains(
            "Honua.Sdk.Abstractions.Features.FeatureQueryRequest",
            query.AuthoritativeTypes);
        Assert.Contains(
            "Honua.Mobile.Sdk.Models.QueryFeaturesRequest",
            query.MobileTypes);
        Assert.Equal("adapter-required", query.MobileDisposition);

        var edits = FindFamily(fixture, "feature-edit");
        Assert.Equal("honua-sdk-dotnet", edits.Owner);
        Assert.Contains(
            "Honua.Sdk.Abstractions.Features.FeatureEditRequest",
            edits.AuthoritativeTypes);
        Assert.Contains(
            "Honua.Mobile.Offline.GeoPackage.OfflineEditOperation",
            edits.MobileTypes);
    }

    [Fact]
    public void Fixture_DeclaresMobileOwnedRuntimeContracts()
    {
        var fixture = LoadFixture();

        var scene = FindFamily(fixture, "scene-metadata");
        Assert.Equal("honua-mobile", scene.Owner);
        Assert.Equal("Honua.Mobile.Sdk", scene.AuthoritativePackage);
        Assert.Contains(
            "Honua.Mobile.Sdk.Scenes.HonuaScenePackageManifest",
            scene.AuthoritativeTypes);

        var routing = FindFamily(fixture, "routing");
        Assert.Equal("honua-mobile", routing.Owner);
        Assert.Contains(
            "Honua.Mobile.Sdk.Routing.IRoutingLocationProvider",
            routing.MobileTypes);

        var geopackage = FindFamily(fixture, "geopackage-sync");
        Assert.Equal("honua-mobile", geopackage.Owner);
        Assert.Equal("Honua.Mobile.Offline", geopackage.AuthoritativePackage);
        Assert.Contains(
            "Honua.Mobile.Offline.GeoPackage.IGeoPackageSyncStore",
            geopackage.AuthoritativeTypes);
    }

    [Fact]
    public void Fixture_HasPortableCompatibilityMetadata()
    {
        var fixture = LoadFixture();

        Assert.Equal("honua.mobile-contract-harmonization.v1", fixture.SchemaVersion);
        Assert.Equal("honua-mobile#48", fixture.MobileIssue);
        Assert.Equal("honua-sdk-dotnet#68", fixture.SdkIssue);
        Assert.Equal("honua-io/honua-mobile", fixture.MobileBaseline.Repository);
        Assert.Equal("unreleased-source", fixture.MobileBaseline.PackageVersion);

        var abstractionsPackage = fixture.SdkBaseline.Packages.Single(package => package.PackageId == "Honua.Sdk.Abstractions");
        Assert.Equal("Honua.Sdk.Abstractions", abstractionsPackage.PackageId);
        Assert.Equal("0.1.0-alpha.1", abstractionsPackage.Version);

        Assert.Contains(fixture.SdkBaseline.Packages, package => package.PackageId == "Honua.Sdk.Offline.Abstractions");
        Assert.Contains(fixture.SdkBaseline.Packages, package => package.PackageId == "Honua.Sdk.Offline");
    }

    private static ContractFixture LoadFixture()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "contracts",
            "fixtures",
            "mobile-sdk-contract-harmonization.v1.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ContractFixture>(json, JsonOptions)
            ?? throw new InvalidOperationException("Contract harmonization fixture was empty.");
    }

    private static ContractFamily FindFamily(ContractFixture fixture, string id)
        => fixture.ModelFamilies.Single(family => string.Equals(family.Id, id, StringComparison.Ordinal));

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

    private sealed record ContractFixture
    {
        public string SchemaVersion { get; init; } = string.Empty;

        public string MobileIssue { get; init; } = string.Empty;

        public string SdkIssue { get; init; } = string.Empty;

        public ContractBaseline MobileBaseline { get; init; } = new();

        public SdkBaseline SdkBaseline { get; init; } = new();

        public IReadOnlyList<ContractFamily> ModelFamilies { get; init; } = [];
    }

    private sealed record ContractBaseline
    {
        public string Repository { get; init; } = string.Empty;

        public string PackageVersion { get; init; } = string.Empty;
    }

    private sealed record SdkBaseline
    {
        public IReadOnlyList<SdkPackage> Packages { get; init; } = [];
    }

    private sealed record SdkPackage
    {
        public string PackageId { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;
    }

    private sealed record ContractFamily
    {
        public string Id { get; init; } = string.Empty;

        public string Owner { get; init; } = string.Empty;

        public string AuthoritativePackage { get; init; } = string.Empty;

        public IReadOnlyList<string> AuthoritativeTypes { get; init; } = [];

        public IReadOnlyList<string> MobileTypes { get; init; } = [];

        public string MobileDisposition { get; init; } = string.Empty;
    }
}
