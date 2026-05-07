# Mobile Store Prerequisites

Operational checklist for Google Play issue #85 and App Store Connect issue #87.
Do not commit store credentials, signing material, certificate files,
provisioning profiles, service account JSON, or API key values to this
repository.

## Project Identifiers

These identifiers come from the MAUI `<ApplicationId>` project property. The iOS
`Info.plist` files do not declare a separate `CFBundleIdentifier`, so the MAUI
application ID is the store-facing Android package ID and iOS bundle ID unless a
future platform-specific override is added.

Honua Mobile App:

- Project configuration: `apps/Honua.Mobile.App/Honua.Mobile.App.csproj`
- Android package ID: `io.honua.mobile.app`
- iOS bundle ID: `io.honua.mobile.app`

Honua Field Collection:

- Project configuration:
  `apps/Honua.Mobile.FieldCollection/Honua.Mobile.FieldCollection.csproj`
- Android package ID: `io.honua.mobile.fieldcollection`
- iOS bundle ID: `io.honua.mobile.fieldcollection`

Before the first store upload, confirm these IDs are final. Package IDs and
bundle IDs become expensive to change after Play Console or App Store Connect
app records are created. Template projects with placeholder IDs, such as
`com.companyname.namespace`, are not store targets.

## Ownership Register

Fill these owners before enabling automated uploads. Store ownership must be
tied to named people or groups outside this repository.

- Google Play Console account owner:
  Owns developer account access, app records, Play App Signing, tester access,
  and production promotion rights.
- Android upload signing keys owner:
  Generates, escrows, rotates, and removes upload keystore material for each
  package ID.
- Apple Developer Program team owner:
  Owns team membership, certificates, identifiers, devices, and App Store
  Connect access.
- iOS signing assets owner:
  Creates and rotates distribution certificates and App Store provisioning
  profiles.
- GitHub Actions environments owner:
  Creates protected environments, manages secret updates, and approves
  deployment jobs.
- Release manager:
  Decides which build is uploaded, approves promotion, and records evidence in
  the release checklist.
- Security owner:
  Coordinates emergency rotation and access review after credential exposure.

## GitHub Environments

Create or verify these protected environments before wiring store upload
workflows. Store the same secret names in each environment only when that
environment actually needs to sign or upload builds.

- `android-internal`:
  Google Play internal app sharing and internal testing uploads. Restrict to
  release branches or manual dispatch and require the release manager.
- `android-production`:
  Google Play production promotion. Require the release manager and Google Play
  Console owner.
- `ios-testflight`:
  App Store Connect upload for internal TestFlight. Restrict to release
  branches or manual dispatch and require the release manager.
- `ios-production`:
  App Store production submission or promotion. Require the release manager and
  Apple Developer or App Store Connect owner.

## Android Checklist

- [ ] Run `scripts/validate-android-store-prereqs.sh` and confirm
      `quality/android-store-prereqs.json` still matches MAUI project IDs,
      Android internal distribution workflow mappings, and this guide.
- [ ] Confirm Google Play Console account ownership and billing status.
- [ ] Grant least-privilege Play Console access to release owners and
      automation.
- [ ] Create or verify app records for `io.honua.mobile.app` and
      `io.honua.mobile.fieldcollection`.
- [ ] Confirm the package IDs above match the project configuration at release
      time.
- [ ] Enable Play App Signing for each app record.
- [ ] Generate one upload keystore per package ID unless the release owner
      explicitly approves shared upload signing material.
- [ ] Store the original keystore files in the approved secrets vault or
      password manager, outside GitHub and outside this repository.
- [ ] Add Android signing and Play upload secrets to the protected GitHub
      environments listed above.
- [ ] Configure internal app sharing or the internal testing track for the first
      tester group.
- [ ] Record who may upload, who may approve, and who may promote Android
      builds.
- [ ] Confirm the build artifact format is accepted by the selected Play track
      before upload.

### Android Secrets

Use these GitHub secret names. Values must be set only in protected
environments. `quality/android-store-prereqs.json` is the non-secret register
that keeps package IDs, app targets, required secret names, and rotation scope
auditable in the repository.

- `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`
  Play Console service account JSON with app-scoped permissions.
- `ANDROID_APP_UPLOAD_KEYSTORE_BASE64`
  Base64-encoded upload keystore file for `io.honua.mobile.app`.
- `ANDROID_APP_UPLOAD_KEYSTORE_PASSWORD`
  Upload keystore password for `io.honua.mobile.app`.
- `ANDROID_APP_UPLOAD_KEY_ALIAS`
  Upload key alias for `io.honua.mobile.app`.
- `ANDROID_APP_UPLOAD_KEY_PASSWORD`
  Upload key password for `io.honua.mobile.app`.
- `ANDROID_FIELD_COLLECTION_UPLOAD_KEYSTORE_BASE64`
  Base64-encoded upload keystore file for
  `io.honua.mobile.fieldcollection`.
- `ANDROID_FIELD_COLLECTION_UPLOAD_KEYSTORE_PASSWORD`
  Upload keystore password for `io.honua.mobile.fieldcollection`.
