# Feedback loop (Archived)

> **Archive:** `apps/Honua.Mobile.FieldCollection/` was removed during the M1 SDK
> consolidation (see `docs/guides/field-sdk-consolidation.md`). The mappings below
> for the retired FieldCollection app are kept for historical context only; active
> field app UI work lives in Collect. Only `templates/honua-fieldcollector/MainPage.xaml`
> remains in this repository.

How Claude Design's feedback historically flowed back into the codebase.

## Mock to XAML mapping

| Mock | Historical / current source XAML | Status |
| --- | --- | --- |
| `shell/mock.html` | `apps/Honua.Mobile.FieldCollection/AppShell.xaml` | Removed (M1 consolidation) |
| `main/mock.html` | `apps/Honua.Mobile.FieldCollection/Views/MainPage.xaml` | Removed (M1 consolidation) |
| `map/mock.html` | `apps/Honua.Mobile.FieldCollection/Views/MapPage.xaml` | Removed (M1 consolidation) |
| `records/mock.html` | `apps/Honua.Mobile.FieldCollection/Views/RecordsPage.xaml` | Removed (M1 consolidation) |
| `sync-center/mock.html` | `apps/Honua.Mobile.FieldCollection/Views/SyncCenterPage.xaml` | Removed (M1 consolidation) |
| `settings/mock.html` | `apps/Honua.Mobile.FieldCollection/Views/SettingsPage.xaml` | Removed (M1 consolidation) |
| `template-starter/mock.html` | `templates/honua-fieldcollector/MainPage.xaml` | Active starter template |

## Tokens to source (historical)

The retired FieldCollection token sources below were removed during the M1 SDK consolidation:

- Color tokens historically lived in `apps/Honua.Mobile.FieldCollection/Resources/Styles/Colors.xaml`.
- Typography, spacing, and control defaults historically lived in `apps/Honua.Mobile.FieldCollection/Resources/Styles/Styles.xaml`.
- App-wide button / card / header styles (`BaseButtonStyle`, `SecondaryButtonStyle`, `DangerButtonStyle`, `PageTitleStyle`, `SectionHeaderStyle`, `CardFrameStyle`) historically lived in `apps/Honua.Mobile.FieldCollection/App.xaml`.
- Shell-level theming historically lived in `apps/Honua.Mobile.FieldCollection/AppShell.xaml` (`BaseStyle`).

## Change convention

For each Claude Design proposal:

1. Edit the relevant `mock.html` in this folder to show the proposed UI.
2. In the same PR, edit the matching XAML file from the table above. If a token changes, update `Colors.xaml` / `Styles.xaml` / `App.xaml` and let the cascade do the rest - do not hardcode hex values in the page XAML.
3. Update `tokens.md` and `tokens.json` if the design system itself shifted.
4. PR title format: `mobile(design): <page> - <one-line summary>`. Include both the mock screenshot/preview and a screenshot of the running MAUI app (Android or iOS) so reviewers can compare.
5. If the proposal answers one of the items in `questions.md`, remove or strike that item in the same PR.

Keep the mock and the XAML in lockstep: a mock that ships without the matching XAML edit (or vice versa) is a drift bug.
