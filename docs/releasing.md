# Daycord Release Guide

**Version:** 0.1.0  
**Release targets:** macOS ARM64 and Windows x64

## 1. Purpose

This document describes how to prepare, build, package, verify, and distribute Daycord releases.

Daycord uses MSBuild-integrated release automation for macOS and Windows.

The current packaging workflow runs on macOS and generates:

- A macOS application bundle.
- A macOS DMG installer image.
- A macOS ZIP archive.
- A Windows NSIS installer.
- A Windows portable ZIP archive.
- SHA-256 checksums.

This guide documents the existing version 0.1.0 release implementation.

## 2. Supported Release Platforms

| Target | Runtime Identifier | Architecture |
|---|---|---|
| macOS | `osx-arm64` | Apple Silicon |
| Windows | `win-x64` | x64 |

The official release workflow does not currently target Linux or additional operating-system architectures.

Although the desktop framework may support other platforms, those platforms are outside the Daycord 0.1.0 release scope.

## 3. Release Prerequisites

### 3.1 Build Host

The current packaging targets require macOS.

They use macOS tools to create the DMG, ZIP archives, and Windows installer.

Build preparation requires:

- macOS build environment.
- Compatible .NET 10 SDK.
- Git.
- Xcode command-line tools and relevant macOS utilities.
- NSIS (`makensis`) for Windows installer creation.

The required SDK version is declared in `global.json`.

### 3.2 Required Tools

The release scripts use tools including:

- `/bin/bash`
- `ditto`
- `unzip`
- `shasum`
- `hdiutil`
- `diskutil`
- `osascript`
- `swift`
- `file`
- `makensis`

The DMG script also uses macOS Finder automation and `/usr/libexec/PlistBuddy`.

### 3.3 NSIS Installation

On a macOS development machine with Homebrew:

```bash
brew install makensis
```

Verify that the compiler is available:

```bash
makensis -VERSION
```

The installer script checks the normal PATH and known Homebrew locations, including:

```text
/opt/homebrew/bin/makensis
/usr/local/bin/makensis
```

### 3.4 Release Environment

Before packaging:

1. Confirm the intended Git branch and commit.
2. Review uncommitted source changes.
3. Restore NuGet dependencies.
4. Build Debug and Release.
5. Review compiler warnings.
6. Verify that required application icons exist.
7. Confirm that the release version is correct.
8. Confirm that no user databases or secrets are present in the release inputs.

## 4. Version Management

Daycord uses a centralized version property.

File:

```text
Directory.Build.props
```

For version 0.1.0:

```xml
<PropertyGroup>
    <DaycordVersion>0.1.0</DaycordVersion>
    <Version>$(DaycordVersion)</Version>
    <AssemblyVersion>$(DaycordVersion).0</AssemblyVersion>
    <FileVersion>$(DaycordVersion).0</FileVersion>
    <InformationalVersion>$(DaycordVersion)</InformationalVersion>
</PropertyGroup>
```

The same version is used for:

- .NET assembly metadata.
- macOS bundle metadata.
- Windows installer metadata.
- Release artifact filenames.
- Release directory naming.

### 4.1 Future Version Updates

To prepare a future release:

1. Update `DaycordVersion`.
2. Review the assembly and product version convention.
3. Verify the generated `Info.plist`.
4. Verify Windows installer version metadata.
5. Publish each release target.
6. Confirm that output filenames contain the expected version.

Do not independently hard-code conflicting versions in scripts or packaging files.

Do not change the official version merely to simulate an upgrade test.

## 5. MSBuild Release Integration

The release workflow is defined in:

```text
Directory.Build.targets
```

The main target is:

```text
CreateDaycordReleaseDistribution
```

It runs after Publish when the following conditions hold:

- The project is `Daycord.Desktop`.
- Configuration is Release.
- Runtime Identifier is `osx-arm64` or `win-x64`.
- MSBuild is running on macOS.

The target coordinates:

1. macOS application bundling, when applicable.
2. Removal of unwanted Finder metadata.
3. macOS DMG generation, when applicable.
4. Windows NSIS installer generation, when applicable.
5. Portable ZIP creation and SHA-256 verification.

The platform-specific targets execute only when their conditions are satisfied.

A normal Debug or Release build does not create the complete release distribution. Use Publish with the required Runtime Identifier.

## 6. Restore and Build

From the repository root:

```bash
dotnet --info
```

```bash
dotnet restore Daycord.slnx
```

```bash
dotnet build Daycord.slnx -c Debug
```

