# Design Tokens

Extracted verbatim from:

- `apps/Honua.Mobile.FieldCollection/Resources/Styles/Colors.xaml` (colors)
- `apps/Honua.Mobile.FieldCollection/Resources/Styles/Styles.xaml` (typography, control styles, shadow)
- `apps/Honua.Mobile.FieldCollection/App.xaml` (app-wide button / frame / heading styles)

The app does not currently ship a separate dark `ResourceDictionary` - `App.xaml` only merges `Colors.xaml` and `Styles.xaml`. `AppShell.xaml` adds Shell-level theming on top. Dark-mode hex values below are derived from the few `AppThemeBinding` usages in the template starter (`MainPage.xaml`) and are noted as "template only".

## Colors

### Brand

| Token | Hex (light) | Notes |
| --- | --- | --- |
| `Primary` | `#2E8B57` | Sea-green; used for Shell background, primary buttons, page titles, section headers, slider thumb, progress, activity indicator. |
| `PrimaryDark` | `#1F5F3F` | Also used as `StatusBarColor`. |
| `PrimaryLight` | `#4CAF50` | Same hex as `Success`. |
| `Secondary` | `#3F51B5` | Indigo; secondary button background. |
| `SecondaryDark` | `#303F9F` | |
| `SecondaryLight` | `#7986CB` | |
| `Tertiary` | `#FF5722` | Deep orange; used as the danger button background (paired with `DangerButtonStyle`). |
| `TertiaryDark` | `#D32F2F` | |
| `TertiaryLight` | `#FF9800` | Same hex as `Warning`. |

### Semantic

| Token | Hex | Notes |
| --- | --- | --- |
| `Success` | `#4CAF50` | |
| `Info` | `#2196F3` | Attachment count badge. |
| `Warning` | `#FF9800` | Pending-sync badge, offline banner background, "Danger Zone" header text via `Danger`. |
| `Danger` | `#F44336` | "Danger Zone" heading and caption color. |
| `DangerLight` | `#FFEBEE` | Danger-zone card background. |

### Neutrals

| Token | Hex |
| --- | --- |
| `White` | `#FFFFFF` |
| `Black` | `#000000` |
| `Gray100` | `#F5F5F5` |
| `Gray200` | `#EEEEEE` |
| `Gray300` | `#E0E0E0` |
| `Gray400` | `#BDBDBD` |
| `Gray500` | `#9E9E9E` |
| `Gray600` | `#757575` |
| `Gray700` | `#616161` |
| `Gray800` | `#424242` |
| `Gray900` | `#212121` |

### Surfaces / text

| Token | Hex | Notes |
| --- | --- | --- |
| `Background` | `#FFFFFF` | Page background. |
| `Surface` | `#FAFAFA` | Inputs, search, picker. |
| `OnPrimary` | `#FFFFFF` | Text on Primary. |
| `OnSecondary` | `#FFFFFF` | |
| `OnSurface` | `#212121` | Body / title text. |
| `OnBackground` | `#212121` | |
| `StatusBarColor` | `#1F5F3F` | |
| `NavigationBarColor` | `#2E8B57` | |

### Template-only (AppThemeBinding hints, not in Colors.xaml)

| Surface | Light | Dark |
| --- | --- | --- |
| `HonuaLocationIndicator` background | `#E3F2FD` | `#1565C0` |
| `TabView` strip + `HonuaSyncStatus` background | `#F5F5F5` | `#424242` |

## Typography ramp

Sourced from `Styles.xaml` (named styles) plus inline `FontSize=` values found across the pages.

| Role | Style key | Size (sp) | Weight | Color token |
| --- | --- | --- | --- | --- |
| Page title | `PageTitleStyle` (App.xaml) | 32 | Bold | `Primary` |
| Title (alt) | `TitleLabelStyle` | 28 | Bold | `OnSurface` |
| Subtitle | `SubtitleLabelStyle` | 20 | Bold | `OnSurface` |
| Section header | `SectionHeaderStyle` (App.xaml) | 18 | Bold | `Primary` |
| Body | `BodyLabelStyle` | 16 | Regular (line-height 1.4) | `OnSurface` |
| Body emphasis | inline | 16 | Bold | `OnSurface` / `Warning` |
| Stat number | inline (Records/Sync stats) | 18 | Bold | `OnSurface` / `Warning` |
| Stat label | inline | 12 | Regular | `Gray600` |
| Caption | `CaptionLabelStyle` | 12 | Regular | `Gray600` |
| Micro label (range hint) | inline | 10 | Regular | `OnSurface` |
| Badge text | inline (Sync / attachments badges) | 10 | Regular | `White` |
| Emoji icon (card) | inline | 24 | n/a | n/a |
| Empty-state icon | inline | 48 | n/a | `Gray400` |

> The app uses the system font (no `FontFamily` override in `Styles.xaml`). Mocks fall back to the system stack.

## Spacing

Recurring numeric values in the XAML for `Padding`, `Margin`, `Spacing`, `ColumnSpacing`, `RowSpacing`.

| Token | Value (dp) | Used by |
| --- | --- | --- |
| `space-xs` | 5 | Inline stack spacing inside cards |
| `space-sm` | 8 | Card vertical margin, badge padding |
| `space-md` | 10 | Card content spacing, picker column spacing |
| `space-lg` | 15 | Quick-action grid spacing, settings stack |
| `space-xl` | 20 | Sync Center outer padding, frame padding |
| `space-2xl` | 25 | Main page stack spacing |
| `page-h-padding` | 30 | Main page horizontal padding (`Padding="30,0"`) |
| `fab-margin` | 20 | Records FAB margin |
| `tab-bar-height` | 50 | Shell `Tab` item height; template tab strip is 60 |

## Radius

| Token | Value | Used by |
| --- | --- | --- |
| `radius-sm` | 8 | `BaseButtonStyle`, `CardFrameStyle`, outline frames |
| `radius-md` | 10 | Template welcome frame |
| `radius-lg` | 12 | `ElevatedFrameStyle` |
| `radius-pill` | 25 / 30 | `IconButtonStyle` (50dp circle), Records FAB (60dp circle) |

## Elevation / shadow

The only named shadow is `CommonShadow` in `Styles.xaml`:

| Token | Brush | Offset | Radius | Opacity |
| --- | --- | --- | --- | --- |
| `CommonShadow` | Black | `0,2` | 8 | 0.16 |

`CardFrameStyle` in `App.xaml` uses MAUI's legacy `HasShadow=True` (platform default) rather than the named shadow; `ElevatedFrameStyle` and `PrimaryButtonStyle` use `CommonShadow` explicitly. The Records FAB pulls `CommonShadow` directly.

## Control defaults worth knowing

- Primary button (`BaseButtonStyle` in `App.xaml`): bg `Primary`, text `White`, radius 8, padding `16,12`, font 16.
- Secondary button (`SecondaryButtonStyle`): inherits BaseButton, bg `Secondary` (indigo).
- Danger button (`DangerButtonStyle`): inherits BaseButton, bg `Tertiary` (deep orange).
- Card (`CardFrameStyle`): bg `White`, border `Gray200`, radius 8, padding 16, margin `0,8`, shadow on.
- Switch (`BaseSwitchStyle`): on-color `Primary`, thumb `White`.
- Slider (`BaseSliderStyle`): min track `Primary`, max track `Gray300`, thumb `Primary`.

See `tokens.json` for the same values in machine-readable form.
