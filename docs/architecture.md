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
   Skipped entirely when:
   - the update carries ClipHive's own-copy marker format (self-paste),
   - the source app marked it with a standard exclusion format
     (ExcludeClipboardContentFromMonitorProcessing, CanIncludeInClipboardHistory=0,
      Clipboard Viewer Ignore — password managers do this),
   - the source app or content matches the user's IgnoredApps / IgnorePatterns.
       │
       ▼
4. App.xaml.cs handler calls StorageService.AddAsync(plaintext, sourceApp)
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
    - Writes a DataObject carrying the content + the private
      "ClipHive.OwnCopy" marker format → step 3 skips it deterministically
    - SendInput(Ctrl+V)
```

## Encryption Key (DPAPI, since v1.2.0)

```
First run:  RandomNumberGenerator.GetBytes(32)
    │
    ▼
ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser)
    │  ← user-scoped: only this Windows account can unprotect it
    ▼
written to %LOCALAPPDATA%\ClipHive\key.dat

Later runs: File.ReadAllBytes(key.dat) → ProtectedData.Unprotect → same 32-byte key
```

A second key for dedupe fingerprints is derived in memory as
`HMAC-SHA256(key, "ClipHive.ContentHash.v2")` — the stored `content_hash` column is a
keyed HMAC of the plaintext, so it deduplicates without decryption yet reveals nothing
that would let an attacker confirm content guesses offline.

**Why user scope?** If the database + key.dat are copied to another machine (or
another user's profile), `Unprotect` throws — the data is bound to this Windows
account. (Caveat: with roaming profiles / credential roaming, DPAPI CurrentUser
keys legitimately follow the user; the guarantee is user-bound, not machine-bound.)
If key.dat is corrupted, startup offers a key reset (fresh key, history discarded —
it is undecryptable without the old key) instead of failing to launch.

## Architecture Decision Records

See the main development plan for full ADRs. Summary:

| Decision | Choice | Rationale |
|----------|--------|-----------|
| UI framework | WPF (.NET 8) | Native rendering, MVVM, GPU compositing — Electron/WinForms lack overlay animation |
| Storage | SQLite (Microsoft.Data.Sqlite) | Structured queries, WAL mode, disaster recovery via file copy |
| Encryption | AES-256-GCM | Authenticated encryption, random IV per record, .NET 8 built-in |
| Win32 | Raw P/Invoke | No third-party hook libraries; full control; minimal dependency surface |
| Installer | Inno Setup 6 | Small, easy to audit; per-user install (PrivilegesRequired=lowest), so no UAC by default |
