# ClipHive

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue.svg)](https://www.microsoft.com/windows)
[![Release](https://img.shields.io/github/v/release/levitasOrg/cliphive)](https://github.com/levitasOrg/cliphive/releases/latest)

> A privacy-first, encrypted clipboard history manager for Windows — built natively in WPF/C# with zero telemetry and zero runtime dependencies (standalone build).

---

## Features

### Core
- **Instant recall** — Press `Ctrl + Shift + V` (configurable) to open your clipboard history
- **Single-click paste** — Click any item to copy it back and close the sidebar immediately
- **Live search** — Type to filter history in real time; searches both text content and OCR-extracted image text
- **Keyboard navigation** — `↑`/`↓` to navigate, `Enter` to paste, `Delete` to remove, `Escape` to dismiss
- **Per-item delete** — Click the `×` button on any item to remove it instantly
- **Pin items** — Keep important clips at the top of the list permanently
- **Auto-clear** — Optional automatic history purge: 2 hours, 3 days, 15 days, 1 month, or never
- **Duplicate prevention** — Copying the same content twice bumps it to the top instead of creating a duplicate (SHA-256 hash check, no decryption overhead)

### Smart Contextual Actions
Each clipboard item detects its content type and surfaces instant actions:
- **URLs** — "Open ↗" button launches the link in your browser
- **File paths** — "Show in Explorer" reveals the file/folder in Windows Explorer
- **Hex colour codes** — Live colour swatch renders the exact colour inline (`#RRGGBB`)

### Image Support
- **Image thumbnails** — Screenshots and copied images appear as visual previews in the list
- **Offline OCR** — Windows' built-in OCR engine (`Windows.Media.Ocr`) silently extracts text from images in the background; images are fully searchable by their text content without any cloud service or network call

### Code Viewer
- Clipboard items detected as code show a "⌄ view" button
- Clicking it opens an inline monospace panel (scrollable, read-only)
- C#, Python, JavaScript, and SQL are auto-detected

### Paste as Plain Text
- `Ctrl + Alt + V` (configurable) strips all RTF/HTML rich formatting and pastes raw text
- Useful when copying from Word, browsers, or design tools and pasting into code editors or plain text fields

### Security & Privacy
- **AES-256-GCM encryption at rest** — all history rows are encrypted; the database contains only base64 ciphertext
- **DPAPI-protected key** — encryption key generated once, protected by Windows DPAPI (user scope), stored at `%LOCALAPPDATA%\ClipHive\key.dat`; key never leaves your machine or your Windows account
- **Zero network calls** — `netstat -an` while running shows no outbound connections, ever
- **Machine-bound data** — copying the database to another machine fails to decrypt (DPAPI user-scope isolation)

### Windows Integration
- **System tray** — lives quietly in your notification area; double-click or press the hotkey to open
- **Start with Windows** — optional registry entry for automatic startup (configurable)
- **Windows 11 Acrylic backdrop** — native Desktop Acrylic glass effect on Windows 11 Build 22000+ via `DwmSetWindowAttribute`; falls back to the dark translucent theme on Windows 10 gracefully
- **High-DPI** — all icons use native Segoe MDL2 Assets glyphs; crisp at any DPI/scale

---

## Usage

| Action | How |
|--------|-----|
| Open history | `Ctrl + Shift + V` or double-click tray icon |
| Paste an item | Single-click it (copies + closes) |
| Navigate items | `↑` / `↓` arrow keys |
| Paste selected | `Enter` |
| Delete item | `Delete` key or click the `×` button on the item |
| Pin / unpin item | Right-click tray → or use `PinItemCommand` |
| Paste as plain text | `Ctrl + Alt + V` |
| Close sidebar | `Escape` or click elsewhere |
| Settings | Right-click tray icon → Settings |

---

## Why ClipHive Is Not a Maccy Clone

Maccy is a great macOS app — but ClipHive is a **separate, Windows-native product** built from scratch in C#/WPF. The comparison is worth reading because ClipHive goes further in almost every dimension:

| Capability | ClipHive | Maccy |
|-----------|----------|-------|
| **Platform** | Windows 10 / 11 (native WPF) | macOS only |
| **Encryption at rest** | AES-256-GCM, DPAPI-protected key | None — plain SQLite |
| **Image OCR search** | Yes — offline, via Windows.Media.Ocr | No |
| **Contextual actions** | URL open, file reveal, hex colour swatch | No |
| **Paste as plain text** | Yes (`Ctrl+Alt+V`) | Yes |
| **Duplicate deduplication** | SHA-256 hash check (no decryption cost) | Yes |
| **Pin items** | Yes | Yes |
| **Windows 11 Acrylic glass** | Native `DwmSetWindowAttribute` | n/a |
| **Machine-bound encryption** | Yes (DPAPI user scope) | n/a |
| **Zero network calls** | Verifiable via netstat | Yes |
| **Code viewer** | Inline monospace panel, auto language detect | No |
| **Runtime requirement** | Standalone: none bundled; Installer: .NET 8 | macOS built-in |
| **Open source** | MIT | MIT |

**The key differences:**

1. **Privacy**: Maccy stores clipboard history as plaintext in SQLite. ClipHive stores everything as AES-256-GCM ciphertext. If someone copies your database, they get ciphertext — not your passwords, tokens, or sensitive text.

2. **Image intelligence**: ClipHive runs Windows' built-in OCR on every image you copy. Screenshot of an error message? Search for the error text and find the screenshot. No API key, no cloud service.

3. **Platform-native**: ClipHive is not a port, wrapper, or Electron app. It is a WPF application that uses Windows-specific APIs throughout — `DwmSetWindowAttribute` for Acrylic, `Windows.Media.Ocr` for image text, `ProtectedData` (DPAPI) for key storage, Win32 `SendInput` for paste simulation, `WM_NCACTIVATE` for focus tracking. Every feature is implemented at the platform level, not over a cross-platform abstraction.

4. **Size efficiency**: The installer build is ~20 MB (framework-dependent). The standalone build is ~90 MB with the full .NET 8 runtime bundled and compressed. Maccy is ~4 MB because it relies entirely on macOS system frameworks — a fair comparison would be the installer build which similarly relies on the Windows-provided .NET 8 runtime.

---

## Privacy Guarantee

ClipHive stores clipboard data **only on your machine**, encrypted with a key that never leaves your device:

- `netstat -an` while running: **zero outbound connections**
- Direct SQL inspection of `%LOCALAPPDATA%\ClipHive\history.db`: **all values are base64 ciphertext**
- Copy the database to another machine: **decryption fails** (DPAPI user-scope isolation)
- Encryption key stored at `%LOCALAPPDATA%\ClipHive\key.dat` — readable only by your Windows account

---

## Installation

### Option A — Installer (~20 MB)
Download `ClipHive-1.3.2-Setup.exe` from the [latest release](https://github.com/levitasOrg/cliphive/releases/latest).

Requires the [.NET 8 Windows Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). The installer detects this automatically and offers to open the download page if it's missing.

### Option B — Standalone EXE (~90 MB)
Download `ClipHive-1.3.2-Standalone.exe`. No installer, no .NET required — the runtime is bundled and compressed inside the EXE. Just run it.

---

## Building from Source

### Prerequisites

| Requirement | Version | Download |
|-------------|---------|----------|
| .NET 8 SDK | 8.0.x | https://dotnet.microsoft.com/download/dotnet/8 |
| Windows | 10 1809+ / 11 | — |
| Inno Setup 6 *(installer only)* | 6.x | https://jrsoftware.org/isinfo.php |

```powershell
dotnet --version   # must print 8.x.x
```

### Step 1 — Clone and restore

```powershell
git clone https://github.com/levitasOrg/cliphive.git
cd ClipHive
dotnet restore
```

### Step 2 — Run tests

```powershell
dotnet test --configuration Release
```

### Step 3A — Publish standalone EXE

```powershell
dotnet publish src/ClipHive/ClipHive.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:PublishReadyToRun=false `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist/standalone
```

Output: `dist/standalone/ClipHive.exe` (~90 MB, fully self-contained, no .NET required)

### Step 3B — Publish installer payload

```powershell
dotnet publish src/ClipHive/ClipHive.csproj `
  -c Release -r win-x64 --self-contained false `
  -o dist/release
```

Then build the installer:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ClipHive.iss
```

Output: `dist/ClipHive-1.3.2-Setup.exe` (~20 MB, requires .NET 8 Desktop Runtime)

---

## Common Build Failures

### `dotnet: command not found` or `'dotnet' is not recognized`

**Fix:**
```powershell
# Add .NET to PATH if installed but not found:
$env:PATH += ";C:\Program Files\dotnet"
dotnet --version
```

### `NETSDK1045: The current .NET SDK does not support targeting .NET 8`

**Fix:** Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8

### `error NU1301: Unable to load the service index` (NuGet failure)

**Fix:**
```powershell
dotnet nuget locals all --clear
dotnet restore
```

### `LINK : fatal error LNK1104: cannot open file 'ClipHive.exe'` (file locked)

**Fix:**
```powershell
Stop-Process -Name "ClipHive" -Force -ErrorAction SilentlyContinue
```

### `System.Security.Cryptography.CryptographicException` at runtime

**Cause:** Database was written with an old key (from versions before 1.2.0).

**Fix:** Delete the old database (key is fine):
```powershell
Remove-Item "$env:LOCALAPPDATA\ClipHive\history.db"
```

### Windows Defender flags the EXE

**Cause:** Unsigned EXEs that simulate keyboard input and hook the clipboard match AV heuristics.

**Mitigations already in place:** `EnableCompressionInSingleFile=true` (standalone) avoids the TEMP-extraction pattern; `IncludeNativeLibrariesForSelfExtract=true` bundles natives without a second extraction step.

**If Defender still flags it:**
1. Submit to https://www.microsoft.com/en-us/wdsi/filesubmission (false positive report)
2. Add a Windows Security exclusion for the EXE path
3. Sign the EXE with a code-signing certificate (removes virtually all AV false positives)

### Installer says .NET 8 is missing even though it's installed

The installer checks multiple locations (standard system path, `C:\Program Files\dotnet`, per-user install path, and registry). If all checks fail despite .NET being present, click **"Yes — continue anyway"** in the prompt. The app will launch normally.

---

## Project Structure

```
ClipHive/
├── src/ClipHive/
│   ├── Models/            # ClipboardItem, AppSettings
│   ├── Services/          # ClipboardMonitor, StorageService, HotkeyService,
│   │                      #   PasteService, OcrService, EncryptionService
│   ├── ViewModels/        # SidebarViewModel, SettingsViewModel
│   ├── Views/             # SidebarWindow.xaml, SettingsWindow.xaml
│   ├── Resources/         # Styles.xaml (dark theme)
│   └── Helpers/           # Win32.cs (P/Invoke: DWM, SendInput, HWND hooks)
├── tests/ClipHive.Tests/  # xUnit test suite
├── installer/ClipHive.iss # Inno Setup 6 script
├── assets/icon/           # ClipHive.ico (16–256 px multi-size)
└── dist/                  # Build output (gitignored)
```

---

## License

[MIT](LICENSE) — Copyright © 2026 ClipHive Contributors
