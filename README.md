<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/portholelogowithname-dark.svg">
    <source media="(prefers-color-scheme: light)" srcset="assets/portholelogowithname.svg">
    <img alt="logo" src="assets/logo-light.svg" width="200">
  </picture>
</div>
<div align="center">
  <a href="https://github.com/celloza/porthole/actions/workflows/release-installer.yml">
    <img src="https://github.com/celloza/porthole/actions/workflows/release-installer.yml/badge.svg" alt="Release Installer status">
  </a>
  <a href="https://github.com/celloza/porthole/actions/workflows/tests.yml">
    <img src="https://github.com/celloza/porthole/actions/workflows/tests.yml/badge.svg" alt="Tests status">
  </a>
</div>
<div>
  <br />
  <br />
</div>

Porthole is a native Windows desktop dashboard for WSL Containers.

It uses a WinUI 3 application for the UI and a tray-hosted backend for container and image operations. The app and tray communicate over named pipes using typed JSON contracts in `Porthole.Core`.

## Features

**Implemented:**
- ✅ **Dashboard**: real-time system metrics and container status
- ✅ **Images**: pull, tag, and delete container images
- ✅ **Containers**: start, stop, and remove containers
- ✅ **Sessions**: create and manage isolated session environments for workload grouping
- ✅ **Networking**: configure network mode (bridge vs. consomme) and inspect active port bindings and host proxy configuration
- ✅ **Volume Management**: inspect named volumes and bind mounts, surface virtiofs telemetry, and create/delete/prune named volumes
- ✅ **Run Wizard**: interactive container creation with template save/load, port mapping, environment variables, and volume configuration
- ✅ **Settings & About**: in-app appearance switching (System/Light/Dark) and build/version details

**Planned:**
- 🔒 **Enterprise Governance**: MDM registry allowlists, Defender for Endpoint integration, audit logging

## Feature Details

### Dashboard

Real-time overview of system status and container inventory:
- Active container count and status breakdown
- System resource utilization
- Quick-access container controls

<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/dashboard.png">
    <img alt="dashboard" src="assets/dashboard.png" width="600">
  </picture>
</div>

### Images

Manage container images in the active session:
- **Pull**: fetch images from registries with progress tracking
- **Tag**: apply custom repository and tag labels
- **Delete**: remove images (with dependency checks)
- **Prune**: clean up unused images

<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/images.png">
    <img alt="dashboard" src="assets/images.png" width="600">
  </picture>
</div>

### Containers

Lifecycle management for running containers:
- **Start/Stop**: manage container state
- **Remove**: delete containers (with safety confirmation)
- **Inspect**: view container details (ID, image, status, ports)
- **Logs**: view recent container output (future)

<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/containers.png">
    <img alt="dashboard" src="assets/containers.png" width="600">
  </picture>
</div>

### Networking

Inspect and configure container networking in the active session:

**Network Mode Toggle**
- **Bridge**: Containers are connected to a default bridge network (standard Docker mode)
- **Consomme**: Experimental mode for specialized networking scenarios

**Active Port Bindings**
- Real-time display of all port mappings from running containers
- Shows host port, container port, and protocol (tcp/udp)
- Auto-discovered via `wslc inspect` — no manual configuration needed
- Useful for debugging port conflicts and validating expose declarations

**Proxy Configuration**
- Reads host Windows proxy settings from environment variables (HTTP_PROXY, HTTPS_PROXY, NO_PROXY)
- Displays effective proxy configuration for container operations
- Helps diagnose proxy-dependent workloads (artifact downloads, registry access, etc.)

<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/networking.png">
    <img alt="dashboard" src="assets/networking.png" width="600">
  </picture>
</div>

### Volume Management

Inspect storage attached to the active session, including both named volumes and host-path bind mounts:

**Volume Inventory**
- Lists named volumes returned by `wslc volume ls`
- Discovers bind mounts by inspecting containers in the active session
- Shows source path, container target path, driver label, access mode, usage state, and throughput class
- Flags Windows host-path mounts as virtiofs-backed shared mounts

**Volume Operations**
- **Create Volume**: create a new named volume in the active session
- **Delete Volume**: remove an unused named volume with confirmation
- **Prune Volumes**: remove unused named volumes in bulk
- Bind mounts are shown for visibility and telemetry, but are not deleted from the Volumes page

**Run Wizard Integration**
- Volume mounts entered in the wizard are annotated as named volumes, virtiofs host mounts, or 9P-style Linux/WSL path mounts
- A host-folder picker can prefill bind mounts for Windows paths

See [docs/volume-management.md](docs/volume-management.md) for behavior details and limitations.

