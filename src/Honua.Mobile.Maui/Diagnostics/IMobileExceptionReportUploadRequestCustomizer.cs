// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Allows mobile apps to add approved request metadata, such as same-origin auth headers,
/// before a sanitized exception report upload is sent.
/// </summary>
public interface IMobileExceptionReportUploadRequestCustomizer
{
    void Customize(HttpRequestMessage request, MobileExceptionReport report);
}
