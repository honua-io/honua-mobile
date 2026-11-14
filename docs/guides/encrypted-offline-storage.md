# Encrypted offline storage

`Honua.Mobile.Offline` uses `Microsoft.Data.Sqlite.Core`. The application must
supply exactly one native SQLite provider. For unencrypted storage, reference
`Microsoft.Data.Sqlite` and the repository's native-library version pin. For
SQLCipher storage, reference `SQLitePCLRaw.bundle_e_sqlcipher` instead; do not
also reference a plaintext SQLite bundle. This follows the
[Microsoft.Data.Sqlite encryption configuration](https://learn.microsoft.com/dotnet/standard/data/sqlite/encryption).

Obtain a stable key from the platform's secure key store before creating the
store. Set `GeoPackageSyncStoreOptions.EncryptionKey` to that key. Every keyed
connection checks SQLCipher support before schema access. Missing providers,
unsupported encryption, and incorrect keys fail rather than retrying without
encryption. Existing unencrypted databases are not automatically converted.

The application owns key generation, secure persistence, rotation, recovery and
sign-out policy. Do not put keys in settings files, logs or sync payloads. Losing
the key can make unsynced observations unrecoverable; a replacement key must not
silently replace or delete the old database. Plan and verify a separate migration
for existing databases. Database encryption does not encrypt separately stored
attachment files or exported data.

The desktop tests verify encrypted queue reopen, non-plaintext database contents,
wrong/missing-key rejection and rejection by a plaintext provider. Before adopting
this store in Collect and removing the duplicate store, run on Android and iOS:

1. Install the paired Collect build and record its package version and device OS.
2. Disconnect, save a field observation and attachment, terminate, and reopen.
3. Verify the same observation and pending edit survive with the persisted key.
4. Reconnect and confirm one upload, with no duplicate queue entry.
5. Exercise key-store failure and recovery without deleting pending data.
6. Verify exported data and attachment protection follow the application's policy.

Record installable build locations and the journey results with the paired Collect
change. Desktop success does not establish native provider or key-store behavior.
