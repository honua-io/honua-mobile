# Offline assigned work

`Honua.Mobile.Offline.Tasks` is a .NET 11 adapter for the shared task client in
`Honua.Sdk.Field`. It is an opt-in project while the task client awaits a
published package. The existing .NET 10 Mobile packages can be built and used
independently.

Initialize a SQLCipher-backed `GeoPackageSyncStore` with the app's durable
encryption key. Construct `OfflineAssignedWork` with that store, a
`WorkTaskClient` authenticated as the signed-in worker, and an
`AssignedWorkScope` built from the canonical server identity, stable
authenticated subject, service ID and exact assignee handle. Only reuse the
adapter while that identity and its credentials remain active.

- Run `RefreshAsync` online to replace the cached list after all pages succeed.
  `ReadAsync` reads the cached tasks and journal offline. The app can use the
  server's permitted actions and due dates when presenting the tasks.
- Use `RefreshWithAssetsAsync` with the same active account's authorized asset
  reader to cache asset summaries alongside the task list. Resolve the
  asset within the task's authoritative profile or layer, since an asset ID
  alone does not identify a record across profiles. A successful full
  refresh replaces the prior summaries. An incomplete refresh preserves the
  previous assignments and summaries, apart from explicitly denied assets.
  The asset reader must propagate transient failures and return null only
  when the asset is absent or inaccessible. A successful task-only
  `RefreshAsync` clears summaries because it does not reauthorize them.
- Read the pinned form ID and version from the cached task. The completion
  request must use that pin. Use `QueueCompletionWithAttachmentsAsync` to
  commit durable local file IDs alongside the request. The app owns the
  referenced files and their retention. These IDs remain in the encrypted
  command journal even if the assignment or asset becomes inaccessible.
  The current shared completion contract cannot upload these local IDs.
- Use `QueueTransitionAsync` or `QueueCompletionAsync` with a fresh command
  ID and the cached task version for each new action. Queueing commits the
  request before any upload. A task has at most one pending local command.
- Call `ReplayAsync` after connectivity returns. A transport interruption
  leaves the original command pending. Replay retains the original command ID
  and expected version, and records the authoritative receipt, including a
  duplicate receipt after a lost response. Version conflicts and rejections
  stay in the journal for review. Refresh before queuing a replacement.
  Removing a reassigned task from the assignment list preserves its command
  history in the original account scope.

## Verify with a candidate SDK package

Use a .NET 11 SDK from a directory outside this repository's .NET 10
`global.json`. Configure a temporary NuGet source for the versioned
`Honua.Sdk.Field` candidate and its dependencies, in addition to the usual
package sources. For the candidate from SDK commit `8eb3a424`, use
`5.1.1-fieldtasks.1`.

```sh
suite="$MOBILE_REPO/tests/Honua.Mobile.Offline.Tasks.Tests/Honua.Mobile.Offline.Tasks.Tests.csproj"
dotnet restore "$suite" --configfile "$CANDIDATE_NUGET_CONFIG" -p:HonuaWorkTasksSdkVersion=5.1.1-fieldtasks.1
dotnet test "$suite" --no-restore -p:HonuaWorkTasksSdkVersion=5.1.1-fieldtasks.1
```

The test project exercises the packaged shared HTTP client with controlled
responses and an encrypted on-disk database. Deployment also requires a Server
version that supports the task list, transition and completion APIs. The
consuming application owns scheduling, user-facing conflict handling and
collection of the completion form answers.
