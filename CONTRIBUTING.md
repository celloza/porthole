# Contributing to Porthole

Thank you for your interest in contributing to Porthole! This document outlines the guidelines and processes for contributing to the project.

## Branch Protection Rules

### Main Branch Policy

The `main` branch is protected with the following rules:

- ✅ **Pull Request Required**: All changes must go through a pull request and be reviewed before merging
- ✅ **Signed Commits Required**: All commits must be GPG-signed or S/MIME-signed for authenticity and traceability
- ✅ **Semantic Commit Messages**: Commits must follow conventional commit format for clear project history

### Bypass Restrictions

Only repository administrators can bypass these rules in emergencies. Contact a maintainer if needed.

## Workflow

### 1. Create a Feature Branch

Branch names should be descriptive and related to the work:

```bash
git checkout -b feature/sessions-ui
git checkout -b fix/port-binding-enumeration
git checkout -b docs/update-readme
```

Avoid naming branches `main`, `develop`, `master`, or committing directly to `main`.

### 2. Setup Commit Signing

#### GPG Signing (Recommended for Linux/macOS)

```bash
# Generate a GPG key if you don't have one
gpg --full-generate-key

# List your keys and get the key ID
gpg --list-secret-keys --keyid-format=long

# Configure Git to use your key
git config --global user.signingkey YOUR_KEY_ID
git config --global commit.gpgsign true

# Export public key to GitHub (Settings → SSH and GPG keys)
gpg --armor --export YOUR_KEY_ID
```

#### Signed Commits on Windows

On Windows with Git Bash or PowerShell, GPG signing works the same way. Alternatively:

```bash
# Using Windows's native certificate store
git config user.signingkey <thumbprint>
git config gpg.x509.program gpgsm
git config gpg.format x509
```

#### Verify Signing is Enabled

```bash
# Create a test commit with signing
git commit -m "test: verify signing" --allow-empty -S

# Verify the signature
git log --show-signature -1
```

### 3. Semantic Commit Messages

Follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

#### Commit Types

- **feat**: A new feature
- **fix**: A bug fix
- **docs**: Documentation changes (README, comments, guides)
- **style**: Code style changes (formatting, missing semicolons, etc.)
- **refactor**: Code refactoring without feature changes or bug fixes
- **perf**: Performance improvements
- **test**: Adding or updating tests
- **chore**: Build configuration, dependencies, tooling
- **ci**: CI/CD configuration and scripts

#### Examples

```bash
# Feature
git commit -m "feat: add session isolation for multi-user support"

# Bug fix
git commit -m "fix: correct port binding state check for running containers"

# With scope
git commit -m "feat(ui): add networking page with port binding display"

# With body and footer
git commit -m "refactor(backend): simplify container enumeration logic

- Extract port binding parsing into separate method
- Add comprehensive error handling
- Improve performance with batch operations

Closes #42"
```

#### Commit Signing with Message

```bash
# Sign your commit (git will prompt for passphrase if needed)
git commit -S -m "feat: your feature message"

# Or if gpg.sign is globally enabled:
git commit -m "feat: your feature message"
```

### 4. Create a Pull Request

When pushing your branch:

```bash
git push origin feature/your-feature-name
```

Then create a PR via GitHub with:
- Clear title matching semantic commit format
- Description of changes and motivation
- Reference to related issues: `Closes #123`
- Screenshots or test results if applicable

### 5. Code Review & Merge

- Address feedback from reviewers
- Ensure all CI checks pass (builds, tests, linting)
- Squash or rebase commits if requested
- Once approved, a maintainer will merge your PR

## Development Setup

### Prerequisites

- .NET 8.0 SDK (target framework: `net8.0-windows10.0.19041.0`)
- Windows 10 (build 19041) or later
- WSL 2 with containers support
- Git with GPG signing configured

### Building

```bash
# Restore and build
dotnet build Porthole.slnx -c Debug

# Run the dashboard
dotnet run --project src/Porthole.App

# Run the tray service
dotnet run --project src/Porthole.Tray -c Debug
```

### Architecture

Porthole is organized into three main projects:

- **Porthole.App**: WinUI 3 dashboard (UI layer)
- **Porthole.Core**: Shared models, services, and viewmodels
- **Porthole.Tray**: Backend tray service with WSL Containers integration
- **Porthole.Core.Tests**: Unit tests

For detailed architecture information, see [copilot-instructions.md](.github/copilot-instructions.md).

### Testing

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Porthole.Core.Tests/Porthole.Core.Tests.csproj
```

## Code Style & Guidelines

### MVVM & Binding

- Use `CommunityToolkit.Mvvm` patterns for ViewModels
- Prefer DI-constructed services over singletons
- Use `[ObservableProperty]` for two-way binding properties
- Use `[RelayCommand]` for command methods

### Naming Conventions

- Classes, methods, properties: `PascalCase`
- Private fields: `_camelCase`
- Constants: `UPPER_SNAKE_CASE` or `PascalCase`
- Async methods: Suffix with `Async`

### Documentation

- Add XML doc comments to public types and methods
- Update README.md for user-facing changes
- Update ROADMAP.md to reflect completed features
- Keep copilot-instructions.md in sync with architecture changes

## Reporting Issues

Before submitting an issue, check if it already exists. When reporting:

1. Use a clear, descriptive title
2. Describe the problem and expected behavior
3. Include steps to reproduce
4. Add screenshots or logs if relevant
5. Mention your Windows and .NET versions

## Questions?

- Open a discussion on GitHub
- Check existing issues and documentation
- See [README.md](README.md) for project overview

Thank you for contributing! 🚀