```bash
dotnet build Daycord.slnx -c Release
```

Investigate any compiler errors before publishing.

Distinguish build warnings from actual errors and record unresolved warnings in the release report.

## 7. macOS ARM64 Release

### 7.1 Publish

From the repository root on macOS:

```bash
dotnet publish \
  src/Daycord.Desktop/Daycord.Desktop.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true
```

The existing MSBuild targets automatically package the result.

The command does not require manually invoking the DMG script afterward.

### 7.2 Application Bundle

The Desktop project contains the target:

```text
BundleDaycordMacApp
```

It creates the application bundle:

```text
Daycord.app/
├── Contents/
│   ├── Info.plist
│   ├── MacOS/
│   │   ├── Daycord.Desktop
│   │   └── ...
│   └── Resources/
│       └── icon.icns
```

The source bundle metadata is located at:

```text
src/Daycord.Desktop/Packaging/macOS/Info.plist
```

The macOS icon is:

```text
src/Daycord.Desktop/Assets/icon.icns
```

The bundling target updates version metadata using `DaycordVersion`.

It also validates the generated property list with `plutil`.

### 7.3 DMG Generation

Script:

```text
scripts/create-macos-dmg.sh
```

Background generator:

```text
scripts/create-macos-dmg-background.swift
```

The script:

1. Validates the application bundle.
2. Creates a temporary writable APFS disk image.
3. Copies `Daycord.app`.
4. Creates an Applications folder link.
5. Generates the branded background.
6. Configures Finder layout and icon positions.
7. Produces a compressed read-only DMG.
8. Verifies the resulting disk image.
9. Publishes the completed artifact.

The DMG design is part of the accepted Daycord distribution workflow.

Do not change it without an explicit design requirement.

### 7.4 macOS Artifacts

Expected outputs:

```text
artifacts/release/0.1.0/
├── Daycord-0.1.0-osx-arm64.dmg
└── Daycord-0.1.0-osx-arm64.zip
```

The ZIP contains the complete `Daycord.app` bundle.

### 7.5 macOS Manual Verification

On an Apple Silicon Mac:

1. Verify the DMG with `hdiutil verify`.
2. Mount the DMG.
3. Confirm that `Daycord.app` and the Applications link are present.
4. Drag the application into Applications.
5. Open it from Finder.
6. Verify the application icon.
7. Verify the displayed application version.
8. Close the application.
9. Launch it from Spotlight.
10. Confirm that the existing database remains available.
11. Exercise representative task, report, and settings functions.

Operating-system security warnings must be recorded accurately because the current release is unsigned and not notarized.

## 8. Windows x64 Release

### 8.1 Publish

The current Windows release is prepared from macOS using:

```bash
dotnet publish \
  src/Daycord.Desktop/Daycord.Desktop.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true
```

MSBuild invokes the existing Windows installer workflow.

This does not mean that Windows installation and runtime behavior can be fully verified on macOS.

The final installer must be tested on a Windows x64 environment.

### 8.2 Installer Script

Shell wrapper:

```text
scripts/create-windows-installer.sh
```

NSIS script:

```text
scripts/windows/daycord-installer.nsi
```

The wrapper checks:

- macOS packaging environment.
- Valid release version.
- Windows publish directory.
- Required .NET application files.
- Windows PE executable format.
- Availability of `makensis`.
- Absence of unexpected SQLite databases and other private files.
- Absence of symbolic links in the publish folder.

It generates an explicit uninstall manifest for files included in the published application.

### 8.3 Installation Model

The Windows installer uses machine-wide installation.

| Property | Behavior |
|---|---|
| Install location | `$PROGRAMFILES64\Daycord` |
| Privileges | Administrator / UAC |
| Start Menu | Daycord shortcuts |
| Installed Apps | Registration under HKLM |
| Uninstaller | Included |
| Data storage | Per-user local application data |

The installer does not automatically launch the application from the elevated setup process.

### 8.4 Reinstallation

The installer checks for a previous Daycord installation.

If an existing uninstaller is present, setup invokes it before installing the new application files.

If an existing application executable is found without an uninstaller, setup stops rather than replacing an unknown installation.

The installer is designed to preserve per-user application data.

### 8.5 Uninstallation

The uninstaller removes:

- Installed application files.
- Daycord Start Menu shortcuts.
- The Windows Installed Apps registry entry.
- Empty application installation directories.

It does not intentionally remove:

```text
%LOCALAPPDATA%\Daycord
```

Therefore, the user's SQLite database and internal application backups are intended to survive uninstallation.

