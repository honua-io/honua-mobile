// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using Honua.Mobile.Sdk;

namespace Honua.Mobile.ServerIntegration.Tests;

/// <summary>
/// Unit coverage for the Compatibility-Train conformance harness itself: the
/// contract validators, the known-tracked-xfail policy, and the attribution of an
/// opaque HTTP failure to a named contract. These run without Docker so they guard
/// the harness on every PR (not only on a live run), proving the checks are
/// effective — conforming responses pass, tracked drift xfails (visibly,
/// attributed), and new/untracked drift fails.
/// </summary>
public sealed class ConformanceContractTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void FeatureQuery_conforming_response_passes()
    {
        var live = Parse(
            """
            {
              "objectIdFieldName": "OBJECTID",
              "fields": [{ "name": "globalid", "type": "esriFieldTypeString" }],
              "features": [
                { "attributes": { "globalid": "abc", "site_name": "Site" }, "geometry": { "x": -158, "y": 21 } }
              ]
            }
            """);

        FeatureContractConformance.AssertFeatureQueryResponse(live, canonical: default, requireFeature: true);
    }

    [Fact]
    public void FeatureQuery_missing_features_is_attributed_drift()
    {
        var live = Parse("""{ "objectIdFieldName": "OBJECTID" }""");

        var ex = Assert.Throws<ContractDriftException>(
            () => FeatureContractConformance.AssertFeatureQueryResponse(live, canonical: default, requireFeature: true));

        Assert.Equal(FeatureContractConformance.FeatureQueryContract, ex.Contract);
        Assert.Contains("features", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FeatureQuery_feature_missing_attributes_names_the_field()
    {
        var live = Parse("""{ "features": [ { "geometry": { "x": 1, "y": 2 } } ] }""");

        var ex = Assert.Throws<ContractDriftException>(
            () => FeatureContractConformance.AssertFeatureQueryResponse(live, canonical: default, requireFeature: true));

        Assert.Contains("attributes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OgcItems_non_feature_collection_is_attributed_drift()
    {
        var live = Parse("""{ "type": "Something", "features": [] }""");

        var ex = Assert.Throws<ContractDriftException>(
            () => FeatureContractConformance.AssertOgcItemsResponse(live, requireFeature: false));

        Assert.Equal(FeatureContractConformance.OgcItemsContract, ex.Contract);
        Assert.Contains("FeatureCollection", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyEdits_missing_all_result_arrays_is_attributed_drift()
    {
        var live = Parse("""{ "somethingElse": true }""");

        var ex = Assert.Throws<ContractDriftException>(
            () => FeatureContractConformance.AssertApplyEditsResponse(live));

        Assert.Equal(FeatureContractConformance.FeatureApplyEditsContract, ex.Contract);
    }

    [Fact]
    public void KnownGaps_match_1238_featureserver_400_and_ogc_500()
    {
        var featureServer = new ContractDriftException(
            FeatureContractConformance.FeatureQueryContract,
            "live request failed with HTTP 400",
            transportStatus: HttpStatusCode.BadRequest);
        var ogc = new ContractDriftException(
            FeatureContractConformance.OgcItemsContract,
            "live request failed with HTTP 500",
            transportStatus: HttpStatusCode.InternalServerError);

        Assert.Equal("honua-server#1238", KnownServerGaps.Match(featureServer)?.Issue);
        Assert.Equal("honua-server#1238", KnownServerGaps.Match(ogc)?.Issue);
    }

    [Fact]
    public void KnownGaps_do_not_match_unrelated_query_drift()
    {
        // A structural drift in an otherwise-200 query response is NOT a tracked
        // gap (no transport status) and must surface as a real failure.
        var structuralDrift = new ContractDriftException(
            FeatureContractConformance.FeatureQueryContract,
            "missing required `features` array");

        Assert.Null(KnownServerGaps.Match(structuralDrift));
    }

    [Fact]
    public void Runner_xfails_tracked_transport_failure_with_attribution()
    {
        // A #1238-class transport failure becomes a visible Skip (xfail), not a
        // pass and not a hard failure.
        var skip = Assert.Throws<Xunit.SkipException>(() =>
            ConformanceContractRunner.Run(
                FeatureContractConformance.FeatureQueryContract,
                () => throw new HonuaMobileApiException(
                    HttpStatusCode.BadRequest,
                    "Bad Request",
                    """{"error":{"code":400,"message":"Invalid query parameters"}}""")));

        Assert.Contains("KNOWN-EXPECTED-FAILING", skip.Message, StringComparison.Ordinal);
        Assert.Contains("honua-server#1238", skip.Message, StringComparison.Ordinal);
        Assert.Contains(FeatureContractConformance.FeatureQueryContract, skip.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_fails_untracked_structural_drift()
    {
        // New/untracked drift must FAIL (the regression-detection guarantee), with
        // a message that names the contract — not a silent pass.
        var ex = Assert.Throws<ContractConformanceException>(() =>
            ConformanceContractRunner.Run(
                FeatureContractConformance.FeatureQueryContract,
                () => throw new ContractDriftException(
                    FeatureContractConformance.FeatureQueryContract,
                    "`features[0].attributes` must be an object")));

        Assert.Contains("UNTRACKED", ex.Message, StringComparison.Ordinal);
        Assert.Contains(FeatureContractConformance.FeatureQueryContract, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_fails_untracked_transport_failure()
    {
        // An unexpected transport status (e.g. 404 on a query) is not a tracked gap
        // and must fail, attributed to the contract.
        var ex = Assert.Throws<ContractConformanceException>(() =>
            ConformanceContractRunner.Run(
                FeatureContractConformance.FeatureQueryContract,
                () => throw new HonuaMobileApiException(HttpStatusCode.NotFound, "Not Found")));

        Assert.Contains("UNTRACKED", ex.Message, StringComparison.Ordinal);
        Assert.Contains("404", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_passes_conforming_response()
    {
        // No throw == conforming == pass.
        ConformanceContractRunner.Run(
            FeatureContractConformance.FeatureQueryContract,
            () => FeatureContractConformance.AssertFeatureQueryResponse(
                Parse("""{ "objectIdFieldName": "OBJECTID", "features": [ { "attributes": {} } ] }"""),
                canonical: default,
                requireFeature: true));
    }
}
