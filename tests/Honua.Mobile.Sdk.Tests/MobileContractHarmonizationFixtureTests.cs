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
            "display-embed",
            "feature-attachments",
            "feature-edit",
            "feature-query",
            "form-feature-schema",
            "geometry",
            "geopackage-sync",
            "legacy-mobile-sdk",
            "offline-sync-state",
            "plugin-contracts",
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

        var attachments = FindFamily(fixture, "feature-attachments");
        Assert.Equal("honua-sdk-dotnet", attachments.Owner);
        Assert.Equal("Honua.Sdk.Abstractions", attachments.AuthoritativePackage);
        Assert.Contains(
            "Honua.Sdk.Abstractions.Features.IHonuaFeatureAttachmentClient",
            attachments.AuthoritativeTypes);
        Assert.Contains(
            "Honua.Mobile.Sdk.Features.HonuaMobileSdkFeatureClient",
            attachments.MobileTypes);
        Assert.Equal("adapter-only", attachments.MobileDisposition);
    }

    [Fact]
    public void Fixture_DeclaresMobileOwnedRuntimeContracts()
    {
        var fixture = LoadFixture();

        var routing = FindFamily(fixture, "routing");
        Assert.Equal("honua-sdk-dotnet", routing.Owner);
        Assert.Equal("Honua.Sdk.Abstractions", routing.AuthoritativePackage);
        Assert.Contains(
            "Honua.Sdk.Abstractions.Routing.IHonuaRoutingClient",
            routing.AuthoritativeTypes);
        Assert.Contains(
            "Honua.Mobile.Sdk.Routing.IRoutingLocationProvider",
            routing.MobileTypes);
        Assert.DoesNotContain(
            "Honua.Mobile.Sdk.Routing.HonuaRoutingClient",
            routing.MobileTypes);
        Assert.Equal("adapter-only", routing.MobileDisposition);

        var geopackage = FindFamily(fixture, "geopackage-sync");
        Assert.Equal("honua-mobile", geopackage.Owner);
        Assert.Equal("Honua.Mobile.Offline", geopackage.AuthoritativePackage);
        Assert.Contains(
            "Honua.Mobile.Offline.GeoPackage.IGeoPackageSyncStore",
            geopackage.AuthoritativeTypes);

        var scene = FindFamily(fixture, "scene-metadata");
        Assert.Equal("honua-sdk-dotnet", scene.Owner);
        Assert.Equal("Honua.Sdk.Abstractions; Honua.Sdk.Scenes", scene.AuthoritativePackage);
        Assert.Contains(
            "Honua.Sdk.Abstractions.Scenes.IHonuaSceneClient",
            scene.AuthoritativeTypes);
        Assert.Contains(
            "Honua.Mobile.Offline.ScenePackages.IHonuaScenePackageDownloader",
            scene.MobileTypes);
        var mobileSdkScenePrefix = string.Concat("Honua.Mobile.Sdk.", "Scenes.");
        Assert.DoesNotContain(
            scene.MobileTypes,
            type => type.StartsWith(mobileSdkScenePrefix, StringComparison.Ordinal));
        Assert.Equal("mobile-runtime-adapter", scene.MobileDisposition);

        var fields = FindFamily(fixture, "form-feature-schema");
        Assert.Equal("honua-sdk-dotnet", fields.Owner);
        Assert.Equal("Honua.Sdk.Abstractions; Honua.Sdk.Field", fields.AuthoritativePackage);
        Assert.Contains(
            "Honua.Sdk.Field.Forms.FormDefinition",
            fields.AuthoritativeTypes);
        Assert.Contains(
            "Honua.Sdk.Field.Records.FieldRecord",
            fields.AuthoritativeTypes);
        Assert.Contains(
            "Honua.Mobile.Field.Capture.MobileFieldCaptureWorkflow",
            fields.MobileTypes);
        Assert.DoesNotContain(
            fields.MobileTypes,
            type => type.StartsWith("Honua.Mobile.Field.Forms.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            fields.MobileTypes,
            type => type.StartsWith("Honua.Mobile.Field.Records.", StringComparison.Ordinal));
        Assert.Equal("mobile-runtime-adapter", fields.MobileDisposition);
    }

    [Fact]
    public void Fixture_HasPortableCompatibilityMetadata()
    {
        var fixture = LoadFixture();

        Assert.Equal("honua.mobile-contract-harmonization.v1", fixture.SchemaVersion);
        Assert.Equal("honua-mobile#48", fixture.MobileIssue);
        Assert.Equal("honua-sdk-dotnet#68", fixture.SdkIssue);
        Assert.Equal("honua-io/honua-mobile", fixture.Compatibility.MobileBaseline.Repository);
        Assert.Equal("unreleased-source", fixture.Compatibility.MobileBaseline.PackageVersion);

        var abstractionsPackage = fixture.Compatibility.SdkBaseline.Packages.Single(package => package.PackageId == "Honua.Sdk.Abstractions");
        Assert.Equal("Honua.Sdk.Abstractions", abstractionsPackage.PackageId);
        Assert.Equal("0.1.8-alpha.1", abstractionsPackage.Version);

        Assert.Contains(fixture.Compatibility.SdkBaseline.Packages, package => package.PackageId == "Honua.Sdk.Offline.Abstractions");
        Assert.Contains(fixture.Compatibility.SdkBaseline.Packages, package => package.PackageId == "Honua.Sdk.Offline");
        Assert.Contains(fixture.Compatibility.SdkBaseline.Packages, package => package.PackageId == "Honua.Sdk.Grpc");
        Assert.Contains(fixture.Compatibility.SdkBaseline.Packages, package => package.PackageId == "Honua.Sdk.GeoServices");
        Assert.Contains(fixture.Compatibility.SdkBaseline.Packages, package => package.PackageId == "Honua.Sdk.Scenes");
        Assert.Contains(fixture.Compatibility.SdkBaseline.Packages, package => package.PackageId == "Honua.Sdk.OgcFeatures");
        Assert.Contains(fixture.Compatibility.SdkBaseline.Packages, package => package.PackageId == "Honua.Sdk.Field");
        Assert.All(fixture.Compatibility.SdkBaseline.Packages, package => Assert.Equal("0.1.8-alpha.1", package.Version));
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

        public ContractCompatibility Compatibility { get; init; } = new();

        public IReadOnlyList<ContractFamily> ModelFamilies { get; init; } = [];
    }

    private sealed record ContractCompatibility
    {
        public ContractBaseline MobileBaseline { get; init; } = new();

        public SdkBaseline SdkBaseline { get; init; } = new();
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
