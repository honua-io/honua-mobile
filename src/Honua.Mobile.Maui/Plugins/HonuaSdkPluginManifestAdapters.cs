// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using Honua.Sdk.Abstractions.Plugins;

namespace Honua.Mobile.Maui.Plugins;

/// <summary>
/// Mobile-host preflight result for an SDK-owned plugin manifest.
/// </summary>
public sealed record HonuaMobilePluginManifestEvaluation
{
    public required HonuaPluginManifest Manifest { get; init; }

    public required HonuaPluginValidationResult SdkValidation { get; init; }

    public IReadOnlyList<string> HostCompatibilityIssues { get; init; } = [];

    public bool CanLoad => SdkValidation.IsValid && HostCompatibilityIssues.Count == 0;

    public string FormatIssues()
    {
        var sdkIssues = SdkValidation.Issues
            .Where(issue => issue.Severity == HonuaPluginValidationSeverity.Error)
            .Select(issue => string.IsNullOrWhiteSpace(issue.Path)
                ? $"{issue.Code}: {issue.Message}"
                : $"{issue.Code} at {issue.Path}: {issue.Message}");

        return string.Join("; ", sdkIssues.Concat(HostCompatibilityIssues));
    }
}

/// <summary>
/// Adapters from SDK-owned plugin manifests into MAUI host/runtime descriptors.
/// </summary>
public static class HonuaSdkPluginManifestAdapters
{
    public static HonuaMobilePluginManifestEvaluation EvaluateForMobileHost(this HonuaPluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var validation = manifest.Validate();
        var hostIssues = new List<string>();
        var supportedHosts = manifest.Compatibility?.SupportedHosts ?? [];

        if (supportedHosts.Count > 0 &&
            !supportedHosts.Contains(HonuaPluginHostKinds.Mobile, StringComparer.Ordinal))
        {
            hostIssues.Add(
                $"manifest compatibility does not include the '{HonuaPluginHostKinds.Mobile}' host kind");
        }

        return new HonuaMobilePluginManifestEvaluation
        {
            Manifest = manifest,
            SdkValidation = validation,
            HostCompatibilityIssues = hostIssues.ToArray(),
        };
    }

    public static HonuaMapPluginDescriptor ToMapPluginDescriptor(
        this HonuaPluginManifest manifest,
        int priority = 0)
    {
        var evaluation = manifest.EvaluateForMobileHost();
        if (!evaluation.CanLoad)
        {
            throw new InvalidOperationException(
                $"SDK plugin manifest '{manifest.PluginId}' is not valid for the mobile host: {evaluation.FormatIssues()}");
        }

        return new HonuaMapPluginDescriptor
        {
            Id = manifest.PluginId ?? string.Empty,
            DisplayName = manifest.DisplayName ?? string.Empty,
            Version = TryParseVersion(manifest.Version),
            Priority = priority,
            SdkManifest = manifest,
        };
    }

    private static Version? TryParseVersion(string? value)
        => Version.TryParse(value, out var version) ? version : null;
}
