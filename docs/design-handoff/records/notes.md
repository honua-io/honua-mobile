# RecordsPage

**Source:** `apps/Honua.Mobile.FieldCollection/Views/RecordsPage.xaml`
**ViewModel:** `RecordsViewModel` (Title: "Records")

## Purpose
List all features for the selected layer, filterable and searchable, with row-level actions and a floating create button.

## Toolbar items
- "Create" (`plus.png`) -> `CreateNewRecordCommand`.
- "Export" (`export.png`) -> `ExportRecordsCommand`.

## Layout (top to bottom)

### 1. Statistics card
- Layer picker (`AvailableLayers` / `SelectedLayer`).
- 3-column counter row: `TotalRecordCount`, `PendingRecordCount` (Warning color), `Records.Count` ("Filtered").

### 2. Search & filter card
- `SearchBar` bound to `SearchText`; `Search` runs `SearchRecordsCommand`. Clear button visible when text is present (`ClearSearchCommand`).
- Filter row: "Pending only" checkbox (`ShowPendingOnly`), "📅 Date Filter" + "🗺️ Location Filter" buttons (no command bindings present in XAML).

### 3. Records list (`CollectionView`)
- `EmptyView`: large 📝 icon, "No records found" + "Create your first record or adjust your filters".
- Item template: `CardFrameStyle` row with three columns:
  - **Status indicator**: 8x40dp vertical bar; orange when `IsPendingSync`, green otherwise (via `BoolToColorConverter`).
  - **Content**: `DisplayTitle` (16sp bold), `CreatedAt`, `AttributeSummary` (2-line truncated), badge row with "Pending Sync" (`Warning`) and "{N} attachments" (`Info`).
  - **Action buttons stack**: 👁️ View (`SecondaryButtonStyle`), ✏️ Edit (`BaseButtonStyle`), 🗑️ Delete (`DangerButtonStyle`). Whole row also gets a `TapGestureRecognizer` that triggers `ViewRecordCommand`.

### 4. Floating Action Button
- 60x60dp pill, `BaseButtonStyle` + `CommonShadow`, anchored bottom-right with 20dp margin, bound to `CreateNewRecordCommand`.

## Bindings of note
- `IsRefreshing` <-> `RefreshCommand`
- `Records` is the filtered visible list; `TotalRecordCount` and `PendingRecordCount` are unfiltered.
- Per-row commands use `RelativeSource AncestorType` to reach the page-level ViewModel.

## Navigation in / out
- **In:** Shell tab "Records".
- **Out:** View / Edit / Delete commands and FAB-create push detail pages (handled in `RecordsViewModel`).
