# Open UX questions

Each question is grounded in something in the current XAML. Answers will shape the next round of mock revisions and corresponding XAML edits.

1. **SyncCenterPage surfaces `ActiveConflicts` as a flat `CollectionView` and `ConflictReviewItems` as a second flat list right below it.** When a partner returns from a week in the field with 50+ conflicts spanning Culverts, Headwalls, and Crews, would grouping by layer (with collapsible sections, totals, and a per-group "Resolve all") be more usable than two long flat lists? Or should the two lists be merged with a status filter chip row?

2. **MapPage's capture-action FAB (📐) is bolted to the top-right map controls stack, not the bottom thumb zone.** Field crews use the phone one-handed in PPE gloves. Should we move "Add new feature" to a bottom-center primary action bar and reserve the top-right stack for non-destructive view controls (zoom-to-me, zoom-to-features)? Should we mirror left/right per `Settings.HandPreference`?

3. **RecordsPage row status is conveyed by an 8x40dp colored bar (`Orange` = pending, `Green` = synced) and an "orange" pill badge.** Is that the only sync-state nuance needed, or do we also want distinct visual states for: locally created but never synced, edited-since-last-sync, sync-failed, in-conflict? A 5-state legend on Records would mirror what SyncCenter already exposes.

4. **The Quick Actions grid on MainPage repeats targets that already exist as bottom tabs (Map, Records, Settings, Sync).** Is MainPage meant to be a true dashboard (KPIs + recent activity, no nav duplication), or a quick-launcher (current shape)? It is not registered in the Shell's `TabBar`, so today it is reachable only via deep-link / direct push - which is the intended role?

5. **SettingsPage puts the "Developer Mode" toggle *underneath* the "Developer Options" card.** That means a user enabling dev mode for the first time sees an empty placeholder area, scrolls past it, finds the toggle, then scrolls back up. Should the toggle move above the options card, or should we collapse the dev-options card into an inline disclosure that lives next to the toggle?

6. **The Danger Zone "Reset App" is a single full-width red button with no confirmation pattern in the XAML.** Do we want a 2-step confirm (slider, typed phrase, or modal with destructive action) baked into the UI rather than a follow-on alert? The current pattern is one tap to reach an irreversible action.

7. **OfflineCacheDiagnostics shows six raw counters (Pending / Claimed / Succeeded / Failed / Retry / Conflict) as equal-weight tiles.** Field users have asked "is the sync healthy?" not "what is my retry count?" - should we collapse these into a single health indicator with an expandable details view, and reserve the raw counters for the developer-mode diagnostics page?

8. **The template starter (`templates/honua-fieldcollector/MainPage.xaml`) uses `BackgroundColor="Green"` (the XAML named color, not our `#4CAF50` Success token) on the stats card and success toast.** Should we lock the template to the same tokens as the in-house app, or is the template intentionally permissive so partners can rebrand without touching `Colors.xaml`?

9. **MapPage's `IsAddingFeature` overlay sits centered at the top with the "Cancel" button right beside the instruction.** On a 5"+ phone in landscape capture mode, the cancel target is far from the thumb. Should the overlay split into a top instruction band plus a bottom-anchored cancel/confirm pair?

10. **There is no dark-mode `ResourceDictionary` - `App.xaml` only merges `Colors.xaml` and `Styles.xaml`.** The template uses `AppThemeBinding` in two places. Do we want a full dark-mode pass as part of the design review (dark tokens for every brand color, every gray), or is dark mode out of scope for this iteration?
