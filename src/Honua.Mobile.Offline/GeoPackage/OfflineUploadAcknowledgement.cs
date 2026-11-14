// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

namespace Honua.Mobile.Offline.GeoPackage;

/// <summary>A durable upload result retained after the queued operation is removed.</summary>
public sealed record OfflineUploadAcknowledgement
{
    /// <summary>The unique queued operation identifier.</summary>
    public required string OperationId { get; init; }

    /// <summary>The local layer key of the uploaded operation.</summary>
    public required string LayerKey { get; init; }

    /// <summary>The uploaded operation's target collection.</summary>
    public required string TargetCollection { get; init; }

    /// <summary>Server identifiers in edit-result order; empty when the server supplied none.</summary>
    public required IReadOnlyList<string> ServerFeatureIds { get; init; }

    /// <summary>When the upload result was committed locally.</summary>
    public required DateTimeOffset UploadedAtUtc { get; init; }
}
