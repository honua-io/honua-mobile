# MainPage (Dashboard)

**Source:** `apps/Honua.Mobile.FieldCollection/Views/MainPage.xaml`
**ViewModel:** `MainViewModel` (Title default from `BaseViewModel`: "Honua Field Collection")

## Purpose
Landing / dashboard screen surfaced when the user is not on a specific feature tab. Provides at-a-glance connectivity, sync state, and quick-action shortcuts.

## Layout
A pull-to-refresh (`RefreshView`) scroll container with vertical stacking, 30dp horizontal padding, 25dp inter-section spacing. Top-level `ActivityIndicator` overlay handles busy state.

## Sections (top to bottom)

1. **Welcome card** (`CardFrameStyle`)
   - `WelcomeMessage` bound from `BaseViewModel` (defaults vary based on auth).
   - Subtitle: "Your mobile field data collection companion".
   - 3-column status strip with outline frames, `Primary`-colored borders:
     - 🌐 Online / Offline (color-coded green / red via `BoolToColorConverter`).
     - 📝 `PendingChangesCount` + caption "Pending".
     - 🔄 `LastSyncTime` formatted `HH:mm` (or "Never") + caption "Last Sync".
2. **Quick Actions** (`SectionHeaderStyle` heading, 2x2 grid of buttons)
   - `BaseButtonStyle`: 📍 Map -> `NavigateToMapCommand`, 📋 Records -> `NavigateToRecordsCommand`.
   - `SecondaryButtonStyle`: 🔄 Sync (disabled offline) -> `QuickSyncCommand`, ⚙️ Settings -> `NavigateToSettingsCommand`.
3. **Sync Required** card (only when `PendingChangesCount > 0`)
   - "⚠️ Sync Required" in `Warning` orange.
   - Caption with pending count, full-width Sync Now button.
4. **Offline Notice** card (only when `IsOnline == false`)
   - Card background swapped to `Warning`, white text. Title "📶 Offline Mode", caption explains queueing.

## Bindings of note
- `IsRefreshing` <-> `RefreshCommand`
- `IsBusy` -> activity indicator visibility/run state
- `IsOnline` drives quick-sync `IsEnabled`, online/offline label and the offline-mode card visibility (`InvertedBoolConverter`).

## Navigation in / out
- **In:** Direct navigation only (not a Shell tab; the Shell goes straight to MapPage). Code-behind / DI is responsible for routing here.
- **Out:** Each quick-action button issues a `Navigate*Command` to a `RecordsPage`, `MapPage`, `SettingsPage`, or triggers `QuickSyncCommand` in place.
