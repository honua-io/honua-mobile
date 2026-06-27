// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Transport boundary for sending sanitized mobile exception reports to an app-configured ingestion point.
/// </summary>
public interface IMobileExceptionReportUploader
{
    Task<bool> UploadAsync(MobileExceptionReport report, CancellationToken cancellationToken = default);
}
