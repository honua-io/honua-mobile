// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Microsoft.Data.Sqlite;

var mode = args[0];
var databasePath = args[1];
var encrypted = mode == "sqlcipher";
var key = encrypted ? "package-smoke-disposable-key" : null;
const string feature = """{"attributes":{"OBJECTID":42,"name":"package smoke feature"}}""";

GeoPackageSyncStore Open(string? password) => new(new GeoPackageSyncStoreOptions
{
    DatabasePath = databasePath,
    EncryptionKey = password,
});

SQLitePCL.Batteries_V2.Init();
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA cipher_version;";
    Require((await command.ExecuteScalarAsync() is string { Length: > 0 }) == encrypted,
        "Unexpected native SQLite provider.");
}

var store = Open(key);
await store.InitializeAsync();
await store.UpsertFeatureAndEnqueueAsync(new OfflineEditOperation
{
    OperationId = "package-smoke-42",
    LayerKey = "assets",
    TargetCollection = "assets",
    OperationType = OfflineOperationType.Add,
    PayloadJson = feature,
}, feature);
SqliteConnection.ClearAllPools();

var bytes = await File.ReadAllBytesAsync(databasePath);
Require(bytes.AsSpan().StartsWith("SQLite format 3\0"u8) == !encrypted,
    "The persisted database header does not match the selected mode.");
if (encrypted)
{
    Require(bytes.AsSpan().IndexOf("package smoke feature"u8) < 0,
        "The encrypted database contains the plaintext feature.");
    try
    {
        await Open("invalid-package-smoke-key").InitializeAsync();
        throw new InvalidOperationException("The database opened with the wrong key.");
    }
    catch (SqliteException)
    {
        // Reopen with the correct key below to verify the failed attempt preserved the data.
    }
}

var reopened = Open(key);
await reopened.InitializeAsync();
Require((await reopened.GetFeaturesAsync("assets")).Single() == feature,
    "The cached feature did not survive reopening.");
var pending = (await reopened.GetPendingAsync(10)).Single();
Require(pending.OperationId == "package-smoke-42" && pending.PayloadJson == feature,
    "The queued operation did not survive reopening.");
SqliteConnection.ClearAllPools();
Console.WriteLine($"Packaged offline storage round-trip passed ({mode}).");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
