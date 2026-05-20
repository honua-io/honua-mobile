# AppShell

**Source:** `apps/Honua.Mobile.FieldCollection/AppShell.xaml`

## Purpose
Root navigation container. Provides a bottom `TabBar` with four destinations and a `Shell.ItemTemplate` for any flyout-style item rendering.

## Primary tabs

| Order | Route | Icon | Page |
| --- | --- | --- | --- |
| 1 | `map` | `map_icon.png` | `MapPage` |
| 2 | `records` (inside a Tab named "Records") | `list_icon.png` | `RecordsPage` |
| 3 | `sync` | `sync_icon.png` | `SyncCenterPage` |
| 4 | `settings` | `settings_icon.png` | `SettingsPage` |

The Records tab is wrapped in an explicit `<Tab>` element (the others are bare `<ShellContent>`), which on Android means Records gets a slightly different surface than its siblings.

## Theming applied at Shell level (`BaseStyle`)

- `Shell.BackgroundColor` -> `Primary` (`#2E8B57`)
- `Shell.ForegroundColor` / `TitleColor` / `TabBarForegroundColor` / `TabBarTitleColor` -> `White`
- `Shell.DisabledColor` -> `Gray200`
- `Shell.UnselectedColor` / `Shell.TabBarUnselectedColor` -> `Gray300`
- `Shell.TabBarBackgroundColor` -> `Primary`

## ItemTemplate
A 50dp tall grid with a 20%/80% column split: icon on the left (45dp), bold 18sp label on the right. Only fires for flyout-style items; the tab bar uses platform defaults.

## Navigation in / out
- Entry: `AppShell` is set as `MainPage` in `App.xaml.cs`.
- No `MainPage` (dashboard) tab is registered; it is reachable only via deep-link / direct navigation.
