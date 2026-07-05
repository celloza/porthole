# Release Workflow (Installer + GitHub Release)

This document explains how Porthole publishes MSI installer releases using GitHub Actions.

## Workflow file

- `.github/workflows/release-installer.yml`

## What the workflow does

1. Validates the tag commit is part of `main` history.
2. Builds the WiX MSI installer from `src/Porthole.Installer/Porthole.Installer.wixproj`.
3. Creates release assets:
   - `Porthole-<version>-x64.msi`
   - `sha256.txt`
4. Uploads artifacts to the workflow run.
5. Creates or updates a GitHub Release and attaches the MSI + hash file.

## Triggers

The workflow supports two triggers:

1. Tag push (recommended)
   - Trigger pattern: `v*`
   - Example tags:
     - Stable: `v1.2.3`
     - Pre-release: `v1.2.3-rc.1`

2. Manual (`workflow_dispatch`)
   - Inputs:
     - `version` (example: `1.2.3`)
     - `tag` (example: `v1.2.3`)
     - `prerelease` (boolean)

## Stable vs pre-production releases

### Stable release

- Use a tag like `v1.2.3`
- Release is marked as normal (non-prerelease)

### Pre-production release

- Use semver prerelease tag forms such as:
  - `v1.2.3-alpha.1`
  - `v1.2.3-beta.2`
  - `v1.2.3-rc.1`
- Workflow auto-marks the GitHub release as `prerelease=true`

Notes:
- Manual runs can also set `prerelease=true` explicitly.
- Auto-detection still applies for prerelease-style tags.

## Recommended release flow

1. Ensure the intended commit is merged into `main`.
2. Create and push a version tag from that commit:

```powershell
git checkout main
git pull
git tag v1.2.3
git push origin v1.2.3
```

3. Wait for the `Release Installer` action to finish.
4. Verify the GitHub Release contains:
   - MSI file
   - `sha256.txt`

## Pre-production example

```powershell
git checkout main
git pull
git tag v1.3.0-rc.1
git push origin v1.3.0-rc.1
```

This will create a prerelease entry in GitHub Releases.

## Winget alignment notes

- Keep the release tag/version aligned with `PackageVersion` in your winget manifests.
- Use the uploaded MSI URL from GitHub Releases in winget manifest installer entries.
- Publish stable tags for production channels and prerelease tags for preview channels.
