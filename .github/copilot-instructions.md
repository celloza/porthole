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

- Shell/navigation: implemented.
- Home/dashboard page: implemented.
- Images page: implemented and backed by the tray host through named pipes.
- Containers page: still mostly a placeholder.
- Run Wizard page: still a placeholder.
- Settings page: placeholder/lightweight.
- Image prune support is currently limited by the SDK surface used in the tray backend.

## WSL and prerequisites

- WSL prerequisite detection should check common install locations and PATH, not only a single hard-coded path.
- Keep prerequisite messaging accurate; avoid false negatives when `wsl.exe` or `wslc.exe` is installed and available.

## Change safety rules

- Prefer small, surgical edits over broad refactors.
- Preserve the App/Core/Tray boundaries.
- Do not replace named-pipe integration with direct UI-to-SDK calls.
- Do not regress tray behavior: tray startup, tray double-click, and existing-window activation should keep working.
- When changing image operations, verify both the UI flow and the tray backend flow.

## Useful validation paths

- Full solution build: `dotnet build Porthole.slnx -c Debug`
- Run dashboard directly: `dotnet run --project src/Porthole.App`
- Run tray host directly: `dotnet run --project src/Porthole.Tray -c Debug`

## When extending the app

- For new container or image actions, start with shared contracts and service interfaces in `Porthole.Core`.
- Implement backend behavior in `Porthole.Tray`.
- Expose the operation to the dashboard through the named-pipe client.
- Keep the WinUI layer focused on presentation, command flow, and status feedback.
