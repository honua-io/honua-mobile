# MapPage

**Source:** `apps/Honua.Mobile.FieldCollection/Views/MapPage.xaml`
**ViewModel:** `MapViewModel` (Title: "Map")

## Purpose
Show the active layer's features on a `maps:Map` control and provide layer-aware capture.

## Layout
Two-row grid: row 0 is the full-bleed map plus floating overlays; row 1 is a card-style bottom panel for layer selection and stats. A full-screen `ActivityIndicator` overlays both rows for busy state.

## Map control
- `IsShowingUser="True"`, `MapType="Hybrid"`.
- Pins are bound from `MapFeatures` (collection binding is wired up in code-behind / from the ViewModel; not declarative in XAML).

## Floating actions (top-right vertical stack, 10dp gap)
1. **📍 Zoom to current location** -> `ZoomToCurrentLocationCommand` (`BaseButtonStyle`, 50x50).
2. **🎯 Zoom to features** -> `ZoomToFeaturesCommand` (`BaseButtonStyle`, 50x50).
3. **📐 Add new feature** -> `StartAddingFeatureCommand` (`SecondaryButtonStyle`). Visible only when `SelectedLayer.IsEditable` is true.

## Capture-mode overlay (top, centered)
- Becomes visible while `IsAddingFeature` is true.
- Frame background `Primary`, label "📍 Tap on the map to add a feature" + `Cancel` button (`SecondaryButtonStyle`) bound to `CancelAddingFeatureCommand`.

## Bottom panel (`CardFrameStyle`, 10dp side margin)
- Row 0: "Layer:" label | Picker bound to `AvailableLayers` (display by `Name`, selected -> `SelectedLayer`) | `⚙️` settings button (`OpenLayerSettingsCommand`, only when a layer is selected).
- Row 1 (only when a layer is selected): horizontal row with `📊 N features`, `🎨 GeometryType`, and a `CheckBox` + "Visible" label bound to `SelectedLayer.IsVisible`.

## Navigation in / out
- **In:** Shell tab "Map" (default landing tab).
- **Out:** `OpenLayerSettingsCommand` is expected to navigate to a layer-settings modal (not in this folder); capture flows post-tap.
