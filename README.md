# ClipHive

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue.svg)](https://www.microsoft.com/windows)

> A lightweight, privacy-first clipboard history manager for Windows — inspired by macOS's Maccy.

## Features

- **Instant recall** — Press `Ctrl + Shift + V` to open your clipboard history sidebar
- **Single-click paste** — Click any history item to copy it and close the sidebar instantly
- **Encrypted at rest** — All history stored with AES-256-GCM; key protected via Windows DPAPI (user-scoped)
- **Zero network calls** — No telemetry, no cloud sync, no outbound connections — ever
- **Keyboard-driven** — Arrow keys to navigate, Enter to paste, Delete to remove, Escape to dismiss
- **Live search** — Type to filter history in real time
- **Auto-clear** — Optional automatic history purge: 2 hours, 3 days, 15 days, 1 month, or never
- **System tray** — Lives quietly in your tray; double-click or press hotkey to open

## Usage

| Action | How |
|--------|-----|
| Open history | `Ctrl + Shift + V` or double-click tray icon |
| Paste an item | Single-click it (copies + closes) |
| Navigate items | `↑` / `↓` arrow keys |
| Paste selected | `Enter` |
| Delete item | `Delete` key |
| Close sidebar | `Escape` or click elsewhere |
| Settings | Right-click tray icon → Settings |

## Privacy Guarantee

ClipHive stores clipboard data **only on your machine**, encrypted with a key that never leaves your device:

- `netstat -an` while running: **zero outbound connections**
- Direct SQL inspection of `%LOCALAPPDATA%\ClipHive\history.db`: **all values are base64 ciphertext**
- Copy the database to another machine: **decryption fails** (DPAPI user-scope isolation)
- Encryption key is stored at `%LOCALAPPDATA%\ClipHive\key.dat` — protected by Windows DPAPI, readable only by your Windows user account

---

## Building the EXE from Source

### Prerequisites

| Requirement | Version | Download |
|-------------|---------|----------|
| .NET 8 SDK | 8.0.x | https://dotnet.microsoft.com/download/dotnet/8 |
| Windows | 10 1903+ / 11 | — |
| Inno Setup 6 *(installer only)* | 6.x | https://jrsoftware.org/isinfo.php |

Verify your setup:
```powershell
dotnet --version   # should print 8.x.x
```

---

### Step 1 — Clone and restore

```powershell
git clone https://github.com/gokulMv/ClipHive.git
cd ClipHive
dotnet restore
```

---

### Step 2 — Run tests (optional but recommended)

```powershell
dotnet test --configuration Release
```

Expected output: all tests pass, no failures.

---

### Step 3 — Publish the EXE

```powershell
dotnet publish src/ClipHive/ClipHive.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=false `
  -p:PublishReadyToRun=true `
  -p:IncludeNativeLibrariesForSelfExtract=false `
  -o dist/release
```

Output: `dist/release/ClipHive.exe` (~160 MB, self-contained, no .NET installation required)

> **Why `EnableCompressionInSingleFile=false`?**
> Compressed single-file EXEs unpack to `%TEMP%` at startup, which triggers Windows Defender's heuristic for self-extracting malware. Disabling compression makes the file larger but avoids AV false positives.

---

### Step 4 — Build the installer (optional)

```powershell
# Copy the published files to the expected location
$buildDir = "$env:TEMP\ClipHiveBuild"
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
Copy-Item -Path "dist\release\*" -Destination $buildDir -Recurse -Force

# Compile the installer
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ClipHive.iss
```

Output: `dist/ClipHive-1.0.0-Setup.exe`

---

## Common Build Failures and Fixes

### `dotnet: command not found` or `'dotnet' is not recognized`

**Cause:** .NET SDK not installed or not on PATH.

**Fix:**
```powershell
# Download and install .NET 8 SDK from:
# https://dotnet.microsoft.com/download/dotnet/8

# If installed but not on PATH, add it manually:
$env:PATH += ";C:\Program Files\dotnet"
dotnet --version
```

---

### `NETSDK1045: The current .NET SDK does not support targeting .NET 8`

**Cause:** You have an older .NET SDK (e.g., .NET 6 or 7) installed.

**Fix:**
```powershell
dotnet --list-sdks   # check what's installed
# Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8
# Then verify:
dotnet --version   # must show 8.x.x
```

---

### `error MSB4019: The imported project "Microsoft.CSharp.targets" was not found`

**Cause:** .NET SDK is corrupted or the workload for Windows desktop is missing.

**Fix:**
```powershell
dotnet workload install microsoft-net-sdk-windowsdesktop
```

---

### `error NU1301: Unable to load the service index` (NuGet restore failure)

**Cause:** No internet connection or NuGet source is unreachable.

**Fix:**
```powershell
# Check connectivity
Invoke-WebRequest -Uri "https://api.nuget.org/v3/index.json" -UseBasicParsing

# Clear NuGet cache and retry
dotnet nuget locals all --clear
dotnet restore
```

---

### `LINK : fatal error LNK1104: cannot open file 'ClipHive.exe'` (file locked)

**Cause:** A previous instance of ClipHive.exe is still running and locking the output file.

**Fix:**
```powershell
# Kill any running ClipHive instances
Stop-Process -Name "ClipHive" -Force -ErrorAction SilentlyContinue
# Then re-run the publish command
```

---

### `error CS0246: The type or namespace name 'X' could not be found`

**Cause:** NuGet packages weren't restored properly.

**Fix:**
```powershell
dotnet restore --force
dotnet build
```

---

### `System.Security.Cryptography.CryptographicException` at runtime (data loss after restart)

**Cause (old versions):** The encryption key was re-derived non-deterministically on each start, making old DB data unreadable.

**Current behavior:** The key is generated once, DPAPI-protected, and saved to `%LOCALAPPDATA%\ClipHive\key.dat`. It's stable across restarts.

**If you still get this after upgrading:** Your `history.db` was written with an old key and cannot be recovered. Delete it to start fresh:
```powershell
Remove-Item "$env:LOCALAPPDATA\ClipHive\history.db"
# key.dat is fine — leave it
```

---

### `The application failed to start because its side-by-side configuration is incorrect`

**Cause:** Missing Visual C++ Redistributable (rare with self-contained publish but possible).

**Fix:**
```powershell
# Download and install VC++ Redistributable x64:
# https://aka.ms/vs/17/release/vc_redist.x64.exe
```

---

### Windows Defender flags the EXE as suspicious

**Cause:** Unsigned EXEs that simulate keyboard input (`SendInput`) and hook the clipboard are common patterns in keyloggers, so AV software can heuristically flag them.

**Mitigations already in place:**
- `EnableCompressionInSingleFile=false` — avoids self-extraction to TEMP (a keylogger behaviour)
- `PublishReadyToRun=true` — pre-compiled native code looks less like packed malware
- The EXE uses a proper application manifest and icon

**If Defender still flags it:**
1. Submit the file to https://www.microsoft.com/en-us/wdsi/filesubmission for analysis (false positive reporting)
2. Add an exclusion: *Windows Security → Virus & threat protection → Manage settings → Add or remove exclusions*
3. Digitally sign the EXE with a code-signing certificate (removes virtually all AV false positives)

---

### Installer build fails: `ISCC is not recognized`

**Cause:** Inno Setup 6 is not installed or not on PATH.

**Fix:**
```powershell
# Install from https://jrsoftware.org/isdl.php
# Then either add to PATH or use the full path:
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ClipHive.iss
```

---

### `dotnet publish` succeeds but EXE crashes immediately on launch

**Checklist:**
1. Run from a terminal to see the error:
   ```powershell
   cd dist\release
   .\ClipHive.exe
   ```
2. Check the Windows Event Viewer: *Applications and Services Logs → Application* for crash details
3. Ensure you're on Windows 10 1903 or later (`winver`)
4. Confirm `win-x64` matches your machine architecture (not ARM)

---

## Project Structure

```
ClipHive/
├── src/ClipHive/          # Main WPF application
│   ├── Models/            # ClipboardItem, AppSettings
│   ├── Services/          # Clipboard monitor, storage, hotkey, paste, encryption
│   ├── ViewModels/        # SidebarViewModel, SettingsViewModel
│   ├── Views/             # SidebarWindow, SettingsWindow (XAML)
│   └── Helpers/           # Win32 P/Invoke declarations
├── tests/ClipHive.Tests/  # xUnit test suite
├── installer/ClipHive.iss # Inno Setup 6 installer script
├── assets/icon/           # ClipHive.ico (used for tray + installer)
└── dist/                  # Build output (gitignored)
```

## License

[MIT](LICENSE) — Copyright © 2026 ClipHive Contributors
