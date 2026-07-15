# Porthole Copilot Instructions

## Product direction

- Build Porthole as a pure native Windows desktop app.
- Use WinUI 3 for the dashboard UI.
- Do not introduce web-based wrappers such as Electron, WebView shells, or browser-hosted UI.
- Do not pivot this project to MAUI unless the user explicitly asks for that change again.
- Keep the visual direction modern, desktop-first, and Windows-native.

## Core technical constraints

- Target `net8.0-windows10.0.19041.0` across the solution unless the user explicitly approves a framework migration.
- Keep `Microsoft.WSL.Containers` isolated to the tray/backend side of the solution.
- Do not add `Microsoft.WSL.Containers` references to the WinUI app project or to shared UI-facing layers.
- Do not scrape `stdout` from `wslc` for core functionality when the SDK can provide the data directly.
- Prefer typed integrations and explicit contracts over shell parsing.

## Solution architecture

The solution currently has three main projects:

### `src/Porthole.App`

- WinUI 3 dashboard application.
- Owns shell, navigation, pages, window behavior, and desktop UX.
- Uses dependency injection and MVVM.
- Should stay focused on UI orchestration and app lifecycle.

### `src/Porthole.Core`

- Shared models, service abstractions, IPC contracts, and viewmodels.
- Safe place for app-facing interfaces and DTOs.
- Must remain free of direct `Microsoft.WSL.Containers` dependencies.

### `src/Porthole.Tray`

- Background tray host and backend process.
- Owns real container/image operations through `Microsoft.WSL.Containers`.
- Hosts the named-pipe server used by the dashboard.
- Double-clicking the tray icon should open or foreground the dashboard.
- Tray startup currently auto-launches the dashboard and reuses an existing dashboard window when possible.

## IPC rules

- The app and tray communicate through named pipes using JSON request/response/progress envelopes.
- Shared contracts live under `src/Porthole.Core/Services/NamedPipe`.
- If you add or change an operation, update both the client and server sides together.
- Preserve typed progress reporting for long-running operations such as image pulls.

**Operation Codes:**
- Image operations (0-19): List, Pull, Delete, Tag, Prune
- Session operations (20-24): ListSessions, CreateSession, DeleteSession, SetActiveSession, GetActiveSession
- Networking operations (30-31): GetNetworkingSnapshot, SetNetworkMode

**Async Operations:**
- Long-running operations like `PullImage` use `ExecuteWithTimeoutAsync` and yield progress updates
- Synchronous-looking operations like `DeleteImage` complete without progress tracking
- Port binding enumeration (`GetNetworkingSnapshot`) is async to handle JSON parsing and multiple container inspections
- Always await async operations in pipe server handlers; use `cancellationToken` for timeout safety

## UI and MVVM expectations

- Use `CommunityToolkit.Mvvm` patterns for viewmodels and commands.
- Prefer DI-constructed services and viewmodels over ad-hoc singletons.
- Keep page code-behind light; business logic belongs in viewmodels or services.
- Avoid duplicate top-level page titles when the shell header already provides the page title.

## Windows App SDK and startup guidance

- The dashboard is configured for unpackaged local runs.
- Preserve the current `Porthole.App.csproj` startup-related settings unless there is a strong reason to change them:
  - `WindowsPackageType=None`
  - `EnableWinAppRunSupport=false`
  - `WindowsAppSdkBootstrapInitialize=true`
  - `WindowsAppSdkDeploymentManagerInitialize=false`
- Be careful when changing app startup code because this project previously hit pre-managed startup failures.

## Build and run expectations

- Prefer validating changes with `dotnet build Porthole.slnx -c Debug`.
- If `Porthole.Tray.exe` is already running, rebuilds of the tray project can fail because Windows locks the executable.
- When running the tray locally with `dotnet run`, the tray now resolves `Porthole.App.exe` from either:
  - its own output directory, or
  - the dashboard project's build output under `src/Porthole.App/bin/...`
- If you change output paths or runtime identifiers, verify tray-to-dashboard launch behavior still works.

## Current implementation status

**Implemented:**
- ✅ Shell/navigation: full page routing and menu structure
- ✅ Dashboard page: real-time system metrics and container status
- ✅ Images page: pull, tag, delete with progress tracking
- ✅ Containers page: start, stop, remove with inspection
- ✅ **Sessions page**: multi-session management with create/switch/delete operations
- ✅ **Networking page**: network mode toggle, port binding display, proxy configuration
- ⏳ Settings page: placeholder/lightweight
- ⏳ Run Wizard page: planned for next iteration

