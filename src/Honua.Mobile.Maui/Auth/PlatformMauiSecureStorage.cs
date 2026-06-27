// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using Microsoft.Maui.Storage;

namespace Honua.Mobile.Maui.Auth;

/// <summary>
/// Platform secure-storage adapter backed by MAUI Essentials. iOS uses Keychain and Android uses
/// encrypted SharedPreferences protected by the Android Keystore provider.
/// </summary>
public sealed class PlatformMauiSecureStorage : IMauiSecureStorage
{
    /// <inheritdoc />
    public async ValueTask<string?> GetAsync(string key, CancellationToken ct = default)
        => await SecureStorage.Default.GetAsync(key).WaitAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask SetAsync(string key, string value, CancellationToken ct = default)
        => await SecureStorage.Default.SetAsync(key, value).WaitAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(key);
        return ValueTask.CompletedTask;
    }
}
