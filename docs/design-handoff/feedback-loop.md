# Feedback loop

How Claude Design's feedback flows back into the codebase.

## Mock to XAML mapping

| Mock | Source XAML to edit |
| --- | --- |
| `shell/mock.html` | `apps/Honua.Mobile.FieldCollection/AppShell.xaml` |
| `main/mock.html` | `apps/Honua.Mobile.FieldCollection/Views/MainPage.xaml` |
| `map/mock.html` | `apps/Honua.Mobile.FieldCollection/Views/MapPage.xaml` |
| `records/mock.html` | `apps/Honua.Mobile.FieldCollection/Views/RecordsPage.xaml` |
| `sync-center/mock.html` | `apps/Honua.Mobile.FieldCollection/Views/SyncCenterPage.xaml` |
| `settings/mock.html` | `apps/Honua.Mobile.FieldCollection/Views/SettingsPage.xaml` |
| `template-starter/mock.html` | `templates/honua-fieldcollector/MainPage.xaml` |

## Tokens to source

- Color tokens live in `apps/Honua.Mobile.FieldCollection/Resources/Styles/Colors.xaml`. Renames cascade to every `{StaticResource <Token>}` reference.
- Typography, spacing, control defaults (Switch / Slider / SearchBar / etc.) live in `apps/Honua.Mobile.FieldCollection/Resources/Styles/Styles.xaml`.
- App-wide button / card / header styles (`BaseButtonStyle`, `SecondaryButtonStyle`, `DangerButtonStyle`, `PageTitleStyle`, `SectionHeaderStyle`, `CardFrameStyle`) are inline in `apps/Honua.Mobile.FieldCollection/App.xaml`.
- Shell-level theming is in `apps/Honua.Mobile.FieldCollection/AppShell.xaml` (`BaseStyle`).

## Change convention

For each Claude Design proposal:

1. Edit the relevant `mock.html` in this folder to show the proposed UI.
2. In the same PR, edit the matching XAML file from the table above. If a token changes, update `Colors.xaml` / `Styles.xaml` / `App.xaml` and let the cascade do the rest - do not hardcode hex values in the page XAML.
3. Update `tokens.md` and `tokens.json` if the design system itself shifted.
4. PR title format: `mobile(design): <page> - <one-line summary>`. Include both the mock screenshot/preview and a screenshot of the running MAUI app (Android or iOS) so reviewers can compare.
5. If the proposal answers one of the items in `questions.md`, remove or strike that item in the same PR.

Keep the mock and the XAML in lockstep: a mock that ships without the matching XAML edit (or vice versa) is a drift bug.
