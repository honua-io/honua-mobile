// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

namespace Honua.Mobile.Offline.GeoPackage;

public sealed record OfflineUploadAcknowledgement
{
    public required string OperationId { get; init; }

    public required string LayerKey { get; init; }

    public required string TargetCollection { get; init; }

    public required IReadOnlyList<string> ServerFeatureIds { get; init; }

    public required DateTimeOffset UploadedAtUtc { get; init; }
}
