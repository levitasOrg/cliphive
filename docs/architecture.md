# ClipHive Architecture

## Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│  ClipHive.exe (WPF, net8.0-windows, single-file self-contained) │
│                                                                  │
│  ┌─────────────┐    WM_CLIPBOARDUPDATE    ┌────────────────────┐│
│  │  Windows    │ ──────────────────────►  │ClipboardMonitor    ││
│  │  Clipboard  │                          │Service             ││
│  └─────────────┘                          │(HwndSource hidden  ││
│                                           │ message window)    ││
│                                           └────────┬───────────┘│
│                                                    │ ClipboardChanged event
│                                                    ▼            │
│  ┌────────────────┐   Encrypt()    ┌──────────────────────────┐ │
│  │EncryptionHelper│◄──────────────►│   StorageService         │ │
│  │(AES-256-GCM +  │               │   (SQLite via            │ │
│  │ DPAPI key)     │               │   Microsoft.Data.Sqlite) │ │
│  └────────────────┘               └──────────────┬───────────┘ │
│                                                  │ GetAllAsync  │
│                                                  ▼             │
│  ┌─────────────┐  HotkeyPressed   ┌─────────────────────────┐  │
│  │HotkeyService│ ───────────────► │  SidebarViewModel       │  │
│  │(RegisterHot │                  │  (ObservableCollection, │  │
│  │ Key P/Invoke│                  │   search filter, INotify│  │
│  └─────────────┘                  │   PropertyChanged)      │  │
│                                   └────────────┬────────────┘  │
│  ┌─────────────┐  PasteAsync()               │ DataContext     │
│  │PasteService │◄──────────────────────────── │                │
│  │(SendInput   │                              ▼                │
│  │ P/Invoke)   │               ┌──────────────────────────┐    │
│  └─────────────┘               │  SidebarWindow           │    │
│                                │  (WPF, WindowStyle=None, │    │
│  ┌──────────────┐              │   AllowsTransparency,    │    │
│  │AutoClear     │              │   slide-in animation)    │    │
│  │Service       │              └──────────────────────────┘    │
│  │(System.Timer,│                                              │
│  │ hourly)      │   ┌──────────────────────────────────────┐   │
│  └──────────────┘   │  App.xaml.cs                         │   │
│                     │  - Single-instance mutex              │   │
│  ┌──────────────┐   │  - Service wiring                    │   │
│  │SettingsService│  │  - NotifyIcon (System.Windows.Forms) │   │
│  │(JSON,         │  │  - ShutdownMode.OnExplicitShutdown   │   │
│  │ %LOCALAPPDATA│  └──────────────────────────────────────┘   │
│  │\ClipHive\    │                                              │
│  │settings.json)│                                              │
│  └──────────────┘                                              │
└─────────────────────────────────────────────────────────────────┘

External storage (on disk, encrypted):
  %LOCALAPPDATA%\ClipHive\history.db    ← SQLite (AES-256-GCM ciphertext)
  %LOCALAPPDATA%\ClipHive\settings.json ← Plain JSON (no secrets)
```

## Data Flow: Clipboard Change → Storage → Sidebar

```
1. User copies text
       │
       ▼
2. Windows sends WM_CLIPBOARDUPDATE to hidden HwndSource
       │
       ▼
3. ClipboardMonitorService.WndProc fires ClipboardChanged event
   (checks PasteService.IsPasting to prevent re-entry)
       │
       ▼
4. App.xaml.cs handler calls StorageService.AddAsync(plaintext)
       │
       ▼
5. StorageService calls EncryptionHelper.Encrypt(plaintext)
   → AesGcm.Encrypt with random 12-byte IV
   → Returns (ciphertext_b64, iv_b64, tag_b64)
       │
       ▼
6. INSERT INTO clipboard_items (ciphertext, iv, tag, created_at, ...) 
   — plaintext never written to disk
       │
       ▼
7. SidebarViewModel.OnClipboardChanged(text) prepends item to Items
   (deduplication: removes existing entry with same text)
       │
       ▼
8. User presses Ctrl+Shift+V → HotkeyService fires HotkeyPressed
       │
       ▼
9. SidebarWindow.Show() — slide-in animation, LoadAsync() called
       │
       ▼
10. StorageService.GetAllAsync() → decrypts each row → returns ClipboardItem[]
       │
       ▼
11. User selects item → PasteService.PasteAsync(content)
    - Sets IsPasting = true (prevents step 3 re-entry)
    - Clipboard.SetText(content)
    - SendInput(Ctrl+V)
    - IsPasting = false
```

## Key Derivation (DPAPI)

```
Fixed entropy bytes (hard-coded seed)
    │
    ▼
ProtectedData.Protect(entropy, null, DataProtectionScope.LocalMachine)
    │  ← machine-scoped: output differs per machine, per Windows install
    ▼
SHA-256 hash of protected bytes
    │
    ▼
32-byte AES-256 key (cached in memory, never written to disk)
```

**Why machine-scope?** If the SQLite database is copied to another machine, decryption throws `CryptographicException`. This is the privacy isolation guarantee.

## Architecture Decision Records

See the main development plan for full ADRs. Summary:

| Decision | Choice | Rationale |
|----------|--------|-----------|
| UI framework | WPF (.NET 8) | Native rendering, MVVM, GPU compositing — Electron/WinForms lack overlay animation |
| Storage | SQLite (Microsoft.Data.Sqlite) | Structured queries, WAL mode, disaster recovery via file copy |
| Encryption | AES-256-GCM | Authenticated encryption, random IV per record, .NET 8 built-in |
| Win32 | Raw P/Invoke | No third-party hook libraries; full control; minimal dependency surface |
| Installer | Inno Setup 6 | Smallest size, no UAC required, easy to audit |
