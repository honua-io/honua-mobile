# Honua Mobile Accessibility Checklist

This checklist covers MAUI-specific accessibility requirements for the Honua Mobile application.
Every new or modified UI surface must be validated against these criteria before merge.

Reference: [.NET MAUI Accessibility](https://learn.microsoft.com/dotnet/maui/fundamentals/accessibility)

---

## Content Descriptions

- [ ] All interactive controls (`Button`, `ImageButton`, `Entry`, `Picker`, `Switch`, etc.) have a `SemanticProperties.Description` set
- [ ] Non-decorative images have a meaningful `ContentDescription` or `SemanticProperties.Description`
- [ ] Decorative images are explicitly marked with `SemanticProperties.Description=""` so screen readers skip them
- [ ] Icons that convey meaning have descriptive text alternatives

## Automation Identifiers

- [ ] All tappable elements have an `AutomationId` assigned for UI testing
- [ ] `AutomationId` values follow a consistent naming convention (e.g., `Page_ElementName_Action`)
- [ ] No duplicate `AutomationId` values exist within a single page

## Touch Targets

- [ ] Minimum touch target size is 44x44dp (device-independent pixels) for all interactive elements
- [ ] Tap targets that are visually smaller than 44x44dp use padding or `MinimumHeightRequest`/`MinimumWidthRequest` to meet the threshold
- [ ] Adjacent tap targets have sufficient spacing to avoid accidental activation

## Color Contrast

- [ ] Normal text (below 18sp / 14sp bold) meets a minimum contrast ratio of 4.5:1 against its background
- [ ] Large text (18sp+ or 14sp+ bold) meets a minimum contrast ratio of 3:1 against its background
- [ ] UI components and graphical objects (icons, borders, focus indicators) meet a minimum contrast ratio of 3:1
- [ ] Information is never conveyed by color alone (e.g., error states use icons or text in addition to red)

## Screen Reader Navigation

- [ ] Logical reading order is maintained; `TabIndex` or layout order matches visual flow
- [ ] `SemanticProperties.HeadingLevel` is set on page titles and section headers
- [ ] Grouped controls use `SemanticProperties.Hint` to describe the expected interaction
- [ ] Dynamic content changes announce updates via `SemanticScreenReader.Announce()`
- [ ] Modal dialogs and popups trap focus correctly and return focus on dismissal

## Dynamic Type / Font Scaling

- [ ] All text uses scalable units (`sp` or platform default) rather than fixed pixel sizes
- [ ] Layouts accommodate up to 200% font scaling without content clipping or overlap
- [ ] `Label.LineBreakMode` is set to `WordWrap` or `TailTruncation` where appropriate
- [ ] No hardcoded heights on text-containing views that would clip scaled text

## Semantic Properties

- [ ] `SemanticProperties.Description` is set on all controls that need a screen reader label
- [ ] `SemanticProperties.Hint` is set on controls where the interaction is not obvious (e.g., "Double-tap to open the map area selector")
- [ ] `SemanticProperties.HeadingLevel` is used for structural headings (`Level1` through `Level6`)
- [ ] Custom controls implement `ISemanticProvider` or set semantic properties on inner elements
- [ ] Collection views (`CollectionView`, `ListView`) expose item descriptions via `DataTemplate` semantic bindings

---

## Review Sign-Off

| Reviewer | Date | Notes |
|----------|------|-------|
|          |      |       |