<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/networking.png">
    <img alt="dashboard" src="assets/volumes.png" width="600">
  </picture>
</div>

### Sessions

Isolated container environments for multi-tenant and workload grouping scenarios:

**Session Lifecycle**
- **Create Session**: Launch a new isolated session with a custom name (auto-generates storage directory)
- **Active Session**: All image and container operations target the active session context
- **Switch Active Session**: Designate which session is the active context (containers persist in their session)
- **Delete Session**: Remove an inactive session and all its containers (safety confirmation required)

**Session Listing**
- View all available sessions with metadata (name, storage path, active status)
- Visual indicator for the currently active session
- Storage path shows where session state and containers are persisted

### Run Wizard

Interactive guided flow for creating a container configuration and starting it:

**Wizard Start**
- Choose **Use Template File** to load a saved JSON template
- Choose **Create New** to start from an empty configuration

**Configuration Steps**
- Step 1: Basic settings (container name, image, optional startup command)
- Step 2: Advanced settings (port mappings, environment variables, volume mounts)
- Step 3: Review and run

**Volume Mount Guidance**
- Windows host paths such as `C:\data:/app/data` are labeled as virtiofs-backed bind mounts
- Linux or WSL paths such as `/mnt/c/data:/app/data` are labeled as 9P-style host sharing
- Named volumes such as `myvolume:/app/data` stay inside the active session storage

**Run Actions**
- Primary action: **Save Template and Run**
- Secondary action: **Run Without Saving** from split-button menu

**Template Format & Versioning**
- Newly saved templates use a versioned envelope format:

```json
{
  "version": 2,
  "container": {
    "name": "web",
    "imageReference": "nginx:latest",
    "startupCommand": null,
    "portMappings": ["8080:80"],
    "environmentVariables": ["ENV=prod"],
    "volumeMounts": ["C:\\data:/app/data"]
  },
  "savedAtUtc": "2026-07-05T11:42:52.0000000+00:00"
}
```

- The loader supports:
  - Legacy unversioned templates (raw container fields at root)
  - Versioned templates (v1 and v2)
  - Compatibility aliases for config payload location (`container` or `config`)

**Default Template File Naming**
- Save picker suggests: `porthole-<imagename>-ddmmyyhhss.json`
- Example: `porthole-nginx-0507261142.json`

### Settings & About

Settings now includes both runtime app preferences and project metadata in one place:

**Appearance**
- Theme selector for `System default`, `Light`, and `Dark`
- Theme changes are applied live to the current app window
- Caption button foreground fallback keeps close/minimize/maximize glyphs readable in both light and dark themes

**About**
- Theme-aware logo swap (`portholelogowithname.svg` and `portholelogowithname-dark.svg`)
- Version line in the format `vX.Y.Z` or `vX.Y.Z (<metadata>)` when build metadata is available
- Quick links for repository, license, issues, and releases

## Installation

### Windows Package Manager (winget)

Porthole is available on Windows Package Manager. Install with:

```powershell
winget install celloza.Porthole
```

To upgrade an existing installation:

```powershell
winget upgrade celloza.Porthole
```

### Manual Installation (MSI)