This preservation must remain part of future installer testing.

### 8.6 Windows Artifacts

Expected outputs:

```text
artifacts/release/0.1.0/
├── Daycord-Setup-0.1.0-win-x64.exe
└── Daycord-0.1.0-win-x64.zip
```

The ZIP is a portable distribution containing the published Windows application files.

### 8.7 Windows Manual Verification

On Windows x64:

1. Run Setup.exe.
2. Confirm the expected UAC request.
3. Install into Program Files.
4. Verify the Start Menu shortcut.
5. Confirm registration in Installed Apps.
6. Launch Daycord as a normal user.
7. Create a task.
8. Record planned and actual time.
9. Export an Excel report.
10. Change application settings.
11. Close and reopen Daycord.
12. Confirm persistence.
13. Reinstall the same version.
14. Confirm that the database remains intact.
15. Uninstall the application.
16. Confirm that user data remains in LocalAppData.

Cross-version upgrade validation must be recorded separately.

## 9. Portable ZIP Archives

Script:

```text
scripts/create-release-archives.sh
```

The script supports:

```text
osx-arm64
win-x64
```

It runs only on macOS.

### 9.1 Archive Creation

The script uses `ditto` to create ZIP archives.

For macOS, the archive contains `Daycord.app`.

For Windows, the archive contains the published application files.

### 9.2 Archive Validation

The script verifies:

- ZIP structural integrity.
- Presence of the expected executable.
- Absence of `__MACOSX` directories.
- Absence of AppleDouble metadata.
- Absence of unwanted `.DS_Store` files.
- Absence of unexpected user data and release artifacts.

Windows archives are additionally checked for unexpected symbolic links.

The script uses a temporary output location and publishes the archive only after validation succeeds.

### 9.3 Protected Content

The release process rejects unexpected files such as:

- SQLite databases.
- SQLite WAL and SHM files.
- Backup files.
- Excel report files.
- Private keys and signing certificates.
- Unexpected log or archive files.

Do not weaken these checks to force a release through.

## 10. SHA-256 Checksums

The archive script produces:

```text
SHA256SUMS.txt
```

The checksum manifest is stored in the versioned release directory.

It includes artifacts that exist at the time the script is executed.

After generating both platform distributions, verify that all four expected artifacts are represented.

From the repository root on macOS:

```bash
cd artifacts/release/0.1.0
shasum -a 256 -c SHA256SUMS.txt
```

All recorded files must pass verification.

A successful checksum proves that the local artifact matches the recorded manifest. It does not establish publisher authenticity unless the manifest itself comes from a trusted authenticated source.

## 11. Expected Release Directory

For a complete version 0.1.0 distribution:

```text
artifacts/
└── release/
    └── 0.1.0/
        ├── Daycord-0.1.0-osx-arm64.dmg
        ├── Daycord-0.1.0-osx-arm64.zip
        ├── Daycord-Setup-0.1.0-win-x64.exe
        ├── Daycord-0.1.0-win-x64.zip
        └── SHA256SUMS.txt
```

The directory is generated by the release automation.

`artifacts/` is excluded from Git tracking by the project configuration.

## 12. Rider Publish Configurations

JetBrains Rider can invoke the same MSBuild publication process as the terminal.

For each supported platform, configure a .NET Publish profile with:

- Project: `Daycord.Desktop`.
- Configuration: `Release`.
- Target framework: `net10.0`.
- Runtime Identifier: `osx-arm64` or `win-x64`.
- Self-contained deployment: enabled.

The existing MSBuild targets are responsible for packaging after publication.

Use Rider's build output to verify that the packaging target completed successfully.

Do not manually duplicate packaging steps when the configured Publish target has already executed them.

## 13. Digital Signing and Notarization

### 13.1 Current Status

Daycord 0.1.0 artifacts are currently unsigned.

| Capability | Status |
|---|---|
| Apple Developer ID signing | Not configured |
| Apple notarization | Not configured |
| Windows Authenticode signing | Not configured |
| Trusted publisher identity | Not established for current artifacts |

Unsigned applications can trigger macOS or Windows security warnings.

Do not claim that Gatekeeper or SmartScreen will accept the current artifacts without warnings.

### 13.2 Future macOS Distribution

Developer ID distribution outside the Mac App Store requires an appropriate signing and notarization workflow.

The existing release automation does not implement this workflow.

### 13.3 Future Mac App Store Distribution

The current DMG workflow is not a Mac App Store submission workflow.