- `ANDROID_FIELD_COLLECTION_UPLOAD_KEY_ALIAS`
  Upload key alias for `io.honua.mobile.fieldcollection`.
- `ANDROID_FIELD_COLLECTION_UPLOAD_KEY_PASSWORD`
  Upload key password for `io.honua.mobile.fieldcollection`.

## iOS Checklist

- [ ] Confirm Apple Developer Program enrollment and App Store Connect access.
- [ ] Create or verify explicit bundle identifiers for `io.honua.mobile.app`
      and `io.honua.mobile.fieldcollection`.
- [ ] Create or verify App Store Connect app records for both bundle IDs.
- [ ] Confirm bundle IDs match the project configuration at release time.
- [ ] Create an Apple distribution certificate owned by the organization.
- [ ] Create one App Store provisioning profile for each bundle ID.
- [ ] Create an App Store Connect API key for automated upload.
- [ ] Store certificate, profile, API key, team ID, issuer ID, key ID, and
      passwords in protected GitHub environment secrets.
- [ ] Create the internal TestFlight tester group and invite the initial tester
      accounts.
- [ ] Record who may upload, who may approve TestFlight distribution, and who
      may submit or promote iOS builds.
- [ ] Record certificate and provisioning profile expiration dates in the
      release calendar with rotation reminders.
- [ ] Run `scripts/validate-ios-store-prereqs.sh` before importing signing
      assets or enabling TestFlight uploads, then attach the output to the
      release evidence.

### iOS Secrets

Use these GitHub secret names. Values must be set only in protected
environments.

- `APPLE_TEAM_ID`
  Apple Developer team ID.
- `APP_STORE_CONNECT_ISSUER_ID`
  API issuer ID.
- `APP_STORE_CONNECT_KEY_ID`
  API key ID.
- `APP_STORE_CONNECT_API_KEY_P8`
  Private `.p8` API key contents.
- `IOS_DISTRIBUTION_CERTIFICATE_P12_BASE64`
  Base64-encoded distribution certificate `.p12`.
- `IOS_DISTRIBUTION_CERTIFICATE_PASSWORD`
  Distribution certificate export password.
- `IOS_APP_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64`
  Base64-encoded App Store provisioning profile for `io.honua.mobile.app`.
- `IOS_FIELD_COLLECTION_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64`
  Base64-encoded App Store provisioning profile for
  `io.honua.mobile.fieldcollection`.

## Tester Groups

Record the exact group names and owner before the first upload.

- Google Play internal app sharing allowlist:
  Owner TBD; initial members source TBD.
- Google Play internal testing track tester group or email list:
  Owner TBD; initial members source TBD.
- TestFlight internal tester group:
  Owner TBD; initial members source TBD.

Tester membership changes must be handled by the platform owner or release
manager. Do not encode tester email addresses in workflow files.

## Approval and Promotion

- Internal Android and TestFlight uploads require the release manager to approve
  the GitHub environment deployment.
- Production promotion requires both the release manager and the relevant store
  account owner.
- Emergency rebuilds may bypass normal release cadence only after the security
  owner records the reason and the affected credentials have been reviewed.
- Each promotion must link the GitHub Actions run, store build number, tester
  group or track, and approver in `quality/release-checklist.md`.

## Rotation Responsibilities

Android upload keystore exposure:

- Rotate the affected app upload key in Play Console.
- Replace the affected app's upload keystore, password, alias, and key password
  secrets.
- Responsible owner: Android upload signing keys owner with Google Play Console
  owner.

Play service account exposure:

- Disable the old service account key.
- Issue a new key and replace `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`.
- Audit Play Console access.
- Responsible owner: Google Play Console owner with security owner.

iOS distribution certificate exposure or expiration:

- Revoke or renew the certificate.
- Regenerate affected provisioning profiles.
- Replace `IOS_DISTRIBUTION_CERTIFICATE_P12_BASE64`.
- Replace `IOS_DISTRIBUTION_CERTIFICATE_PASSWORD`.
- Replace affected `IOS_*_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64`
  secrets.
- Responsible owner: iOS signing assets owner with Apple Developer Program
  owner.

iOS provisioning profile expiration:

- Renew the affected profile.
- Replace the matching `IOS_*_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64`
  secret.
- Responsible owner: iOS signing assets owner.

App Store Connect API key exposure:

- Revoke the old key.
- Create a replacement key.
- Update `APP_STORE_CONNECT_KEY_ID` and `APP_STORE_CONNECT_API_KEY_P8`.
- Responsible owner: Apple Developer Program owner with security owner.

Review store access, GitHub environment reviewers, and all store automation
secrets before each production release and after any owner change.

## Repository Validation

Run this check after changing app identifiers, TestFlight workflow inputs, or
iOS signing secret names:

```bash
scripts/validate-ios-store-prereqs.sh
```

The script validates that the documented iOS bundle IDs match the MAUI
`ApplicationId` values, that the TestFlight workflow maps each app target to the
same bundle ID and provisioning profile secret, and that required iOS signing
and App Store Connect secret names stay documented. It does not read or verify
secret values, certificate files, provisioning profile contents, App Store
Connect app records, or Apple Developer account state.
