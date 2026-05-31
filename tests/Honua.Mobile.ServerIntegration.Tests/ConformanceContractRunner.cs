// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Text.Json;
using Honua.Mobile.Sdk;
using Xunit;

namespace Honua.Mobile.ServerIntegration.Tests;

/// <summary>
/// Executes a Compatibility-Train contract conformance check against a live
/// response and applies the <b>known-tracked-xfail</b> policy: a structural or
/// transport drift that matches a registered <see cref="KnownServerGaps"/> entry
/// is reported as a visible skip (xfail) carrying the tracking issue, so the
/// <c>Live Server Integration</c> job stays green while the harness is wired; any
/// new / untracked drift fails the test.
/// </summary>
/// <remarks>
/// The runner is the single choke point that turns an opaque
/// <see cref="HonuaMobileApiException"/> (e.g. the <c>honua-server#1238</c>
/// 400/500) into a <see cref="ContractDriftException"/> attributed to a named
/// <c>geospatial.v1</c> contract, so triage reads as
/// "<c>FeatureService.QueryFeatures response contract drift</c>" rather than a
/// bare HTTP error.
/// </remarks>
public static class ConformanceContractRunner
{
    /// <summary>
    /// Runs <paramref name="check"/> (a contract assertion that throws
    /// <see cref="ContractDriftException"/> on drift), wrapping a live transport
    /// failure for <paramref name="transportContract"/> into attributed drift,
    /// then applies the known-gap xfail policy.
    /// </summary>
    /// <param name="transportContract">
    /// The contract name to attribute a hard transport failure (HTTP 4xx/5xx) to,
    /// for the case where the live call throws before any body can be validated.
    /// </param>
    /// <param name="check">The conformance assertion to execute.</param>
    public static async Task RunAsync(string transportContract, Func<Task> check)
    {
        ArgumentNullException.ThrowIfNull(check);

        ContractDriftException? drift = null;
        try
        {
            await check().ConfigureAwait(false);
        }
        catch (ContractDriftException ex)
        {
            drift = ex;
        }
        catch (HonuaMobileApiException ex)
        {
            // A hard transport failure (the #1238 400/500 class) — attribute it to
            // the named contract and the HTTP status so it can be matched against a
            // tracked gap instead of surfacing as a raw HTTP error.
            drift = new ContractDriftException(
                transportContract,
                $"live request failed with HTTP {(int)ex.StatusCode} ({ex.StatusCode}) before a "
                + $"conforming response body could be validated. Server detail: {Summarize(ex)}",
                transportStatus: ex.StatusCode,
                inner: ex);
        }

        if (drift is null)
        {
            return;
        }

        ApplyPolicy(drift);
    }

    /// <summary>
    /// Synchronous overload for validating an already-materialized response.
    /// </summary>
    public static void Run(string transportContract, Action check)
    {
        ArgumentNullException.ThrowIfNull(check);
        RunAsync(transportContract, () =>
        {
            check();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    private static void ApplyPolicy(ContractDriftException drift)
    {
        var gap = KnownServerGaps.Match(drift);
        if (gap is not null)
        {
            // KNOWN, ALREADY-TRACKED gap: mark known-expected-failing (xfail) with an
            // explicit issue reference. Visible in the test report, never silent.
            Skip.If(
                true,
                $"KNOWN-EXPECTED-FAILING ({gap.Issue}): {gap.Summary}. "
                + $"Tracked at {gap.IssueUrl}. Attributed drift: {drift.Message} "
                + "When the server fix lands, remove this gap from KnownServerGaps so the xfail flips to required.");
            return;
        }

        // NEW / UNTRACKED drift: fail with the contract-attributed message so a
        // future regression is pinned to a named contract + field, not a bare error.
        throw new ContractConformanceException(drift);
    }

    private static string Summarize(HonuaMobileApiException ex)
    {
        var body = ex.ResponseBody;
        if (string.IsNullOrWhiteSpace(body))
        {
            return ex.Message;
        }

        body = body.Trim();
        const int max = 400;
        return body.Length <= max ? body : body[..max] + "…";
    }
}

/// <summary>
/// Failure raised for an untracked contract drift. Wraps the attributed
/// <see cref="ContractDriftException"/> so the assertion failure clearly names the
/// drifted contract and field (and is not mistaken for an infrastructure error).
/// </summary>
public sealed class ContractConformanceException : Xunit.Sdk.XunitException
{
    public ContractConformanceException(ContractDriftException drift)
        : base(BuildMessage(drift), drift)
    {
    }

    private static string BuildMessage(ContractDriftException drift)
        => "UNTRACKED contract drift detected by the Compatibility-Train conformance check. "
           + $"{drift.Message} "
           + "This is a NEW drift (no matching KnownServerGaps entry). If it is a real, "
           + "expected server gap, file a honua-server issue and register it in KnownServerGaps "
           + "with an explicit issue reference; do not silence it.";
}