A future Mac App Store release requires a separate distribution configuration, including:

- Apple Developer Program enrollment.
- Appropriate App Store distribution signing.
- App Sandbox entitlements.
- Review of filesystem access.
- Review of backup import and export.
- Review of report export destinations.
- Compatible app packaging and submission.
- App Store Connect configuration.
- Compliance with the applicable App Review Guidelines.

In particular, Daycord's existing SQLite data location and file-access workflows need to be evaluated under App Sandbox restrictions.

The current release scripts must not be represented as providing these capabilities.

### 13.4 Secrets and Credentials

Never commit:

- Signing certificates.
- Private keys.
- Certificate passwords.
- Authentication tokens.
- App Store API credentials.
- Notarization credentials.

Future signing automation must use appropriately protected credential storage.

## 14. Release Verification Checklist

Before declaring a release technically ready:

- [ ] Confirm Git branch and commit.
- [ ] Review working tree changes.
- [ ] Confirm version in `Directory.Build.props`.
- [ ] Restore dependencies.
- [ ] Build Debug.
- [ ] Build Release.
- [ ] Review warnings.
- [ ] Publish macOS ARM64.
- [ ] Publish Windows x64.
- [ ] Verify macOS application bundle.
- [ ] Verify DMG.
- [ ] Verify NSIS installer.
- [ ] Verify both portable ZIP archives.
- [ ] Verify SHA-256 checksums.
- [ ] Confirm no private user files are included.
- [ ] Test application startup on macOS.
- [ ] Test application startup on Windows.
- [ ] Verify database persistence across restarts.
- [ ] Verify representative Excel exports.
- [ ] Review backup and restore verification status.
- [ ] Confirm application settings survive restart.
- [ ] Confirm user data survives Windows uninstall.
- [ ] Document unsigned-distribution limitations.
- [ ] Review known defects and release risks.
- [ ] Obtain the project owner's release approval.

The checklist is not evidence of completion by itself. Record actual results in [Testing](testing.md).

## 15. Upgrade and Rollback Considerations

### 15.1 Application Upgrade

Daycord uses persistent user data under the operating system's local application data directory.

Application binaries and user data are stored separately.

This separation supports preservation of user data during reinstallations.

However, successful same-version reinstallation does not establish cross-version upgrade compatibility.

### 15.2 Database Schema Changes

Database migrations are versioned and checksum-protected.

A newer release may modify the database schema through new migrations.

An older application version may not understand the upgraded schema.

Do not promise that installing an older binary will automatically downgrade the database.

### 15.3 Recommended Rollback Preparation

Before introducing a release with schema changes:

1. Back up the existing database.
2. Record the installed application version.
3. Record the current database migration version.
4. Install the new release in a controlled environment.
5. Test the relevant workflows.
6. Validate database integrity.

If a rollback is necessary, a compatible database backup may also be required.

Do not manually change migration history to force compatibility.

### 15.4 Failed Installation

If Windows installation or reinstallation fails, investigate the installer logs and installed file state.

Do not remove the user's LocalAppData directory.

If macOS installation fails, preserve the application database and investigate the bundle or distribution file separately.

## 16. Release Evidence

A release report should record:

| Item | Evidence |
|---|---|
| Version | Central version property |
| Revision | Git commit hash |
| Builds | Debug and Release output |
| Artifacts | Actual generated filenames |
| Archive integrity | ZIP and DMG verification |
| Checksums | SHA256SUMS.txt results |
| macOS runtime | Manual test result |
| Windows runtime | Manual test result |
| Database | Integrity and migration results |
| Restore | Full restore test result |
| Known defects | Issue identifiers and severity |
| Approval | Project owner decision |

Do not mark a test as PASS without corresponding evidence.

## 17. Known Release Limitations

For version 0.1.0:

- The supported distribution architectures are macOS ARM64 and Windows x64.
- The current packaging workflow runs on macOS.
- macOS artifacts are not signed or notarized.
- Windows artifacts are not signed.
- Cross-version upgrades have not been fully verified.
- Some backup and recovery failure scenarios require additional verification.
- The Mac App Store submission workflow has not been implemented.
- Known defects may be accepted for the MVP and tracked for later releases.

The project owner determines whether an artifact is approved for distribution.

## 18. Related Documentation

- [Architecture](architecture.md)
- [Database](database.md)
- [Development](development.md)
- [Backup and Recovery](backup-and-recovery.md)
- [Testing](testing.md)
- [User Guide](user-guide.md)

This guide documents the release infrastructure of Daycord 0.1.0.