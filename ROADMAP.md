# Porthole Roadmap

This document captures planned features beyond the current implementation.

---

## Completed ✅

- **Session & Environment Isolation** — Multi-session management with create/switch/delete operations; active session context for all container operations
- **Networking & Proxy Inspector** — Network mode toggle (Bridge/Consomme), real-time port binding enumeration, host proxy configuration display

---

## Planned: Run Wizard (Container Creation)

### Overview

The Run Wizard provides an interactive UI for creating and launching new containers without
needing to memorize `wslc run` command syntax. It guides users through configuration options
with real-time validation and sensible defaults.

### Planned Features

**Run Wizard Page (`RunWizardPage.xaml`)**

- Multi-step form: basic settings → advanced settings → review → create
- **Step 1: Basic Settings**
  - Container name (auto-validated for uniqueness in active session)
  - Image selection (dropdown from pulled images)
  - Startup command (optional; defaults to image entrypoint)
- **Step 2: Advanced Settings**
  - Port mappings (host:container format with protocol)
  - Environment variables (key=value pairs, can add multiple)
  - Volume mounts (select from named volumes or bind-mount paths)
  - Memory and CPU limits (optional)
- **Step 3: Review & Create**
  - Summary of all settings
  - Create button with progress indicator
  - Success/failure message with container ID

**RunWizardViewModel**

- State management for multi-step form
- Validation for container name, port format, env var format
- Image fetching from backend
- Create container operation integration

**Backend Integration**

- New named pipe operation: `CreateContainer`
- Backend method: `WslcBackendService.CreateContainerAsync(containerConfig)`
- Uses `session.RunAsync(...)` from the WSL Containers SDK

### Constraints

- All container creation must go through tray backend (named pipe)
- Form validation on client-side for responsiveness; re-validate on backend
- Port mappings use standard Docker syntax (`8080:80` or `8080:80/udp`)

---

## Planned: Volume & virtiofs Management

### Background

WSL containers use **virtiofs** as their default container filesystem. virtiofs exposes
Windows host directories to Linux containers using a high-performance shared memory
transport rather than the traditional Plan 9 (9P) protocol. This results in dramatically
lower latency for file-intensive workloads compared with bind-mounts in legacy WSL.

Because the container engine, the host OS, and the Linux VM all participate in virtiofs
mounts, tracking active mounts and diagnosing issues requires tooling that spans all three
layers — exactly what a native desktop manager like Porthole is well positioned to provide.

### Planned Features

**Volume List View (`VolumesPage.xaml`)**

- List all named volumes and bind-mount paths known to the active session.
- Show for each volume: name, driver (`virtiofs` or `local`), mount point inside the
  container, host path (for bind mounts), and size on disk.
- Indicate whether the volume is currently in use by a running container.

**Volume Operations**

- Create a named volume (name, optional driver options).
- Delete an unused volume with a confirmation dialog.
- Prune volumes not attached to any container (equivalent to `docker volume prune`).

**Mount Telemetry**

- Show active virtiofs mounts reported by the session alongside the Windows host path.
- Surface mount options (read-only, read-write) and estimated throughput class.

**Run Wizard Integration**

- When the Run Wizard is implemented, allow selecting a host directory to bind-mount into
  a new container and automatically flag whether the path will use virtiofs or 9P.

### Constraints

- Volume operations must go through the tray backend via named pipe; no direct SDK calls
  from `Porthole.App`.
- `Porthole.Core` must not reference `Microsoft.WSL.Containers` directly.

---

## Planned: Enterprise Policy & Registry Governance

### Background

WSL containers are manageable through the Microsoft enterprise device management stack,
enabling organisations to control which container registries developers can pull from and
to monitor container activity with Microsoft Defender for Endpoint.

Key mechanisms:

1. **Registry Allowlists via Intune / MDM**
   Administrators can deploy registry allowlist policies as MDM configuration profiles.
   Porthole should surface the active policy so developers understand why a pull might be
   blocked before they attempt it.

2. **Microsoft Defender for Endpoint (MDE) Integration**
   MDE monitors Linux container events through the WSL subsystem, providing visibility into
   process executions, network connections, and file-system activity inside containers.
   Porthole can surface the local Defender sensor status and link to the MDE portal for
   incident investigation.

3. **Audit Logging**
   Container lifecycle events (start, stop, create, delete) should be optionally written to
   a local append-only log for developer auditing and support troubleshooting.

### Planned Features

**Policy Inspector Panel (inside Settings or a dedicated Governance page)**

- Read and display the active registry allowlist from the local MDM policy store
  (`HKLM\SOFTWARE\Policies\...` key TBD pending SDK/documentation research).
- Show whether each registry a user attempts to pull from is permitted or blocked.
- Surface a "Policy Source" indicator (Intune-managed vs. unmanaged) so developers know
  whether to contact IT or edit settings locally.

**Defender for Endpoint Status Widget**

- Display the local MDE sensor state (active / not enrolled / disabled).
- Show the date of the last container-event report sent to MDE (if available via local API).
- Provide a direct link to the Microsoft 365 Defender portal for incident investigation.

**Audit Log Viewer**

- Toggle audit logging on or off in Settings.
- Write structured JSON log entries to `%LOCALAPPDATA%\Porthole\audit.log` on container
  lifecycle events.
- Provide a simple log viewer page within the app with filtering by date, session, and
  event type.

### Constraints

- Policy reads must use the Windows Registry or MDM bridge API; do not depend on
  undocumented Intune internals.
- Defender integration must be read-only from Porthole's side — no action should be taken
  that could interfere with endpoint protection.
- Audit logging must be opt-in and default to off.
