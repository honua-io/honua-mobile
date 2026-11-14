# Field SDK consolidation (M1)

Tracking: [#421](https://github.com/honua-io/honua-mobile/issues/421),
[#315](https://github.com/honua-io/honua-mobile/issues/315).
This work is tracked under release/2027.1.

## Location evidence migration

Native location capture evidence now lives in `Honua.Mobile.Maui.Location`
in the `Honua.Mobile.Maui` package. Replace the former
`Honua.Mobile.FieldCollection.Models` import for these types with
`using Honua.Mobile.Maui.Location;`. `FieldLocationFix.Location` continues to
accept `Microsoft.Maui.Devices.Sensors.Location`.

The existing record JSON, attribute names, attachment keywords, receiver
metadata, and source classification remain compatible. Stored evidence has no
CLR namespace discriminator. The retained SDK tests include a persisted JSON
fixture from the former app shape. Remaining app consumers import the SDK
namespace during the transition; there is one definition of each evidence type.

This extraction preserves native GPS and external receiver evidence before
removal of the duplicate field app. It does not complete the store/sync migration
or change the Collect shell's existing core boundary.

## Duplicate app retirement

The duplicate `Honua.Mobile.FieldCollection` app, its core library, and its
app-specific tests have been removed. Location evidence is retained in the
shared MAUI SDK. Diagnostics integration tests now call the shared SDK directly;
the live integration suite still verifies upload and queued-report delivery.
The reference `Honua.Mobile.App` and field collector template remain SDK examples.
Distribution workflows target that reference app only. Historical UI sketches
under `docs/design-handoff` describe the retired app.

## Remaining M1 acceptance

- Adopt one published offline store and sync engine in Collect while preserving
  its tested form runtime boundary during the shell spike (paired
  [Collect #173](https://github.com/honua-io/honua-collect/issues/173)).
- Verify encrypted native storage, key lifecycle and recovery on both platforms.
- Publish paired installable Android and iOS Collect builds and run the journey
  below on physical phones. Record platform signing or device dependencies.

## Paired build test script

Record the SDK package version, Collect commit, phone model/OS and installable
build location. Run this journey on the founder's Android and iOS phones:

1. Install the paired Collect build and grant location permission.
2. Open the scripted project online, then disconnect the phone.
3. Capture a point with the built-in receiver; save and reopen the record.
   Confirm coordinates, timestamp, accuracy and source survive restart.
4. If an external receiver is available, repeat with its receiver name, serial
   and correction metadata; otherwise record that step as not run.
5. Reconnect and sync. Reopen the record and confirm the same evidence survives
   upload/download without a duplicate record or queue entry.
6. Revoke location permission, restart, and verify the app presents the existing
   permission recovery path without inventing a new location fix.

Keep the device results with the paired build. Unit-test success alone does not
mark this install journey or the remaining M1 acceptance complete.
