// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Honua.Mobile.ServerIntegration.Tests;

/// <summary>
/// Loads and exposes the shared, versioned <c>geospatial-grpc</c> conformance
/// fixtures (Compatibility Train, epic <c>geospatial-grpc#18</c>) and validates
/// live <c>honua-server</c> FeatureServer/OGC responses against the canonical
/// <c>geospatial.v1</c> contracts they describe.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures are <b>not vendored</b>. CI pulls a pinned release artifact with
/// <c>tests/conformance/fetch-fixtures.sh</c> and points the suite at the
/// extracted directory via the <c>HONUA_MOBILE_CONFORMANCE_FIXTURES_DIR</c>
/// environment variable. Provenance is recorded in
/// <c>tests/conformance/UPSTREAM.md</c>.
/// </para>
/// <para>
/// The canonical fixtures use the protobuf JSON mapping (camelCase fields, enum
/// names as strings, <c>int64</c> as strings) — see
/// <c>geospatial-grpc/conformance/README.md</c>. The mobile-relevant subset is
/// <c>FeatureService.QueryFeatures</c> / <c>FeatureService.ApplyEdits</c> and the
/// OGC Features read/CRUD paths exercised against the <c>mobile_offline_demo</c>
/// seed (layer <c>68910</c>).
/// </para>
/// <para>
/// When a live response does not conform, the assertion fails with a message that
/// names the drifted contract and the specific field, so a future
/// <c>honua-server#1238</c>-class regression reads as
/// "<c>FeatureService.QueryFeatures response contract drift on field X</c>"
/// rather than a bare HTTP 400/500 or a generic <c>JsonValueKind</c> mismatch.
/// </para>
/// </remarks>
public sealed class ConformanceFixtures
{
    /// <summary>Environment variable naming the directory of fetched, verified fixtures.</summary>
    public const string FixturesDirEnvVar = "HONUA_MOBILE_CONFORMANCE_FIXTURES_DIR";

    private static readonly Lazy<ConformanceFixtures> LazyInstance = new(Load);

    private ConformanceFixtures(
        string? root,
        string? version,
        IReadOnlyDictionary<string, JsonDocument> fixtures,
        string? unavailableReason)
    {
        Root = root;
        Version = version;
        _fixtures = fixtures;
        UnavailableReason = unavailableReason;
    }

    private readonly IReadOnlyDictionary<string, JsonDocument> _fixtures;

    /// <summary>Shared instance loaded once per test run.</summary>
    public static ConformanceFixtures Instance => LazyInstance.Value;

    /// <summary>Directory the fixtures were loaded from, or <c>null</c> when unavailable.</summary>
    public string? Root { get; }

    /// <summary>Pinned fixture-set version (the bundle's <c>VERSION</c>), or <c>null</c>.</summary>
    public string? Version { get; }

    /// <summary><c>true</c> when fixtures were located and loaded.</summary>
    public bool Available => Root is not null && _fixtures.Count > 0;

    /// <summary>Human-readable reason the fixtures are unavailable (for <c>Skip.If</c>).</summary>
    public string? UnavailableReason { get; }

    /// <summary>Canonical <c>geospatial.v1.QueryFeaturesResponse</c> fixture.</summary>
    public JsonElement FeatureQueryResponse => Fixture("feature_query_response.json").RootElement;

    /// <summary>Canonical <c>geospatial.v1.ApplyEditsResponse</c> fixture.</summary>
    public JsonElement FeatureApplyEditsResponse => Fixture("feature_apply_edits_response.json").RootElement;

    private JsonDocument Fixture(string name)
        => _fixtures.TryGetValue(name, out var doc)
            ? doc
            : throw new InvalidOperationException(
                $"Conformance fixture '{name}' was not found under '{Root}'. "
                + "The fetched fixture bundle is incomplete; check tests/conformance/fetch-fixtures.sh output.");

    private static ConformanceFixtures Load()
    {
        var root = Environment.GetEnvironmentVariable(FixturesDirEnvVar);
        if (string.IsNullOrWhiteSpace(root))
        {
            return Unavailable(
                $"{FixturesDirEnvVar} not set; run tests/conformance/fetch-fixtures.sh "
                + "and point it at the extracted fixtures directory.");
        }

        root = root.Trim();

        // Accept either the bundle root (contains fixtures/, golden/, VERSION) or
        // its parent containing the conformance-fixtures-<version>/ directory.
        var fixturesDir = Path.Combine(root, "fixtures");
        if (!Directory.Exists(fixturesDir))
        {
            var nested = Directory.Exists(root)
                ? Directory.GetDirectories(root, "conformance-fixtures-*").FirstOrDefault()
                : null;
            if (nested is not null && Directory.Exists(Path.Combine(nested, "fixtures")))
            {
                root = nested;
                fixturesDir = Path.Combine(root, "fixtures");
            }
        }

        if (!Directory.Exists(fixturesDir))
        {
            return Unavailable(
                $"{FixturesDirEnvVar}='{root}' does not contain a fixtures/ directory; "
                + "the fixture bundle was not fetched/extracted correctly.");
        }

        string? version = null;
        var versionFile = Path.Combine(root, "VERSION");
        if (File.Exists(versionFile))
        {
            version = File.ReadAllText(versionFile).Trim();
        }

        var fixtures = new Dictionary<string, JsonDocument>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(fixturesDir, "*.json"))
        {
            try
            {
                fixtures[Path.GetFileName(file)] = JsonDocument.Parse(File.ReadAllBytes(file));
            }
            catch (JsonException ex)
            {
                return Unavailable($"conformance fixture '{file}' is not valid JSON: {ex.Message}");
            }
        }

        if (fixtures.Count == 0)
        {
            return Unavailable($"no conformance fixtures found under '{fixturesDir}'.");
        }

        return new ConformanceFixtures(root, version, fixtures, unavailableReason: null);
    }

    private static ConformanceFixtures Unavailable(string reason)
        => new(root: null, version: null, new Dictionary<string, JsonDocument>(StringComparer.Ordinal), reason);
}
