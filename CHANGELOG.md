# Changelog

All notable changes to ClipHive will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.0] - 2026-04-14

### Fixed
- **Data loss on restart (critical)** — Encryption key was re-derived non-deterministically
  on every launch using `ProtectedData.Protect()`, which returns a different blob each call.
  Key is now generated once, DPAPI-protected (`CurrentUser` scope), and persisted to
  `%LOCALAPPDATA%\ClipHive\key.dat`. All history now survives shutdown and restart.
- **App crash on item click** — `PasteAsync` / `PasteImageAsync` called `Clipboard.SetText`
  on a non-STA threadpool thread (after `ConfigureAwait(false)`), causing an unhandled
  `InvalidOperationException`. Clipboard operations are now marshalled via
  `Dispatcher.InvokeAsync` to the UI thread before paste simulation runs.
- **Double-close crash** — `Window_Deactivated` fired while the window was already
  in the process of closing, triggering a second `Close()` call. Fixed with a `_closing`
  guard flag.
- **Click-outside does not close sidebar** — WPF's `Deactivated` event is unreliable for
  `WindowStyle=None` / `AllowsTransparency=True` windows when clicking the desktop,
  taskbar, or other apps. Replaced with a Win32 `WM_NCACTIVATE` / `WM_ACTIVATEAPP`
  hook via `HwndSource`. A `_isReady` guard prevents the window from closing during its
  own initial activation.

### Added
- **"crafted by gmv ♥" credit** — Subtle italic label at the bottom-right of the sidebar,
  visible each time the history panel is opened.
- **Hide from tray setting** — New toggle in Settings to hide the system tray icon.
  When hidden, the app is still accessible via the configured hotkey.
- **Custom branding icon** — ClipHive logo (purple/teal ribbon) replaces the generic
  Windows application icon in all four locations:
  - System tray `NotifyIcon`
  - EXE file icon (visible in Explorer, Alt+Tab, taskbar)
  - Inno Setup installer wizard
  - Desktop shortcut (if created during install)
  Icon generated as a multi-size ICO (16 / 32 / 48 / 64 / 128 / 256 px) with background
  stripped via flood-fill, embedded as an assembly resource for single-file publish.

### Changed
- **Sidebar UX — single click to paste** — Selecting a history item now requires one
  click instead of a double-click. Clicking copies the item to the clipboard, simulates
  Ctrl+V into the previously focused window, and dismisses the sidebar.
- **Footer buttons removed** — "Clear All", "Settings", and "✕" buttons removed from the
  sidebar. The panel is now search box + history list only (Maccy-style). Settings remain
  accessible via the tray icon right-click menu.
- **DPAPI scope changed** — Encryption key protection changed from `LocalMachine` to
  `CurrentUser` scope. `LocalMachine` required elevated permissions on some configurations;
  `CurrentUser` is reliable across all standard Windows accounts.
- **Release workflow** — Inno Setup version pin removed (`--version 6.4.3` caused failures
  when a newer version was already installed on the runner). Now installs latest available.
- **AV-friendly publish flags** — `EnableCompressionInSingleFile=false` prevents the
  self-extraction-to-TEMP pattern that triggers Windows Defender heuristics.
  `PublishReadyToRun=true` pre-compiles to native code for faster startup.
- **All repo URLs** updated to `levitasOrg/cliphive`.
- **Assembly metadata** added: `Version`, `FileVersion`, `Copyright`, `Description`,
  `Company` fields now appear in the EXE's Properties dialog.

### Documentation
- **README** rewritten with full build guide, publish flag explanations, and 10 common
  failure scenarios with exact fixes (dotnet not found, NuGet failures, file locked,
  AV false positives, Inno Setup not found, runtime crashes).

---

## [1.0.0] - 2026-04-13

### Added
- Clipboard history sidebar with keyboard-driven navigation (↑/↓, Enter, Escape, Delete)
- Real-time search filtering with case-insensitive matching
- AES-256-GCM encryption for all stored clipboard data
- DPAPI key derivation (data stays on this machine only)
- SQLite backend with WAL mode for reliable concurrent access
- Auto-clear policies: 2 hours, 3 days, 15 days, 1 month, or never
- Pin items to keep them at the top of history indefinitely
- System tray icon with context menu (Open, Settings, Exit)
- Configurable global hotkey (default: Ctrl+Shift+V)
- Optional start-with-Windows via registry
- Settings window for hotkey, auto-clear, and startup preferences
- Slide-in sidebar animation (120 ms ease-out from right)
- Single-file self-contained build (no .NET pre-installed required)
- Inno Setup installer with repair/uninstall detection
- Zero network calls — no telemetry, no cloud sync

[Unreleased]: https://github.com/levitasOrg/cliphive/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/levitasOrg/cliphive/compare/v1.0.0...v1.2.0
[1.0.0]: https://github.com/levitasOrg/cliphive/releases/tag/v1.0.0
