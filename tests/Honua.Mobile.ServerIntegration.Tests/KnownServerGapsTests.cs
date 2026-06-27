// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Net;
using Xunit;

namespace Honua.Mobile.ServerIntegration.Tests;

/// <summary>
/// Unit coverage for the <see cref="KnownServerGaps"/> matcher policy. These assert
/// the regression-detection guarantee: the honua-server#1238 core-read xfail only
/// swallows drifts that carry the JSONB-projection error signature, so an unrelated
/// 400/500 on the same read paths still fails the hard gate.
/// </summary>
public sealed class KnownServerGapsTests
{
    private const string Jsonb1238Detail =
        "live request failed with HTTP 400 (BadRequest) before a conforming response body "
        + "could be validated. Server detail: {\"error\":{\"code\":400,\"message\":\"42703: "
        + "column \\\"globalid\\\" does not exist\"}}";

    [Fact]
    public void Match_FeatureQuery400_WithJsonbSignature_ReturnsTrackedGap()
    {
        var drift = new ContractDriftException(
            FeatureContractConformance.FeatureQueryContract,
            Jsonb1238Detail,
            transportStatus: HttpStatusCode.BadRequest);

        var gap = KnownServerGaps.Match(drift);

        Assert.NotNull(gap);
        Assert.Equal("honua-server#1238", gap!.Issue);
    }

    [Fact]
    public void Match_OgcItems500_WithJsonbSignature_ReturnsTrackedGap()
    {
        var drift = new ContractDriftException(
            FeatureContractConformance.OgcItemsContract,
            "Server detail: column \"globalid\" does not exist",
            transportStatus: HttpStatusCode.InternalServerError);

        var gap = KnownServerGaps.Match(drift);

        Assert.NotNull(gap);
        Assert.Equal("honua-server#1238", gap!.Issue);
    }

    [Fact]
    public void Match_FeatureQuery400_WithoutJsonbSignature_DoesNotMatch()
    {
        // An unrelated 400 on the core read path (e.g. a malformed where-clause
        // regression) must NOT be swallowed by the #1238 gap.
        var drift = new ContractDriftException(
            FeatureContractConformance.FeatureQueryContract,
            "live request failed with HTTP 400 (BadRequest). Server detail: "
            + "{\"error\":{\"code\":400,\"message\":\"Invalid where clause\"}}",
            transportStatus: HttpStatusCode.BadRequest);

        Assert.Null(KnownServerGaps.Match(drift));
    }

    [Fact]
    public void Match_OgcItems500_WithoutJsonbSignature_DoesNotMatch()
    {
        var drift = new ContractDriftException(
            FeatureContractConformance.OgcItemsContract,
            "live request failed with HTTP 500 (InternalServerError). Server detail: "
            + "{\"error\":\"NullReferenceException in projection\"}",
            transportStatus: HttpStatusCode.InternalServerError);

        Assert.Null(KnownServerGaps.Match(drift));
    }

    [Fact]
    public void FeatureServerOgcJsonbProjection_IsTrackedAsCoreReadGap()
    {
        Assert.Contains(KnownServerGaps.FeatureServerOgcJsonbProjection, KnownServerGaps.CoreReadGaps);
    }
}