Alternatively, download the `.msi` installer from [GitHub Releases](https://github.com/celloza/porthole/releases) and run it directly.

## Projects

- `src/Porthole.App`: WinUI 3 desktop dashboard
- `src/Porthole.Core`: shared models, service contracts, and viewmodels
- `src/Porthole.Tray`: tray host backend with WSL Containers integration
- `tests/Porthole.Core.Tests`: unit tests

## Documentation

- [docs/volume-management.md](docs/volume-management.md): named volumes, bind mounts, virtiofs telemetry, and run-wizard mount guidance
- [docs/release-workflow.md](docs/release-workflow.md): installer and GitHub release flow

## Prerequisites

### Runtime

- Windows 10/11 (Pro, Enterprise, or Education editions)
- WSL 2 with WSL Containers prerelease components

To verify you have WSL Containers installed, run:

```powershell
wsl --update --pre-release
```

If you see `wslc` available in your PATH, you're ready to use Porthole.

### Development (Building from Source)

- .NET SDK 8.0+
- Visual Studio or VS Code with C# extension

## Architecture

Porthole follows a **three-tier architecture**:

```
┌─────────────────────────┐
│   Porthole.App (WinUI)  │  Desktop dashboard UI
│   (MainWindow, Pages)   │  - MVVM with CommunityToolkit
└────────────┬────────────┘
             │ Named Pipes (JSON IPC)
             ↓
┌─────────────────────────┐
│  Porthole.Core (Shared) │  Contracts & ViewModels
│  (Models, Services)     │  - No UI, No SDK refs
└────────────┬────────────┘
             │ Named Pipes (JSON IPC)
             ↓
┌─────────────────────────┐
│ Porthole.Tray (Backend) │  Container operations
│ (WslcBackendService)    │  - WSL Containers SDK integration
└─────────────────────────┘
```

**Key Design Principles:**

- **UI ↔ Backend Separation**: All container operations flow through named pipes; no direct SDK calls from the dashboard
- **Shared Contracts**: `Porthole.Core` contains request/response DTOs and service interfaces that both app and tray implement/consume
- **MVVM with DI**: Pages are thin; all business logic lives in ViewModels with relay commands
- **Async-first**: All I/O (pipes, file system, processes) is async to keep the UI responsive

## Build

```powershell
dotnet build Porthole.slnx -c Debug
```

**Troubleshooting:**
- If the tray process is locked: `Stop-Process -Name Porthole.Tray -Force`
- For a clean rebuild: `dotnet clean ; dotnet build Porthole.slnx -c Debug`

## Run

**Recommended: Run tray first (it auto-launches the dashboard)**

```powershell
dotnet run --project src/Porthole.Tray -c Debug
```

The tray host resolves the dashboard path by preferring the repository app build output and avoids activating mismatched stale `Porthole.App.exe` instances from unrelated locations.

The tray host starts a named pipe server and automatically launches the dashboard. You can then reopen the dashboard by double-clicking the tray icon.

**Alternative: Run components separately**

Dashboard only (requires tray to be running):
```powershell
dotnet run --project src/Porthole.App
```

Tray only (without auto-launching dashboard):
```powershell
dotnet run --project src/Porthole.Tray -c Debug
```

## Test

```powershell
dotnet test tests/Porthole.Core.Tests/Porthole.Core.Tests.csproj -c Debug
```

## Build Installer (WiX MSI)

Porthole includes a WiX v4 installer project intended for winget-friendly distribution.

```powershell
dotnet build src/Porthole.Installer/Porthole.Installer.wixproj -c Release -p:ProductVersion=1.0.0 -p:Platform=x64
```

What this does:
- Publishes framework-dependent (not self-contained) `Porthole.App` and `Porthole.Tray` payloads for the selected architecture
- Stages payload files under `src/Porthole.Installer/obj/<Configuration>/payload`
- Produces an MSI named `Porthole-<version>-<arch>.msi` under `src/Porthole.Installer/bin/<arch>/<Configuration>`

Winget notes:
- Use the MSI URL from GitHub Releases in your winget manifest (`InstallerType: msi`)
- Keep `PackageVersion` aligned with `-p:ProductVersion` used for the MSI build
- The MSI supports standard silent install switches used by winget (`/quiet` and `/qn`)

## CI

A GitHub Actions workflow runs tests on pull requests:

- `.github/workflows/tests.yml`

Release automation documentation:

- `docs/release-workflow.md`

## Development Notes

### Adding a New Feature

1. **Define the model** in `Porthole.Core/Models/` (e.g., `MyFeatureSnapshot.cs`)
2. **Create the service interface** in `Porthole.Core/Services/` (e.g., `IMyFeatureService.cs`)
3. **Implement named pipe client** in `Porthole.Core/Services/NamedPipe/NamedPipe<Feature>Service.cs`
4. **Create the ViewModel** in `Porthole.Core/ViewModels/<Feature>ViewModel.cs`
5. **Implement backend logic** in `Porthole.Tray/Services/WslcBackendService.cs`
6. **Add pipe operation handlers** in `Porthole.Tray/Services/NamedPipeImageCatalogServer.cs`
7. **Create the Page** in `Porthole.App/Pages/<Feature>Page.xaml[.cs]`
8. **Register in DI** in `Porthole.App/App.xaml.cs`
9. **Add navigation** in `Porthole.App/MainWindow.xaml[.cs]`

### Port Binding Enumeration

Port bindings are discovered by:
1. Getting list of running containers via `wslc list --all --format json`
2. Filtering for State=2 (Running)
3. For each container, calling `wslc inspect <container-name>` to get full details
4. Parsing the `Ports` JSON object (e.g., `"80/tcp": [{"HostPort": "8080"}]`)
5. Building `PortBinding` records with container name, host port, container port, and protocol

### Session Storage

Sessions are stored in the WSL Containers SDK with:
- `SessionSettings sessionSettings = CreateDefaultSessionSettings(name)` — generates new settings
- `Session session = new Session(sessionSettings)` — creates session instance
- `session.Start()` — initializes the session filesystem and runtime
- Active session is tracked in `_activeSessionName` (in-memory; resets on tray restart)
