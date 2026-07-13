# Release Workflow (Installer + GitHub Release)

This document explains how Porthole publishes MSI installer releases using GitHub Actions.

## Workflow file

- `.github/workflows/release-installer.yml`

## What the workflow does

1. Validates the tag commit is part of `main` history.
2. Builds the WiX MSI installer from `src/Porthole.Installer/Porthole.Installer.wixproj`.
3. Creates release assets:
   - `Porthole-<tag>-x64.msi`
   - `Porthole-<tag>-arm64.msi`
   - `sha256.txt`
4. Uploads artifacts to the workflow run.
5. Creates or updates a GitHub Release and attaches the MSI + hash file.
6. Uses `wingetcreate update --out ...` against the published MSI URL to generate an updated WinGet manifest set in CI.
7. Validates the generated manifest directory with `winget validate --manifest`.
8. Submits that generated manifest directory to `microsoft/winget-pkgs`.

Notes:
- Package file names are derived from the release tag and architecture (example: `v0.2.0-rc.1` -> `Porthole-v0.2.0-rc.1-x64.msi` and `Porthole-v0.2.0-rc.1-arm64.msi`).
- MSI `ProductVersion` is derived from the numeric core of the tag (`0.2.0` for `v0.2.0-rc.1`) because Windows Installer requires numeric product versions.
- The WinGet workflow generates manifests in the runner temp directory from the GitHub Release MSI URL.
- The first accepted submission is tracked in [issue #24](https://github.com/celloza/porthole/issues/24); subsequent releases use `wingetcreate update` only.
- During installer build, the app publish step also receives explicit version metadata:
   - `Version` = numeric MSI product version (`major.minor.patch`)
   - `FileVersion` = `<ProductVersion>.0`
   - `InformationalVersion` = `<release-version>+<commit-sha>`
- This metadata is surfaced in-app on the Settings/About page so release builds show the intended release version string.

**Note on MSI ProductVersion format:** The WiX toolset requires the MSI `ProductVersion` to be numeric only
(e.g., `1.2.3`). Pre-release labels like `-alpha`, `-rc.1` are stripped during version extraction; see the
workflow's `outputs.msi_version` for the final numeric version used. This ensures compatibility with Windows
Installer requirements while still supporting semantic versioning in the release tag and app metadata.

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
   - x64 MSI file
   - ARM64 MSI file
   - `sha256.txt`
6. Verify the workflow generated and validated a WinGet manifest set from the release MSI URL.
7. If `WINGETCREATE_TOKEN` is configured, verify the workflow also submitted the manifest to `microsoft/winget-pkgs`.

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

On ARM64 machines, install the ARM64 package instead:

```powershell
msiexec /i "Porthole-v0.2.0-arm64.msi" /qn
```

Per-user install that runs Porthole immediately and enables startup:

```powershell
msiexec /i "Porthole-v0.2.0-x64.msi" /qn RUN_AFTER_INSTALL=1 AUTO_START_WITH_WINDOWS=1
```

Per-user install with startup disabled:

```powershell
msiexec /i "Porthole-v0.2.0-x64.msi" /qn AUTO_START_WITH_WINDOWS=0
```

## Testing the installer in Windows Sandbox

Before tagging a release, verify the MSI installs and runs correctly in a clean,
disposable environment using the `scripts/Test-Sandbox.ps1` helper.

### Prerequisites

Windows Sandbox must be enabled (not available on Windows Home edition):

```powershell
Enable-WindowsOptionalFeature -FeatureName 'Containers-DisposableClientVM' -All -Online
```

Restart after the command completes.

### Build and launch

Build a fresh installer and open it in a sandbox:

```powershell
.\scripts\Test-Sandbox.ps1 -Version 0.0.5
```

**Note:** The `-Version` parameter must be numeric only (e.g., `0.0.5`, not `0.0.5-alpha`).
WiX/Windows Installer requires the `ProductVersion` in `major.minor.build` format with no pre-release labels.

Reuse the most recently built MSI without rebuilding:

```powershell
.\scripts\Test-Sandbox.ps1 -SkipBuild
```

### What happens

1. The installer output folder (`src/Porthole.Installer/bin/x64/Release/`) is mapped
   read-only into the sandbox at `C:\Users\WDAGUtilityAccount\Desktop\Porthole`.
2. A startup script copies the MSI to the sandbox desktop and launches it interactively.
3. Complete the installation wizard inside the sandbox.
4. Porthole starts via the tray; the Home page should show the prerequisites `InfoBar`
   because WSL Containers is not present in a fresh sandbox.
5. Click **Run wsl --update --pre-release** and confirm:
   - A `cmd.exe` terminal opens and runs the command.
   - A guidance dialog appears instructing you to relaunch Porthole once the
     terminal completes.

Closing the sandbox discards all changes — no cleanup is required on the host.

## Winget alignment notes

- Keep the release tag/version aligned with `PackageVersion` in your winget manifests.
- Use the uploaded MSI URL from GitHub Releases in winget manifest installer entries.
- Publish stable tags for production channels and prerelease tags for preview channels.
- For this post-bootstrap flow, the release pipeline updates the accepted package with `wingetcreate update`, validates the generated manifest, and always submits it.
