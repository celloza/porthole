# VS Code Integration

Porthole now exposes two separate integration surfaces for VS Code:

- a Docker-compatible API bridge hosted by `Porthole.Tray`
- a lightweight Docker CLI compatibility shim hosted by `porthole-cli`

They solve different problems and can be used together.

## Integration Modes

### Containers Extension

The VS Code Containers extension works against the Docker-compatible API exposed by the tray host.

Current tray transports:

- HTTP: `http://127.0.0.1:23751/`
- named pipe: `docker_engine`
- named pipe: `dockerDesktopLinuxEngine`

For VS Code, the most reliable path today is the HTTP endpoint via `DOCKER_HOST`.

### Dev Containers Extension

The Dev Containers extension still expects a Docker CLI for several workflows. Porthole provides a CLI shim in `src/porthole-cli` for that surface.

The shim currently implements these commands:

- `version`
- `ps`
- `inspect`
- `run`
- `exec`

## Recommended VS Code Settings

Add these settings in your VS Code `settings.json`.

### Containers Extension Settings

```json
{
  "containers.environment": {
    "DOCKER_HOST": "tcp://127.0.0.1:23751"
  }
}
```

Why this matters:

- the Containers extension can run requests in processes that do not inherit your interactive shell environment
- setting `DOCKER_HOST` in `containers.environment` ensures the extension talks to the Porthole tray bridge consistently
- this avoids falling back to Docker Desktop or another local engine context by accident

### Dev Containers Extension Settings

Point the Dev Containers extension at the Porthole CLI shim:

```json
{
  "dev.containers.dockerPath": "C:\\porthole-cli\\porthole-cli.exe"
}
```

If you are running from source instead of an installed copy, use the built executable path for your environment.

Example:

```json
{
  "dev.containers.dockerPath": "C:\\repos\\porthole\\src\\porthole-cli\\bin\\Debug\\net8.0-windows10.0.19041.0\\porthole-cli.exe"
}
```

### Combined Example

```json
{
  "dev.containers.dockerPath": "C:\\porthole-cli\\porthole-cli.exe",
  "containers.environment": {
    "DOCKER_HOST": "tcp://127.0.0.1:23751"
  }
}
```

## Session Model

The active session is central to how Porthole behaves.

- the dashboard operates on the tray host's current active session
- the Docker-compatible API bridge resolves container, image, networking, and volume operations against that active session
- the shell now exposes the active session in the bottom status bar and allows switching it without opening the Sessions page

For Dev Containers support, Porthole also uses a dedicated session name:

- `porthole-devcontainers`

That session is used for the CLI shim and for devcontainer-oriented runtime operations.

## Hidden `wslc --session` Parameter

Porthole discovered and now depends on a hidden global `wslc` switch:

```powershell
wslc --session <name> <command>
```

This is important because unscoped `wslc` calls do not reliably target the session that VS Code integrations are using.

Examples:

```powershell
wslc --session porthole-devcontainers list --all --format json
wslc --session porthole-devcontainers inspect <container-id>
wslc --session porthole-devcontainers exec <container-id> sh -lc "echo hello"
wslc --session porthole-devcontainers images --format json
```

Porthole uses this session-scoped path in the tray backend for:

- container listing
- container inspect
- logs
- exec
- start, stop, and remove
- image enumeration for Docker API responses
- volume and networking inspection that must stay session-local

Without `--session`, the Docker-compatible API can resolve the wrong inventory or miss the devcontainer session entirely.

## Supported Docker API Calls

These routes are currently implemented by the tray bridge:

- `GET /_ping`
- `HEAD /_ping`
- `GET /version`
- `GET /info`
- `GET /networks`
- `GET /images/json`
- `GET /images/{id}/json`
- `GET /volumes`
- `GET /containers/json`
- `GET /containers/{id}/json`
- `GET /containers/{id}/logs`
- `POST /containers/create`
- `POST /containers/{id}/start`
- `POST /containers/{id}/stop`
- `DELETE /containers/{id}`
- `POST /containers/{id}/exec`
- `POST /exec/{id}/start`

## Outstanding Work

The bridge is intentionally narrow today. The following areas still need more work for broader Docker compatibility:

- additional image lifecycle endpoints beyond list and inspect
- richer container mutation endpoints beyond create, start, stop, remove, and exec
- streaming exec attach semantics beyond the current text response flow
- broader volumes and networks compatibility with the full Docker Engine API surface
- compose-oriented workflows if VS Code or other clients require them
- a dedicated integration test layer that exercises the real tray plus a live WSL Containers session

## Validation and Troubleshooting

### Verify the Tray Bridge

```powershell
Test-NetConnection 127.0.0.1 -Port 23751
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:23751/v1.52/_ping
```

### Verify the CLI Shim

```powershell
porthole-cli version
porthole-cli ps --all
```

### Common Failure Modes

If the Containers extension still does not work:

- confirm `Porthole.Tray` is running
- confirm `containers.environment.DOCKER_HOST` points to `tcp://127.0.0.1:23751`
- confirm no other Docker context is overriding that setting for extension-owned processes

If Dev Containers still fails:

- confirm `dev.containers.dockerPath` points to the actual `porthole-cli.exe`
- confirm the shim binary matches the version you built or installed
- confirm the devcontainer workload is targeting the expected session

If session-local inventory looks wrong:

- verify the active session in the Porthole window footer
- verify session-scoped `wslc --session <name>` commands return the expected inventory