# Daycord Release Guide

**Target version:** 0.1.1  
**Supported release targets:** macOS ARM64 and Windows x64

## 1. Overview

This guide describes how to build, package, verify, and distribute Daycord.

The release workflow is integrated into MSBuild and runs on macOS.

It produces:

- macOS DMG.
- macOS portable ZIP containing `Daycord.app`.
- Windows NSIS installer.
- Windows portable ZIP.
- SHA-256 checksum manifest.

The application version in the current `develop` branch is 0.1.1.

A version being configured in source code does not mean that its release has already been published.

## 2. Supported Platforms

| Platform | Runtime Identifier | Architecture |
|---|---|---|
| macOS | `osx-arm64` | Apple Silicon |
| Windows | `win-x64` | x64 |

Linux and other architectures are outside the official release workflow.

The existing distribution packaging scripts run on macOS.

Windows packages are cross-published from macOS but must be verified on Windows.

## 3. Release Prerequisites

The build host requires:

- macOS.
- .NET SDK compatible with `global.json`.
- Git.
- macOS command-line utilities.
- Swift.
- Finder automation support.
- NSIS compiler (`makensis`) for Windows installer creation.

The repository specifies .NET SDK 10.0.201 with latest-patch roll-forward.

Verify the SDK:

```bash
dotnet --info
```

The scripts use macOS utilities including:

```text
ditto
hdiutil
diskutil
osascript
swift
unzip
shasum
plutil
```

The DMG workflow requires a macOS environment capable of running its Finder automation.

### Installing NSIS

With Homebrew:

```bash
brew install makensis
```

Verify installation:

```bash
makensis -VERSION
```

The Windows installer script also searches standard Homebrew locations when the compiler is not available through PATH.

## 4. Version Management

The application version is defined centrally in:

```text
Directory.Build.props
```

For version 0.1.1:

```xml
<PropertyGroup>
    <DaycordVersion>0.1.1</DaycordVersion>
    <Version>$(DaycordVersion)</Version>
    <AssemblyVersion>$(DaycordVersion).0</AssemblyVersion>
    <FileVersion>$(DaycordVersion).0</FileVersion>
    <InformationalVersion>$(DaycordVersion)</InformationalVersion>
</PropertyGroup>
```

The version is used for:

- .NET assembly metadata.
- macOS application bundle metadata.
- Windows installer metadata.
- Distribution filenames.
- Release output directories.

The macOS `Info.plist` is updated using the centralized version during the build or publish process.

Do not maintain conflicting version numbers in separate packaging components.

## 5. Release Automation

Release targets are defined in:

```text
Directory.Build.targets
```

The main orchestration target is:

```text
CreateDaycordReleaseDistribution
```

It runs after Publish when:

- The project is `Daycord.Desktop`.
- Configuration is Release.
- The RID is `osx-arm64` or `win-x64`.
- MSBuild is running on macOS.

The workflow coordinates platform-specific packaging, archive creation, and checksum generation.

A normal `dotnet build` does not create the complete release distribution.

Use `dotnet publish` with the required runtime identifier.

## 6. Restore and Build

From the repository root:

```bash
dotnet restore Daycord.slnx
```

Build Debug:

```bash
dotnet build Daycord.slnx --configuration Debug
```

Build Release:

```bash
dotnet build Daycord.slnx --configuration Release
```

Run the automated verification projects as described in [Testing](testing.md).

Before packaging, confirm that the working tree contains only intended release changes:

```bash
git branch --show-current
git status --short
git log -1 --oneline
git diff --check
```

## 7. macOS ARM64 Release

Publish on macOS:

```bash
dotnet publish \
  src/Daycord.Desktop/Daycord.Desktop.csproj \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true
```

The existing MSBuild workflow creates the application bundle and distribution packages.

### Application Bundle

Expected bundle structure:

```text
Daycord.app/
└── Contents/
    ├── Info.plist
    ├── MacOS/
    │   ├── Daycord.Desktop
    │   └── ...
    └── Resources/
        └── icon.icns
```