**Architectural Patterns in Use:**
- Multi-session support: All container operations target the active session context (set via `_activeSessionName` in backend)
- Session storage: WSL Containers SDK manages session filesystem; `_sessionSettings` dict tracks name→path mapping as workaround for Session.Settings not being exposed
- Port binding enumeration: Iterates running containers (State=2), calls `wslc inspect <name>` for each, parses Ports JSON object
- Network mode: Toggle between Bridge and Consomme stored in `_networkMode` field; persists only during tray session lifetime
- Proxy detection: Reads from Windows environment variables (HTTP_PROXY, HTTPS_PROXY, NO_PROXY) on demand

## WSL and prerequisites

- WSL prerequisite detection should check common install locations and PATH, not only a single hard-coded path.
- Keep prerequisite messaging accurate; avoid false negatives when `wsl.exe` or `wslc.exe` is installed and available.

## Change safety rules

- Prefer small, surgical edits over broad refactors.
- Preserve the App/Core/Tray boundaries.
- Do not replace named-pipe integration with direct UI-to-SDK calls.
- Do not regress tray behavior: tray startup, tray double-click, and existing-window activation should keep working.
- When changing image operations, verify both the UI flow and the tray backend flow.
- If the Run Wizard template JSON schema changes, increment the template `version` value.
- Template loading must remain backward compatible with previously saved template versions.

## Useful validation paths

- Full solution build: `dotnet build Porthole.slnx -c Debug`
- Run dashboard directly: `dotnet run --project src/Porthole.App`
- Run tray host directly: `dotnet run --project src/Porthole.Tray -c Debug`

## When extending the app

### General Pattern

- For new container or image actions, start with shared contracts and service interfaces in `Porthole.Core`.
- Implement backend behavior in `Porthole.Tray`.
- Expose the operation to the dashboard through the named-pipe client.
- Keep the WinUI layer focused on presentation, command flow, and status feedback.

### Multi-Session Operations

When adding features that operate on containers or images:

1. Backend methods should use `GetActiveSessionInstance()` to get the current session context
2. Verify container/image state checks use correct enum values (e.g., State=2 for Running, not State=1)
3. All session-aware operations must go through the active session, not a static/singleton instance
4. When listing resources (containers, images, volumes), they are automatically scoped to the active session

### Port Binding Enumeration Pattern

When discovering port bindings or other container metadata:

1. Get list of containers via `wslc list --all --format json` (already provides basic info and state)
2. For detailed inspection (ports, mounts, network config), call `wslc inspect <container-name>` which returns array with one element
3. Parse JSON carefully:
   - Container `Ports` field is object: `{"80/tcp": [{"HostPort": "8080"}]}`
   - Extract protocol from key (e.g., "80/tcp" → containerPort=80, protocol="tcp")
   - Array value contains HostPort string that must be parsed as int
4. Return results as strongly-typed records (e.g., `PortBinding`) for UI binding

### Session Context Management

- Active session name is tracked in `_activeSessionName` (backend state)
- When creating/switching sessions, create new Session instance and store in `_sessions` dictionary
- Session.Settings is not exposed by SDK → use `_sessionSettings` dictionary to track name→storagePath mappings
- Session lifetime: Created on demand when referenced, persists until deleted
- Sessions are persisted to registry at `%LOCALAPPDATA%\Porthole\sessions.json` containing `activeSessionName` and `knownSessionNames` array
- **Active session persists across tray restarts** via registry (changed from previous behavior)

### The (Default) Session

The `(Default)` session (represented by constant `UnnamedDefaultSessionName = "(Default)"`) represents the unnamed default WSL Containers session:

**When it appears:**
- On first run (no registry): If existing containers are detected in the default unnamed session via `wslc container list --all --format json`
- On upgrade or registry deletion: If named sessions exist on disk AND the default session has containers

**Key differences from named sessions:**
- Cannot be deleted via `DeleteNamedSession()` (throws InvalidOperationException)
- Commands use no `--session` parameter: `wslc list --all --format json` instead of `wslc --session Default list --all --format json`
- `GetSessionStoragePath("(Default)")` returns `"(default)"` (not a real filesystem path)
- `EnsureSessionInitialized("(Default)")` skips Session SDK initialization since WSL manages it
- Storage path is `"(default)"` indicating WSL-managed storage, not user-created

**Session selection priority on first run:**
1. Other named sessions (not Default, not porthole-devcontainers) if any exist
2. (Default) session if it has containers
3. First discovered named session (alphabetically)
4. porthole-devcontainers (hardcoded default if nothing else exists)

**Implementation details:**
- `HasDefaultUnnamedSessionOnFirstRun()` runs `wslc container list --all --format json` to detect if default session has containers
- `skipDefaultSessionDetection` parameter in constructor (used by tests) prevents this check
- `BuildSessionScopedWslcArguments()` checks if session name equals `UnnamedDefaultSessionName` and omits `--session` parameter if true

