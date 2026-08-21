# Publishing Honua Mobile packages publicly

Honua Mobile has two independent, immutable public release lanes:

- `mobile-dotnet-vX.Y.Z[-prerelease]` publishes `Honua.Mobile.Sdk`,
  `Honua.Mobile.Offline`, and `Honua.Mobile.Maui` to nuget.org.
- `mobile-embed-vX.Y.Z[-prerelease]` publishes `@honua-io/embed` to npmjs.com.

Both lanes build once, retain the exact package bytes as workflow artifacts,
publish only from a GitHub-verified signed annotated tag, verify an anonymous
consumer install from the public registry, and only then create a GitHub
Release with checksums. A failed public-consumer job is a failed release even
when the registry accepted the upload.

## One-time registry setup

The repository owner must complete these actions outside GitHub:

1. On nuget.org, ensure the Honua owner account controls (or reserves) the
   `Honua.Mobile.*` prefix. Create a push-only API key scoped to
   `Honua.Mobile.Sdk`, `Honua.Mobile.Offline`, and `Honua.Mobile.Maui`.
2. Create a protected GitHub environment named `public-nuget`, require the
   desired human reviewers, and store that key as `NUGET_API_KEY` in the
   environment. No such secret is configured in this repository yet.
3. Ensure the Honua npm account controls the `@honua-io` scope. Create a
   granular publish token scoped only to `@honua-io/embed` (including the npm
   account's required 2FA/bypass policy for CI publication).
4. Create a protected GitHub environment named `public-npm`, require the
   desired human reviewers, and store that token as `NPM_TOKEN` in the
   environment. No such secret is configured in this repository yet.
5. After the first npm publication, configure npm Trusted Publishing for this
   repository and `.github/workflows/publish-npm-embed.yml`; then remove the
   long-lived token path in a follow-up. The first publish still needs a token
   because there is no package page on which to configure a trusted publisher.

Never put either credential in `NuGet.config`, `.npmrc`, workflow inputs,
release notes, or repository variables.

## .NET prerequisite

The mobile packages consume the `Honua.Sdk.*` train pinned by
`HonuaSdkDotNetTrainVersion` in `Directory.Build.props`. Every transitive SDK
package for that exact version must already be on nuget.org. The package-smoke
job installs the locally packed mobile packages using only a local source and
nuget.org, so a missing private-only dependency fails before publication.

For the 2026.1 cut, that SDK train is `1.6.0`.

## Cut the .NET package set

1. Set the same `<PackageVersion>` in all three package projects.
2. Let required CI and release dry-run checks pass.
3. Create a signed annotated tag whose version matches those files exactly:

   ```bash
   git tag -s mobile-dotnet-v0.1.0-alpha.1 -m "Honua Mobile .NET 0.1.0-alpha.1"
   git push origin mobile-dotnet-v0.1.0-alpha.1
   ```

4. Approve the single `public-nuget` environment job. It proves all three
   versions are absent before pushing any member of the package set.
5. Require the anonymous nuget.org restore and GitHub Release jobs to pass.

NuGet package versions cannot be replaced. Any content correction requires a
new version and tag. Publication is not atomic: if nuget.org accepts only part
of the set before a later push fails, do not rerun the tag workflow. Advance
all three package versions and cut a new signed tag so the workflow never
treats a pre-existing public package as the artifact built by a different run.

## Cut the embed package

1. Set the same version in `src/Honua.Embed/package.json` and its lockfile.
2. Let required CI and the release dry run pass.
3. Create and push a matching signed annotated tag:

   ```bash
   git tag -s mobile-embed-v0.1.0 -m "@honua-io/embed 0.1.0"
   git push origin mobile-embed-v0.1.0
   ```

4. Approve the `public-npm` environment.
5. Require the anonymous npm install/import and GitHub Release jobs to pass.

npm package versions cannot be replaced. Any content correction requires a
new version and tag. If npm accepted an upload but the workflow lost the
response or failed later, confirm the public version and cut a new version;
the preflight intentionally refuses to reuse the existing one.

## Public evidence

A completed release has all of these independent signals:

- the exact version is visible through the unauthenticated registry endpoint;
- the workflow's anonymous install/restore job passed with no private feed;
- the GitHub Release is tied to the signed source tag;
- release assets include `SHA256SUMS`;
- GitHub build-provenance attestations exist for the uploaded package bytes;
- the workflow run identifies the exact source commit and environment approval.
