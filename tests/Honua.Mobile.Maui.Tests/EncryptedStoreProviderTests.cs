// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using Honua.Mobile.Offline.GeoPackage;
using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Maui.Tests;

public sealed class EncryptedStoreProviderTests
{
    [Fact]
    public async Task PlaintextProvider_RejectsEncryptionBeforeWritingSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"honua-provider-{Guid.NewGuid():N}.gpkg");
        try
        {
            var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
            {
                DatabasePath = path,
                EncryptionKey = "test-only-key",
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
            Assert.True(!File.Exists(path) || new FileInfo(path).Length == 0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
