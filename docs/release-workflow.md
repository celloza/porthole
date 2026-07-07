# Release Workflow (Installer + GitHub Release)

This document explains how Porthole publishes MSI installer releases using GitHub Actions.

## Workflow file

- `.github/workflows/release-installer.yml`

## What the workflow does

1. Validates the tag commit is part of `main` history.
2. Builds the WiX MSI installer from `src/Porthole.Installer/Porthole.Installer.wixproj`.
3. Creates release assets:
   - `Porthole-<tag>-x64.msi`
   - `sha256.txt`
4. Uploads artifacts to the workflow run.
5. Creates or updates a GitHub Release and attaches the MSI + hash file.

Notes:
- The package file name is derived from the release tag (example: `v0.2.0-rc.1` -> `Porthole-v0.2.0-rc.1-x64.msi`).
- MSI `ProductVersion` is derived from the numeric core of the tag (`0.2.0` for `v0.2.0-rc.1`) because Windows Installer requires numeric product versions.
- During installer build, the app publish step also receives explicit version metadata:
   - `Version` = numeric MSI product version (`major.minor.patch`)
   - `FileVersion` = `<ProductVersion>.0`
   - `InformationalVersion` = `<release-version>+<commit-sha>`
- This metadata is surfaced in-app on the Settings/About page so release builds show the intended release version string.

## Installer options shown during setup

The MSI now prompts users for two post-install behaviors:

1. `Run Porthole after setup`
   - Shown as an Exit dialog checkbox at the end of installation.
   - If selected, setup launches `Porthole.Tray.exe` when the installer closes.
   - Can be controlled silently with `RUN_AFTER_INSTALL=1`.

2. `Automatically start Porthole when you login to Windows`
   - Exposed as an optional installer feature in the setup UI.
   - If selected, setup writes an `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry for `Porthole`.
   - Enabled by default.
   - Can be controlled silently with `AUTO_START_WITH_WINDOWS=1` or `AUTO_START_WITH_WINDOWS=0`.

## Triggers

The workflow supports two triggers:

1. GitHub Release publish (recommended)
   - Triggered when a release is published in GitHub.
   - This includes prerelease publications, so only one run is created per release.
    - The release's tag is used to derive build metadata and output MSI naming.
    - Example tags:
       - Stable: `v1.2.3`
       - Pre-release: `v1.2.3-rc.1`

2. Manual (`workflow_dispatch`)
   - Inputs:
     - `version` (example: `1.2.3`)
     - `tag` (example: `v1.2.3`)
     - `prerelease` (boolean)

In both trigger modes, the workflow emits:
- `msi_version`: numeric `major.minor.patch` used for MSI `ProductVersion`
- `app_informational_version`: `<version>+<github.sha>` used for app informational metadata

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

3. In GitHub, draft/publish a Release for that tag.
4. Wait for the `Release Installer` action to finish.
5. Verify the GitHub Release contains:
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

## Silent install examples

Per-user install (non-admin) with defaults:

```powershell
msiexec /i "Porthole-v0.2.0-x64.msi" /qn
```

Per-user install that runs Porthole immediately and enables startup:

```powershell
msiexec /i "Porthole-v0.2.0-x64.msi" /qn RUN_AFTER_INSTALL=1 AUTO_START_WITH_WINDOWS=1
```

Per-user install with startup disabled:

```powershell
msiexec /i "Porthole-v0.2.0-x64.msi" /qn AUTO_START_WITH_WINDOWS=0
```

## Winget alignment notes

- Keep the release tag/version aligned with `PackageVersion` in your winget manifests.
- Use the uploaded MSI URL from GitHub Releases in winget manifest installer entries.
- Publish stable tags for production channels and prerelease tags for preview channels.