The source metadata file is:

```text
src/Daycord.Desktop/Packaging/macOS/Info.plist
```

The application icon is:

```text
src/Daycord.Desktop/Assets/icon.icns
```

The bundling target updates the version metadata and validates `Info.plist`.

### DMG Generation

The automated DMG workflow uses:

```text
scripts/create-macos-dmg.sh
scripts/create-macos-dmg-background.swift
```

It creates a branded DMG containing:

- `Daycord.app`.
- An Applications folder link.

The script configures the Finder layout, produces a compressed read-only image, and verifies it before publication.

The DMG script is invoked automatically during the supported Release publish workflow.

Do not run it again unless packaging needs to be repeated explicitly.

### Expected macOS Artifacts

```text
artifacts/release/0.1.1/
├── Daycord-0.1.1-osx-arm64.dmg
└── Daycord-0.1.1-osx-arm64.zip
```

The ZIP contains the complete macOS application bundle.

## 8. Windows x64 Release

Publish from macOS:

```bash
dotnet publish \
  src/Daycord.Desktop/Daycord.Desktop.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true
```

The existing MSBuild workflow invokes the NSIS installer and portable ZIP packaging steps.

### Installer Generation

The Windows installer uses:

```text
scripts/create-windows-installer.sh
scripts/windows/daycord-installer.nsi
```

The wrapper validates the Windows publish output and invokes `makensis`.

The installer is built from the complete self-contained publish directory.

### Installation Behavior

The Windows installer:

- Installs to `$PROGRAMFILES64\Daycord`.
- Requires administrator privileges.
- Creates Start Menu shortcuts.
- Registers the application under the machine-wide uninstall registry key.
- Includes an uninstaller.
- Does not automatically start Daycord from the elevated setup process.

### Reinstallation

When an existing installation is detected, Setup uses the previous uninstaller before installing the replacement files.

If an application executable is found without a recognized uninstaller, Setup stops rather than blindly replacing it.

The user's SQLite database is stored separately from the installation directory.

### Uninstallation

The uninstaller removes the installed application files, shortcuts, and registration entries.

It does not intentionally remove:

```text
%LOCALAPPDATA%\Daycord
```

User tasks, application settings, and database backups are intended to survive application uninstallation.

### Expected Windows Artifacts

```text
artifacts/release/0.1.1/
├── Daycord-Setup-0.1.1-win-x64.exe
└── Daycord-0.1.1-win-x64.zip
```

The portable ZIP contains the Windows application files.

The Windows installer and portable build must be checked on Windows x64.

## 9. Release Archives and Checksums

Archive creation is implemented in:

```text
scripts/create-release-archives.sh
```

The script:

1. Validates the publish directory.
2. Verifies required application files.
3. Rejects unexpected private data and packaging files.
4. Creates a ZIP archive using `ditto`.
5. Validates ZIP integrity and required executable entries.
6. Rejects unexpected macOS metadata.
7. Publishes the verified archive.
8. Generates a SHA-256 manifest for available release artifacts.

The script rejects content such as:

- SQLite databases and sidecar files.
- Database backups.
- Excel reports.
- Private keys and signing files.
- Unexpected logs and archive files.

Do not disable these protections to force packaging to complete.

### SHA-256 Verification

The checksum manifest is:

```text
SHA256SUMS.txt
```

After publishing both platforms:

```bash
cd artifacts/release/0.1.1
shasum -a 256 -c SHA256SUMS.txt
```

Verify that the manifest contains all four distribution artifacts.

SHA-256 validation confirms artifact integrity against the manifest.

It does not independently authenticate the publisher.

## 10. Expected Release Output

A complete 0.1.1 distribution has this structure:

```text
artifacts/
└── release/
    └── 0.1.1/
        ├── Daycord-0.1.1-osx-arm64.dmg
        ├── Daycord-0.1.1-osx-arm64.zip
        ├── Daycord-Setup-0.1.1-win-x64.exe
        ├── Daycord-0.1.1-win-x64.zip
        └── SHA256SUMS.txt
```

