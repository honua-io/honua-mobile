# Honua FieldCollection - Design Handoff

This bundle is a self-contained snapshot of the FieldCollection mobile UI surface, prepared so that Claude Design (or any other design reviewer) can audit layout, hierarchy, and design-token usage without needing to build the .NET MAUI project.

Everything here was extracted from the live XAML in `apps/Honua.Mobile.FieldCollection/` plus the customer-facing starter template in `templates/honua-fieldcollector/`. No screenshots are included; each page is reconstructed as a static HTML mock that uses the real design tokens (CSS custom properties) so visual fidelity matches the app's intent.

## Pages in this bundle

| Folder | Page | Purpose | Primary actions | Key ViewModel |
| --- | --- | --- | --- | --- |
| `shell/` | `AppShell` | Bottom tab navigation host | Switch between Map / Records / Sync / Settings | (Shell, no VM) |
| `main/` | `MainPage` | Dashboard / landing card with status + quick actions | Quick-sync, jump to Map/Records/Settings | `MainViewModel` |
| `map/` | `MapPage` | Map view with layer picker and capture overlay | Zoom to me, zoom to features, add feature, pick layer | `MapViewModel` |
| `records/` | `RecordsPage` | Filterable feature list per layer | Search, filter pending, view/edit/delete records, FAB create | `RecordsViewModel` |
| `sync-center/` | `SyncCenterPage` | Sync state, statistics, conflicts, history, offline diagnostics | Full sync, pull-only, push-only, resolve / review conflicts | `SyncCenterViewModel` |
| `settings/` | `SettingsPage` | Account, preferences, device info, developer & danger zones | Sign out, toggle preferences, adjust sliders, reset app | `SettingsViewModel` |
| `template-starter/` | `MainPage` (template) | Customer-facing starter template for new FieldCollector apps | Collect tab (form), Map tab, Stats tab | (code-behind only) |

## What's in each subfolder

- `notes.md` - purpose, primary actions, key controls, bindings, navigation in / out.
- `mock.html` - standalone HTML/CSS mock (390x844 mobile viewport, single file, no JS). Open it directly in a browser.
- `xaml-source.md` - the original XAML for that page in a fenced code block, for reference.

## Top-level files

- `tokens.md` / `tokens.json` - the full design system extracted from `Resources/Styles/Colors.xaml`, `Resources/Styles/Styles.xaml`, and the inline styles in `App.xaml`. Includes colors, type ramp, spacing, radius, and elevation tokens.
- `questions.md` - specific open UX questions for Mike, grounded in the current XAML.
- `feedback-loop.md` - how Claude Design feedback flows back into the codebase (which XAML file maps to which mock, where to update tokens, and the PR convention).

## How to import into Claude Design

1. In Claude Design, create a new project for "Honua FieldCollection".
2. Upload the entire `docs/design-handoff/` folder as the project source. All paths in this bundle are relative, so the folder is self-contained.
3. Use the `README.md` (this file) as the project overview, `tokens.md` as the design-system reference, and each page subfolder as a separate review surface.
4. When proposing changes, edit the relevant `mock.html` and, in parallel, mark which XAML file in `apps/Honua.Mobile.FieldCollection/Views/` needs to change (see `feedback-loop.md` for the convention).

> Note: emojis appear in the mocks only where they appear in the original XAML (icons baked into button text, status badges, section headers). The bundle adds none.
