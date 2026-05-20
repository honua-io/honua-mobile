# SettingsPage

**Source:** `apps/Honua.Mobile.FieldCollection/Views/SettingsPage.xaml`
**ViewModel:** `SettingsViewModel` (Title: "Settings")

## Purpose
Configure account, app preferences, see device/build metadata, toggle developer mode, and access destructive ("Danger Zone") actions.

## Layout
Vertical stack inside a `RefreshView` + `ScrollView`. 20dp outer padding, 20dp inter-card gap. All sections are `CardFrameStyle`.

## Sections (top to bottom)

### 1. Account
3-row grid (icon | label | action):
- 👤 `UserName` | Profile button (`ViewUserProfileCommand`, visible when authenticated).
- 🌐 `ServerUrl` | Configure (`ConfigureServerCommand`).
- 🔐 Authenticated / Not signed in (color-coded) | Sign Out (`DangerButtonStyle`, visible when authenticated).

### 2. Preferences
- **📍 Location Tracking** + caption, `Switch` -> `EnableLocationTracking`.
- **🔄 Background Sync** + caption, `Switch` -> `EnableBackgroundSync`.
- **🔔 Push Notifications** + caption, `Switch` -> `EnablePushNotifications`.
- **Exception Reporting** + dynamic `ExceptionReportingStatus`, `Switch` (with `CanEnableExceptionReporting` gating).
- **Exception Upload Endpoint** (`Entry` URL keyboard) + caption.
- **⏱️ Sync Interval** label + dynamic minutes label + `Slider` (5-60, thumb `Primary`) + min/mid/max captions.
- **💾 Offline Storage Limit** label + MB display + `Slider` (100-2000) + captions.
- **💾 Save Settings** button (`BaseButtonStyle`).

### 3. Device Information
2-column grid with 8 rows: Device name, Platform, OS Version, App version, Device id, Build source display, Workflow run display, Service endpoint display. Right column is muted text for ids / metadata.

### 4. Developer Options (visible when `EnableDeveloperMode`)
Four `SecondaryButtonStyle` buttons:
- 🔍 View Diagnostics (`ViewDiagnosticsCommand`)
- 🌐 Test Connection (`TestConnectionCommand`)
- 🗑️ Clear Cache (`ClearCacheCommand`)
- 📤 Export Data (`ExportDataCommand`)
Header is `SectionHeaderStyle` with `Warning` orange text.

### 5. Developer Mode toggle
"🛠️ Developer Mode" + caption, `Switch` -> `EnableDeveloperMode`. Sits *below* the developer options card so the user has to scroll past the dev tools to find the toggle that enables them.

### 6. About
"ℹ️ About Honua" button + centered version caption.

### 7. Danger Zone
Card background swapped to `DangerLight`. Header "⚠️ Danger Zone" in `Danger`. "🔄 Reset App" (`DangerButtonStyle`) plus a centered warning caption in `Danger`.

## Navigation in / out
- **In:** Shell tab "Settings".
- **Out:** Profile, server config, about, diagnostics commands all push detail pages (not in this XAML).
