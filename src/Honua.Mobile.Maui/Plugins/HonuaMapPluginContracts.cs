// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using Honua.Sdk.Abstractions.Plugins;

namespace Honua.Mobile.Maui.Plugins;

/// <summary>
/// MAUI-owned metadata for a runtime map plugin. This is not a portable plugin manifest.
/// </summary>
public sealed record HonuaMapPluginDescriptor
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public Version? Version { get; init; }

    public int Priority { get; init; }

    /// <summary>
    /// Optional SDK-owned manifest for host-neutral plugin metadata, permissions,
    /// compatibility, and non-UI extension declarations.
    /// </summary>
    public HonuaPluginManifest? SdkManifest { get; init; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);

        if (SdkManifest is null)
        {
            return;
        }

        if (!StringComparer.Ordinal.Equals(Id, SdkManifest.PluginId))
        {
            throw new InvalidOperationException(
                $"Map plugin descriptor id '{Id}' does not match SDK manifest plugin id '{SdkManifest.PluginId}'.");
        }

        var evaluation = SdkManifest.EvaluateForMobileHost();
        if (!evaluation.CanLoad)
        {
            throw new InvalidOperationException(
                $"SDK plugin manifest '{SdkManifest.PluginId}' is not valid for the mobile host: {evaluation.FormatIssues()}");
        }
    }
}

/// <summary>
/// Runtime map plugin extension point for MAUI hosts.
/// </summary>
public interface IHonuaMapPlugin
{
    HonuaMapPluginDescriptor Descriptor { get; }

    ValueTask ActivateAsync(IHonuaMapPluginContext context, CancellationToken ct = default);
}

/// <summary>
/// Host capabilities exposed to a map plugin while it activates.
/// </summary>
public interface IHonuaMapPluginContext
{
    IServiceProvider Services { get; }

    HonuaMapPluginDescriptor Plugin { get; }

    IReadOnlyList<HonuaPluginPermissionDeclaration> GrantedPermissions { get; }

    bool HasPermission(string permission, string access);

    void AddToolbarButton(HonuaMapPluginToolbarButton button);

    void AddUiExtension(HonuaMapPluginUiExtension extension);

    void AddFeatureRenderer(HonuaMapPluginFeatureRenderer renderer);
}

public enum HonuaMapPluginTrustState
{
    Approved,
    Untrusted,
    Revoked,
}

public sealed record HonuaMapPluginTrustEvaluation
{
    public HonuaMapPluginTrustState State { get; init; } = HonuaMapPluginTrustState.Approved;

    public string? Reason { get; init; }

    public bool CanLoad => State == HonuaMapPluginTrustState.Approved;

    public static HonuaMapPluginTrustEvaluation Approved()
        => new() { State = HonuaMapPluginTrustState.Approved };

    public static HonuaMapPluginTrustEvaluation Untrusted(string? reason = null)
        => new() { State = HonuaMapPluginTrustState.Untrusted, Reason = reason };

    public static HonuaMapPluginTrustEvaluation Revoked(string? reason = null)
        => new() { State = HonuaMapPluginTrustState.Revoked, Reason = reason };
}

public interface IHonuaMapPluginTrustService
{
    ValueTask<HonuaMapPluginTrustEvaluation> EvaluateTrustAsync(
        HonuaMapPluginDescriptor plugin,
        CancellationToken ct = default);
}

public sealed class LocalHonuaMapPluginTrustService : IHonuaMapPluginTrustService
{
    public ValueTask<HonuaMapPluginTrustEvaluation> EvaluateTrustAsync(
        HonuaMapPluginDescriptor plugin,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ct.ThrowIfCancellationRequested();

        return ValueTask.FromResult(HonuaMapPluginTrustEvaluation.Approved());
    }
}

public sealed record HonuaMapPluginPermissionRequest
{
    public required HonuaMapPluginDescriptor Plugin { get; init; }

    public required HonuaPluginPermissionDeclaration Permission { get; init; }
}

public sealed record HonuaMapPluginPermissionDecision
{
    public bool Granted { get; init; }

    public string? Reason { get; init; }

    public static HonuaMapPluginPermissionDecision Grant()
        => new() { Granted = true };

    public static HonuaMapPluginPermissionDecision Deny(string? reason = null)
        => new() { Granted = false, Reason = reason };
}

public interface IHonuaMapPluginPermissionService
{
    ValueTask<HonuaMapPluginPermissionDecision> RequestPermissionAsync(
        HonuaMapPluginPermissionRequest request,
        CancellationToken ct = default);
}

public sealed class DenyByDefaultHonuaMapPluginPermissionService : IHonuaMapPluginPermissionService
{
    public ValueTask<HonuaMapPluginPermissionDecision> RequestPermissionAsync(
        HonuaMapPluginPermissionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        return ValueTask.FromResult(HonuaMapPluginPermissionDecision.Deny(
            "No mobile plugin permission service approved this request."));
    }
}

public sealed record HonuaMapPluginActivationOptions
{
    public IReadOnlyList<string> DisabledPluginIds { get; init; } = [];
}

/// <summary>
/// Context passed when a host invokes a plugin-owned map command.
/// </summary>
public sealed record HonuaMapPluginCommandContext
{
    public required IServiceProvider Services { get; init; }

    public IReadOnlyDictionary<string, object?> Properties { get; init; } =
        new Dictionary<string, object?>();
}

/// <summary>
/// Toolbar command contributed by a map plugin.
/// </summary>
public sealed record HonuaMapPluginToolbarButton
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Icon { get; init; }

    public int Order { get; init; }

    public required Func<HonuaMapPluginCommandContext, CancellationToken, ValueTask> ExecuteAsync { get; init; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        ArgumentNullException.ThrowIfNull(ExecuteAsync);
    }
}

public enum HonuaMapPluginUiExtensionKind
{
    Panel,
    FloatingWidget,
    Dialog,
    Form,
    WorkflowScreen,
}

/// <summary>
/// UI surface contributed by a map plugin for the MAUI host to mount.
/// </summary>
public sealed record HonuaMapPluginUiExtension
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public HonuaMapPluginUiExtensionKind Kind { get; init; } = HonuaMapPluginUiExtensionKind.Panel;

    public Type? ViewType { get; init; }

    public Type? ViewModelType { get; init; }

    public string? Outlet { get; init; }

    public int Order { get; init; }

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
    }
}

/// <summary>
/// Feature renderer adapter contributed by a map plugin for a native map host.
/// </summary>
public sealed record HonuaMapPluginFeatureRenderer
{
    public required string Id { get; init; }

    public required Type RendererType { get; init; }

    public string? LayerId { get; init; }

    public int Order { get; init; }

    public IReadOnlyDictionary<string, object?> RendererHints { get; init; } =
        new Dictionary<string, object?>();

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentNullException.ThrowIfNull(RendererType);
    }
}
