# Volume Management

This document explains how Porthole models storage in the active WSL Containers session and what the Volumes page shows.

## Overview

Porthole surfaces two storage attachment types:

1. Named volumes managed by `wslc volume`.
2. Bind mounts discovered by inspecting container mount metadata.

The Volumes page combines both into one view so you can see what data is attached to the active session without switching between container inspect output and volume commands.

## What the Volumes page shows

Each row includes:

- Source or volume identity
  - Named volume name for managed volumes.
  - Host path for bind mounts when one is available.
- Target path inside the container.
- Driver label.
- Size label.
- Mount telemetry:
  - whether the row is a named volume or bind mount,
  - whether it is read-only or read-write,
  - throughput class,
  - whether it is currently in use.

## Named volumes

Named volumes come from `wslc volume ls --format json` and are scoped to the active session.

Supported operations:

- Create a named volume.
- Copy a mount string for a named volume or bind mount.
- Delete an unused named volume.
- Prune unused named volumes.

Copy mount string behavior:

- Each row includes a copy action that places a ready-to-use mount string on the clipboard.
- Named volumes copy as `volumeName:/container/path` when a target path is known.
- Bind mounts copy as `hostPath:/container/path`.
- This is intended to make it easy to move from inspection on the Volumes page to container creation in the Run Wizard or another tool.

Deletion rules:

- Porthole blocks deletion of named volumes that are still attached to running containers.
- Deletion requires confirmation in the UI.

## Bind mounts and virtiofs

Porthole inspects containers to discover bind mounts and labels them separately from named volumes.

Current heuristics:

- Windows host paths such as `C:\data:/app/data` are labeled as `virtiofs`.
- UNC host paths are also treated as Windows-hosted virtiofs mounts.
- Linux or WSL-style paths such as `/mnt/c/data:/app/data` are labeled as `9P`-style host sharing.

This is intended as operator guidance, not a low-level runtime guarantee. The exact data plane depends on the underlying runtime and how the mount was configured.

Bind mount behavior in the Volumes page:

- Bind mounts are shown for visibility and telemetry.
- Their mount strings can be copied directly from the row action.
- They are not deleted from the Volumes page.
- Removing a bind mount requires changing the container configuration and recreating or updating the container.

## Run Wizard integration

The Run Wizard accepts the same mount syntaxes used by the backend:

- Named volume: `myvolume:/app/data`
- Windows host path bind mount: `C:\data:/app/data`
- Windows host path bind mount, read-only: `C:\data:/app/data:ro`
- Linux or WSL path bind mount: `/mnt/c/data:/app/data`

Wizard behavior:

- The mount input displays a live hint describing whether the source will be treated as a named volume, virtiofs bind mount, or 9P-style mount.
- A host-folder picker can prefill a Windows bind mount source.
- The review step annotates each mount with its detected transport class.
- Mount strings copied from the Volumes page can be pasted directly into the wizard's volume mount input.

## Limitations

- Bind mount size is currently shown as a host-path placeholder rather than a measured on-disk size.
- Throughput class is a lightweight operator label derived from mount type, not a benchmark.
- The Volumes page shows bind mounts discovered from container inspect output; if inspect data is unavailable for a container, that row may be absent.