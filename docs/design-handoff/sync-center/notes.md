# SyncCenterPage

**Source:** `apps/Honua.Mobile.FieldCollection/Views/SyncCenterPage.xaml`
**ViewModel:** `SyncCenterViewModel` (Title: "Sync Center")

## Purpose
Single screen that surfaces all sync state: current status, manual sync controls, last-sync stats, offline cache diagnostics, conflicts (two flavors), and recent history.

## Layout
Vertical scrollable stack inside a `RefreshView`. Outer padding 20dp, 20dp gap between cards.

## Sections

### 1. Sync Status card
- Header `SyncStatusMessage` (16sp bold, dynamic).
- 🌐 Online / Offline (`BoolToColorConverter`).
- 📝 Pending changes count (hidden if zero).
- 🕒 Last sync time (hidden if null).
- Right column actions: 🔄 Start full sync (`BaseButtonStyle`, 60dp wide), ⏹️ Cancel (`DangerButtonStyle`, only while syncing).
- Bottom row: `ProgressBar` (visible only while syncing, color `Primary`).

### 2. Sync Operations card
- 2-button grid: ⬇️ Pull Only (`PullChangesOnlyCommand`), ⬆️ Push Only (`PushChangesOnlyCommand`), both `SecondaryButtonStyle`.
- Two caption labels explain what each does.

### 3. Last Sync Statistics card (only when stats present)
- 3 stat tiles: Downloaded (`FeaturesPulled`), Uploaded (`FeaturesPushed`), Duration (`mm:ss`).

### 4. Offline Diagnostics card (only when `OfflineCacheDiagnostics` is non-null)
- Section header + Refresh button (`LoadOfflineDiagnosticsCommand`).
- 2-column key/value grid: Package id + size, Metadata status + Feature count, Local gen + Server gen.
- 3-column metrics grid (2 rows): Pending / Claimed / Succeeded / Failed / Retry / Conflict.
- Inline `CollectionView` of per-source diagnostics (capped 180dp height) showing name + source id + feature count.

### 5. Active Conflicts card (only when `ActiveConflicts.Count > 0`)
- "⚠️ Sync Conflicts" header (in `Warning` orange).
- Flat list: layer name + description, with a `Resolve` button per row (`BaseButtonStyle`).

### 6. Conflict Review card (only when `ConflictReviewItems.Count > 0`)
- "Conflict Review" header (Warning orange), 260dp max height collection.
- Each item: feature id, status, source id, operation id, reason, local/server state preview, and a per-row `Review` button (`SecondaryButtonStyle`).

### 7. Recent Sync History card
- Header + "View All" (`ViewSyncHistoryCommand`).
- 200dp capped collection. Each row: status emoji (`SyncStatusToEmojiConverter`), "{Type} Sync", start time, and right-aligned ⬇️N / ⬆️N counters.

### 8. Working Offline card (only when `IsOnline == false`)
- Same orange warning card pattern as MainPage.

## Bindings of note
- `CanRunSyncOperations` gates Pull/Push/Full-sync buttons.
- `IsSyncing` toggles Cancel button visibility and the progress bar.
- All collections live on the VM (`SyncHistory`, `ActiveConflicts`, `ConflictReviewItems`, `OfflineSources`).

## Navigation in / out
- **In:** Shell tab "Sync".
- **Out:** Resolve/Review commands and "View All" are expected to push detail pages; not present in this XAML.
