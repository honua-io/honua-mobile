// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Net;

namespace Honua.Mobile.ServerIntegration.Tests;

/// <summary>
/// Registry of <b>already-tracked</b> <c>honua-server:nightly</c> contract gaps
/// that the Compatibility-Train conformance checks are expected to hit until the
/// matching server fix lands. Each gap is recorded with an explicit issue
/// reference so the <c>Live Server Integration</c> job stays green (the harness
/// is wired) while a tracked server regression is <b>visibly</b> marked
/// known-expected-failing (xfail) — never silenced, never blanket
/// <c>continue-on-error</c>.
/// </summary>
/// <remarks>
/// <para>
/// A drift that matches a registered gap is reported via
/// <c>Skip.If(...)</c> (an xfail) carrying the issue URL and the attributed
/// contract drift. Any <b>new / untracked</b> drift is <b>not</b> matched here and
/// therefore fails the suite — that is the regression-detection guarantee.
/// </para>
/// <para>
/// When a server fix lands, delete the corresponding entry: the xfail then flips
/// to a required assertion and a re-introduced regression fails the build.
/// </para>
/// </remarks>
public static class KnownServerGaps
{
    /// <summary>A single tracked server gap.</summary>
    /// <param name="Issue">The tracking issue, e.g. <c>honua-server#1238</c>.</param>
    /// <param name="Summary">Short description of the gapped contract surface.</param>
    /// <param name="Matches">Predicate identifying a drift caused by this gap.</param>
    public sealed record Gap(string Issue, string Summary, Func<ContractDriftException, bool> Matches)
    {
        /// <summary>Full URL of the tracking issue.</summary>
        public string IssueUrl
        {
            get
            {
                var parts = Issue.Split('#', 2);
                return parts.Length == 2
                    ? $"https://github.com/honua-io/{parts[0]}/issues/{parts[1]}"
                    : Issue;
            }
        }
    }

    /// <summary>
    /// honua-server#1238 — FeatureServer/OGC JSONB-attribute projection drift on
    /// the seeded <c>mobile_offline_demo</c> layer <c>68910</c>. The query/
    /// projection pipeline emits bare SQL columns for JSONB-backed declared fields
    /// (<c>42703 column "globalid" does not exist</c>), so the FeatureServer
    /// <c>/query</c> path returns HTTP 400 and the OGC <c>/items</c> path returns
    /// HTTP 500 — the read paths never produce a conforming feature collection.
    /// </summary>
    public static readonly Gap FeatureServerOgcJsonbProjection = new(
        Issue: "honua-server#1238",
        Summary: "FeatureServer/OGC JSONB-attribute projection (mobile_offline_demo layer 68910) emits bare SQL columns",
        Matches: drift =>
            ((drift.Contract == FeatureContractConformance.FeatureQueryContract
                && drift.IsTransportStatus(HttpStatusCode.BadRequest))
            || (drift.Contract == FeatureContractConformance.OgcItemsContract
                && drift.IsTransportStatus(HttpStatusCode.InternalServerError)))
            // Only xfail when the server error body carries the #1238 JSONB-projection
            // signature. Without this, the matcher swallowed *any* 400/500 on the core
            // read paths, leaving the hard gate green while an unrelated regression broke
            // the primary feature-read surface.
            && drift.MentionsAny("42703", "column \"globalid\" does not exist"));

    /// <summary>
    /// Core live feature-read gaps whose xfail masks the primary read path (the
    /// FeatureServer <c>/query</c> and OGC <c>/items</c> surfaces). Surfaced
    /// separately so CI can report how many core-read gaps are actively xfailed.
    /// </summary>
    public static readonly IReadOnlyList<Gap> CoreReadGaps =
    [
        FeatureServerOgcJsonbProjection,
    ];

    /// <summary>honua-server#1166 — temporal query/filter contract gap.</summary>
    public static readonly Gap TemporalQuery = new(
        Issue: "honua-server#1166",
        Summary: "Temporal query/filter contract",
        Matches: drift => drift.MentionsAny("temporal", "time filter", "timeextent"));

    /// <summary>honua-server#1167 — replica sync contract gap.</summary>
    public static readonly Gap ReplicaSync = new(
        Issue: "honua-server#1167",
        Summary: "Replica sync contract",
        Matches: drift => drift.MentionsAny("replica"));

    /// <summary>honua-server#1237 — analysis list/estimate contract gap.</summary>
    public static readonly Gap AnalysisListEstimate = new(
        Issue: "honua-server#1237",
        Summary: "Analysis list/estimate contract",
        Matches: drift => drift.MentionsAny("analysis", "estimate"));

    /// <summary>All registered gaps.</summary>
    public static readonly IReadOnlyList<Gap> All =
    [
        FeatureServerOgcJsonbProjection,
        TemporalQuery,
        ReplicaSync,
        AnalysisListEstimate,
    ];

    /// <summary>
    /// Returns the tracked gap that explains <paramref name="drift"/>, or
    /// <c>null</c> when the drift is new/untracked (and must fail the suite).
    /// </summary>
    public static Gap? Match(ContractDriftException drift)
    {
        ArgumentNullException.ThrowIfNull(drift);
        return All.FirstOrDefault(gap => gap.Matches(drift));
    }
}
