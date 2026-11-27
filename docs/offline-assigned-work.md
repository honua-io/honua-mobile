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
