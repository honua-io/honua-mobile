using System.Runtime.CompilerServices;
using Honua.Mobile.Sdk.Scenes;

namespace Honua.Mobile.Sdk.Tests;

public sealed class HonuaScenePackageManifestTests
{
    private static readonly DateTimeOffset FreshNow = new(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParseJson_ValidManifest_DeserializesAndValidates()
    {
        var manifest = HonuaScenePackageManifest.ParseJson(ReadFixture("offline-scene-package-manifest.json"));

        var result = manifest.Validate(FreshNow, manifest.Assets.Select(asset => asset.Key!));

        Assert.True(result.IsValid, FormatIssues(result));
        Assert.Equal(HonuaScenePackageState.Ready, result.State);
        Assert.Equal(HonuaScenePackageManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal("downtown-honolulu", manifest.SceneId);
        Assert.Equal(HonuaScenePackageEditionGates.Pro, manifest.EditionGate);
        Assert.Equal(3, manifest.Assets.Count);
        Assert.Contains(manifest.Assets, asset =>
            asset.Required &&
            asset.Type == HonuaScenePackageAssetTypes.SceneMetadata &&
            asset.Path == "metadata/scene.json");
    }

    [Fact]
    public void ParseJson_MalformedJson_ThrowsFormatException()
    {
        var ex = Assert.Throws<FormatException>(() => HonuaScenePackageManifest.ParseJson("{ not-json"));

        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MalformedManifest_ReturnsInvalidIssues()
    {
        var manifest = CreateManifest(
            sceneId: "",
            extent: new HonuaSceneBounds
            {
                MinLongitude = -157.84,
                MinLatitude = 21.30,
                MaxLongitude = -157.88,
                MaxLatitude = 21.32,
            },
            lod: new HonuaScenePackageLod
            {
                MinZoom = 18,
                MaxZoom = 12,
            },
            assets: new[]
            {
                new HonuaScenePackageAsset
                {
                    Key = "bad-path",
                    Type = "terrain-tile",
                    Path = "../terrain/12/742/1619.terrain",
                    Bytes = -1,
                    Sha256 = "not-a-sha",
                    Required = true,
                },
            });

        var result = manifest.Validate(FreshNow);

        Assert.False(result.IsValid);
        Assert.Equal(HonuaScenePackageState.Invalid, result.State);
        AssertHasCode(result, HonuaScenePackageValidationCodes.MissingSceneId);
        AssertHasCode(result, HonuaScenePackageValidationCodes.InvalidExtent);
        AssertHasCode(result, HonuaScenePackageValidationCodes.InvalidLod);
        AssertHasCode(result, HonuaScenePackageValidationCodes.InvalidAssetPath);
        AssertHasCode(result, HonuaScenePackageValidationCodes.InvalidAssetHash);
        AssertHasCode(result, HonuaScenePackageValidationCodes.MissingRequiredSceneMetadata);
    }

    [Fact]
    public void Validate_ExpiredManifest_ReturnsExpired()
    {
        var manifest = CreateManifest(
            staleAfterUtc: FreshNow.AddDays(-1),
            offlineUseExpiresAtUtc: FreshNow.AddMinutes(-1));

        var result = manifest.Validate(FreshNow);

        Assert.False(result.IsValid);
        Assert.Equal(HonuaScenePackageState.Expired, result.State);
        AssertHasCode(result, HonuaScenePackageValidationCodes.OfflineUseExpired);
    }

    [Fact]
    public void Validate_OverBudgetManifest_ReturnsInvalid()
    {
        var manifest = CreateManifest(byteBudget: new HonuaScenePackageByteBudget
        {
            MaxPackageBytes = 10_000,
            DeclaredBytes = 10_001,
        });

        var result = manifest.Validate(FreshNow);

        Assert.False(result.IsValid);
        Assert.Equal(HonuaScenePackageState.Invalid, result.State);
        AssertHasCode(result, HonuaScenePackageValidationCodes.OverByteBudget);
    }

    [Fact]
    public void Validate_UnsupportedVersion_ReturnsInvalid()
    {
        var manifest = CreateManifest(schemaVersion: "honua.scene-package.v2");

        var result = manifest.Validate(FreshNow);

        Assert.False(result.IsValid);
        Assert.Equal(HonuaScenePackageState.Invalid, result.State);
        AssertHasCode(result, HonuaScenePackageValidationCodes.UnsupportedSchemaVersion);
    }

    [Fact]
    public void Validate_MissingRequiredLocalAsset_ReturnsPartial()
    {
        var manifest = CreateManifest();

        var result = manifest.Validate(FreshNow, new[] { "scene-metadata" });

        Assert.False(result.IsValid);
        Assert.Equal(HonuaScenePackageState.Partial, result.State);
        Assert.Contains(result.Issues, issue =>
            issue.Code == HonuaScenePackageValidationCodes.MissingRequiredAsset &&
            issue.AssetKey == "buildings-tileset");
    }

    [Fact]
    public void Validate_StaleManifest_ReturnsWarningButValid()
    {
        var manifest = CreateManifest(
            staleAfterUtc: FreshNow.AddMinutes(-1),
            offlineUseExpiresAtUtc: FreshNow.AddDays(30));

        var result = manifest.Validate(FreshNow);

        Assert.True(result.IsValid, FormatIssues(result));
        Assert.True(result.HasWarnings);
        Assert.Equal(HonuaScenePackageState.Stale, result.State);
        AssertHasCode(result, HonuaScenePackageValidationCodes.Stale);
    }

    private static HonuaScenePackageManifest CreateManifest(
        string? schemaVersion = null,
        string? sceneId = null,
        DateTimeOffset? staleAfterUtc = null,
        DateTimeOffset? offlineUseExpiresAtUtc = null,
        HonuaSceneBounds? extent = null,
        HonuaScenePackageLod? lod = null,
        HonuaScenePackageByteBudget? byteBudget = null,
        IReadOnlyList<HonuaScenePackageAsset>? assets = null)
        => new()
        {
            SchemaVersion = schemaVersion ?? HonuaScenePackageManifest.CurrentSchemaVersion,
            PackageId = "pkg_downtown_honolulu_2026_04",
            SceneId = sceneId ?? "downtown-honolulu",
            DisplayName = "Downtown Honolulu 3D",
            EditionGate = HonuaScenePackageEditionGates.Pro,
            ServerRevision = "scene-rev-42",
            CreatedAtUtc = FreshNow.AddHours(-12),
            StaleAfterUtc = staleAfterUtc ?? FreshNow.AddDays(30),
            OfflineUseExpiresAtUtc = offlineUseExpiresAtUtc ?? FreshNow.AddDays(60),
            AuthExpiresAtUtc = FreshNow.AddDays(1),
            Extent = extent ?? new HonuaSceneBounds
            {
                MinLongitude = -157.872,
                MinLatitude = 21.293,
                MaxLongitude = -157.841,
                MaxLatitude = 21.319,
            },
            Lod = lod ?? new HonuaScenePackageLod
            {
                MinZoom = 12,
                MaxZoom = 17,
                MaxGeometricErrorMeters = 4.0,
            },
            ByteBudget = byteBudget ?? new HonuaScenePackageByteBudget
            {
                MaxPackageBytes = 2_147_483_648,
                DeclaredBytes = 1_000_000,
            },
            Attribution = new[] { "Honua", "City and County source data" },
            Assets = assets ?? ValidAssets(),
        };

    private static IReadOnlyList<HonuaScenePackageAsset> ValidAssets()
        => new[]
        {
            new HonuaScenePackageAsset
            {
                Key = "scene-metadata",
                Type = HonuaScenePackageAssetTypes.SceneMetadata,
                Role = "metadata",
                Path = "metadata/scene.json",
                ContentType = "application/json",
                Bytes = 4_832,
                Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                ETag = "\"scene-42\"",
                Required = true,
            },
            new HonuaScenePackageAsset
            {
                Key = "buildings-tileset",
                Type = HonuaScenePackageAssetTypes.ThreeDimensionalTileset,
                Role = "primary-tileset",
                Path = "tilesets/buildings/tileset.json",
                ContentType = "application/json",
                Bytes = 10_455,
                Sha256 = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
                Required = true,
            },
        };

    private static void AssertHasCode(HonuaScenePackageValidationResult result, string code)
        => Assert.Contains(result.Issues, issue => issue.Code == code);

    private static string FormatIssues(HonuaScenePackageValidationResult result)
        => string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string ReadFixture(string name, [CallerFilePath] string sourceFile = "")
    {
        var testDirectory = Path.GetDirectoryName(sourceFile)
            ?? throw new InvalidOperationException("Unable to resolve test directory.");
        return File.ReadAllText(Path.Combine(testDirectory, "Fixtures", "Scenes", name));
    }
}
