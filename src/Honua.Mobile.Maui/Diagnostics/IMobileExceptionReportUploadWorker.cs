// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Drains locally queued mobile exception reports without blocking app startup or foreground workflows.
/// </summary>
public interface IMobileExceptionReportUploadWorker
{
    Task FlushPendingAsync(CancellationToken cancellationToken = default);
}
