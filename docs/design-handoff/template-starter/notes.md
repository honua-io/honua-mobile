# Template starter - MainPage

**Source:** `templates/honua-fieldcollector/MainPage.xaml`
**Code-behind:** `templates/honua-fieldcollector/MainPage.xaml.cs`

## Purpose
Customer-facing scaffolding shown when a partner spins up a new FieldCollector app from the `honua-fieldcollector` template. It is intentionally separate from the in-house FieldCollection app and uses the Honua MAUI control suite (`honua:` namespace) at the top level.

## Layout
3-row grid:
- Row 0: `HonuaLocationIndicator` (GPS strip).
- Row 1: 3-tab `TabView` (Collect / Map / Stats) + a loading overlay.
- Row 2: `HonuaSyncStatus` (sync strip).
A success toast is overlaid at the top of the page when triggered.

## Tabs

### 📝 Collect
- Welcome `Frame` with `Primary` background: "🚀 Field Data Collection" + tagline.
- `HonuaFeatureForm` (FormId `field-site-inspection`, drafts on, progress on).
- 2-column quick-action grid: "📷 Quick Photo" (`Secondary`) + outline "🗺️ View Map".

### 🗺️ Map
- Full-tab `HonuaMapView` with toolbar + overlays, spatial query off, shows collected features.

### 📊 Stats
- Green-background stats `Frame` with two columns: Records count + Photos count (32sp white numbers).
- "📈 Recent Activity" header.
- `CollectionView` of recent activity rows (icon | title + description | time).

## Theming notes
- This template references `Primary` / `Secondary` / `Gray500` / `Gray600` from `Colors.xaml` and uses two `AppThemeBinding` blocks for `HonuaLocationIndicator` (`#E3F2FD`/`#1565C0`) and the tab strip + `HonuaSyncStatus` (`#F5F5F5`/`#424242`). These dark-mode hex values are not present anywhere else in the codebase.
- The stats `Frame` uses literal `BackgroundColor="Green"` (not a token) and the toast uses literal `BackgroundColor="Green"` - two of the few places in the bundle where colors bypass the tokens.

## Navigation in / out
- **In:** App entrypoint when the template is used.
- **Out:** "🗺️ View Map" button via `OnViewMapClicked`; tab switching is in-page.