These are expected output filenames, not a claim that the artifacts have already been published.

## 11. Rider Publishing

JetBrains Rider can invoke the same MSBuild publishing workflow.

Configure:

| Setting | Value |
|---|---|
| Project | `Daycord.Desktop` |
| Configuration | `Release` |
| Target framework | `net10.0` |
| Runtime Identifier | `osx-arm64` or `win-x64` |
| Self-contained | Enabled |

On macOS, the supported Publish configuration triggers the release packaging targets.

Check the publishing output for failures.

Do not duplicate packaging steps that the configured targets have already performed.

## 12. Signing and Notarization

The current release workflow does not configure:

- Apple Developer ID signing.
- Apple notarization.
- Windows Authenticode signing.

Unsigned application packages may trigger operating-system security warnings.

Do not describe these packages as signed, notarized, or verified by a trusted publisher.

A future signing workflow must be implemented and validated separately.

The current DMG workflow is not a Mac App Store distribution workflow.

## 13. Updating from 0.1.0 to 0.1.1

Daycord stores application data separately from executable files.

An ordinary update does not require deleting or recreating the SQLite database.

### Preparation

Before installing 0.1.1:

1. Open the existing application.
2. Create a database backup through Settings.
3. Store the backup in a safe independent location.
4. Close Daycord.
5. Install or extract the 0.1.1 package.
6. Start Daycord.
7. Verify existing tasks, time records, settings, and presets.

### Database Compatibility

Database initialization validates recognized migration history and applies supported pending migrations.

Version 0.1.1 does not require a new SQLite schema migration for the backup and recovery features.

The currently embedded migrations remain:

```text
001_InitialSchema.sql
002_ApplicationSettings.sql
```

Do not remove existing migration history or delete the database as part of an application update.

### Rollback Considerations

Installing an older application binary does not automatically downgrade a newer database schema.

Before any rollback, check compatibility between the application version and the database migration history.

A compatible pre-update database backup may be required.

Do not modify migration checksums to bypass version compatibility rules.

## 14. Release Verification

Before publishing a release, verify:

### Build and Automated Checks

- Debug and Release builds.
- Automatic backup checks.
- Emergency retry checks.
- Recovery checks.

### macOS

- Application bundle structure.
- Bundle version metadata.
- DMG mounting and installation.
- ZIP extraction.
- Startup on an Apple Silicon Mac.
- Data persistence across restarts.

### Windows

- NSIS installer execution.
- Start Menu shortcuts.
- Startup on Windows x64.
- Portable ZIP startup.
- Data persistence across restarts.
- Reinstallation and uninstallation without user-data loss.

### Application Behavior

- Tasks, planning periods, and actual work entries.
- Calendar and date navigation.
- Reports and Excel export.
- Language and theme switching.
- Backup creation and restoration.
- Cross-version update with existing data.

### Artifacts

- All four expected packages are present.
- Both ZIP archives pass validation.
- The DMG passes verification.
- All SHA-256 checksums match.
- No private databases, backups, reports, or secrets are included.

For detailed testing procedures, see [Testing](testing.md).

## 15. Publication

Once the release candidate is approved:

1. Confirm the intended Git commit.
2. Confirm the version and generated artifacts.
3. Verify the final checksum manifest.
4. Create the appropriate Git release tag.
5. Publish the approved artifacts through GitHub Releases.
6. Include the corresponding release notes.
7. Ensure the published version matches the artifact filenames.

Release notes are maintained separately from this guide.

Do not publish a release solely because packaging completed successfully.

Platform verification and project-owner approval are separate requirements.

## 16. Related Documentation

- [Development](development.md)
- [Architecture](architecture.md)
- [Database](database.md)
- [Backup and Recovery](backup-and-recovery.md)
- [Testing](testing.md)
- [User Guide](user-guide.md)