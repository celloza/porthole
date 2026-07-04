<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/portholelogowithname-dark.svg">
    <source media="(prefers-color-scheme: light)" srcset="assets/portholelogowithname.svg">
    <img alt="My Logo" src="assets/logo-light.svg" width="200">
  </picture>
</div>

Porthole is a native Windows desktop dashboard for WSL Containers.

It uses a WinUI 3 application for the UI and a tray-hosted backend for container and image operations. The app and tray communicate over named pipes using typed JSON contracts in `Porthole.Core`.

## Projects

- `src/Porthole.App`: WinUI 3 desktop dashboard
- `src/Porthole.Core`: shared models, service contracts, and viewmodels
- `src/Porthole.Tray`: tray host backend with WSL Containers integration
- `tests/Porthole.Core.Tests`: unit tests

## Prerequisites

- Windows 10/11
- .NET SDK 8.0+
- WSL with WSL Containers (`wslc`)

## Build

```powershell
dotnet build Porthole.slnx -c Debug
```

## Run

Dashboard:

```powershell
dotnet run --project src/Porthole.App
```

Tray host:

```powershell
dotnet run --project src/Porthole.Tray -c Debug
```

## Test

```powershell
dotnet test tests/Porthole.Core.Tests/Porthole.Core.Tests.csproj -c Debug
```

## CI

A GitHub Actions workflow runs tests on pull requests:

- `.github/workflows/tests.yml`
