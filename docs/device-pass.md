# ClipHive v1.4.0 device pass — agent runbook

**Audience: a Claude Code session running on a Windows 10/11 machine**, driving this
checklist with the user sitting at the desktop. Work top to bottom. Automate every step
marked `[agent]` yourself (PowerShell commands are given, adapt as needed). Steps marked
`[human]` need the user to perform an action or confirm what they see on screen — ask
them, then record their answer. Record every result in the table at the bottom and
report the filled table when done.

**What is being verified:** the desktop-interop layer of the 2026-07-31 audit-fix merge
(PR #1). The logic layer (crypto, storage, migration, filters) is already covered by
124 unit tests in CI — do not re-test it here. This pass covers only what a headless
runner cannot execute: real clipboard interop, paste, hotkeys, recovery UI, installer UX.

## 0 · Setup `[agent]`

```powershell
git pull
dotnet publish src/ClipHive/ClipHive.csproj -c Release -r win-x64 --self-contained false -o dist/devicepass
# IMPORTANT if an older ClipHive is installed: back up the user's real data first.
Copy-Item "$env:LOCALAPPDATA\ClipHive" "$env:LOCALAPPDATA\ClipHive.bak" -Recurse -ErrorAction SilentlyContinue
Stop-Process -Name ClipHive -Force -ErrorAction SilentlyContinue
Start-Process dist/devicepass/ClipHive.exe
```

Confirm the tray icon appears (`[human]` if you can't detect it). For DB row counts,
install sqlite: `winget install SQLite.SQLite` (counts/metadata are readable; content
is ciphertext — that's the point).

```powershell
function Get-ClipRows { sqlite3 "$env:LOCALAPPDATA\ClipHive\history.db" "SELECT id, content_type, source_app, created_at FROM clipboard_items ORDER BY id;" }
```

## 1 · Own-copy marker — paste must not reorder history

- `[agent]` Seed history: `Set-Clipboard "alpha"; sleep 1; Set-Clipboard "beta"; sleep 1; Set-Clipboard "gamma"; sleep 1` then snapshot `Get-ClipRows`.
- `[human]` Open ClipHive (Ctrl+Shift+V), click **alpha** (the oldest) to paste it into Notepad. Repeat twice more.
- `[agent]` `Get-ClipRows` again. **Pass:** row set and `created_at` of alpha's row unchanged — pasting did not re-capture or bump it. (This was defect D1: the old timing flag let pastes reorder history.)

## 2 · Exclusion formats — password-manager copies never recorded

- `[agent]` Write clipboard data carrying the standard exclusion format (this is exactly what KeePass/Bitwarden do):

```powershell
Add-Type -AssemblyName System.Windows.Forms
$d = New-Object System.Windows.Forms.DataObject
$d.SetData([System.Windows.Forms.DataFormats]::UnicodeText, "SECRET-SHOULD-NOT-APPEAR")
$d.SetData("ExcludeClipboardContentFromMonitorProcessing", (New-Object System.IO.MemoryStream(,[byte[]](1,0,0,0))))
[System.Windows.Forms.Clipboard]::SetDataObject($d, $true)
```

  (Run in an STA PowerShell: `powershell -STA -File ...`.) Repeat with `"Clipboard Viewer Ignore"` and with `"CanIncludeInClipboardHistory"` carrying DWORD 0.
- `[agent]` **Pass:** `Get-ClipRows` gains no row for any of the three; a plain `Set-Clipboard "control-entry"` afterwards IS captured (proves the monitor is alive).
- `[human]` (optional, if a password manager is installed) Copy a real entry from it → must not appear in ClipHive.

## 3 · Ignore lists

- `[agent]` Add to `%LOCALAPPDATA%\ClipHive\settings.json`: `"IgnorePatterns": ["^token-"]`, restart ClipHive, then `Set-Clipboard "token-abc123"; sleep 1; Set-Clipboard "not-a-token"`.
- **Pass:** only `not-a-token` is captured. Also check `source_app` is now populated on new rows (it will usually be the shell/host process name for Set-Clipboard — any non-NULL value passes; the column was previously always NULL).

## 4 · Unicode plain-text paste (was D3)

- `[agent]` Put rich text on the clipboard:

```powershell
$html = "Version:0.9`r`nStartHTML:0000000105`r`nEndHTML:0000000199`r`n<html><body><b>日本語 🎉 Привет</b></body></html>"
Set-Clipboard -Value "日本語 🎉 Привет" # then re-set as HTML via DataObject if needed; copying bold text from WordPad/browser also works
```

  Simplest reliable source `[human]`: copy the bold text **日本語 🎉 Привет** from a browser page or Word.
- `[human]` Focus Notepad, press **Ctrl+Alt+V**. **Pass:** pastes `日本語 🎉 Привет` intact — any `?` characters = FAIL.

## 5 · Sidebar stability (was D4)

- `[human]` Press Ctrl+Shift+V ~10 times rapidly. **Pass:** the sidebar toggles open/closed cleanly each time; it never "flashes" open and instantly closes on its own. Typing in the search box immediately after opening works.

## 6 · Hotkey conflict surfacing (was D2)

- `[agent]` Register Ctrl+Shift+V from another process before launching ClipHive (e.g. a tiny PowerShell using RegisterHotKey via Add-Type, or `[human]` start Ditto/AutoHotkey with that binding), then start ClipHive.
- **Pass:** a tray balloon warns the hotkey is unavailable; the tray icon is visible; the app is still usable from the tray menu.

## 7 · Key-corruption recovery (was D10)

- `[agent]` `Stop-Process -Name ClipHive -Force; Set-Content "$env:LOCALAPPDATA\ClipHive\key.dat" -Value "garbage"` then relaunch.
- `[human]` **Pass:** a dialog explains the key problem and offers Reset (Yes) / exit (No) — not a crash or silent exit. Choose **Yes**: app starts fresh and captures normally. (History loss is expected — it's undecryptable without the old key; the real data was backed up in step 0.)

## 8 · Migration on a real v1.3.x database

- `[agent]` If `%LOCALAPPDATA%\ClipHive.bak` (step 0) contains a pre-existing history.db: stop the app, restore the backup over the data dir, launch.
- **Pass:** `sqlite3 ... "PRAGMA user_version;"` returns 2; old items visible in the sidebar; copying the text of an old item does NOT create a duplicate row (keyed re-fingerprint worked).

## 9 · Installer `[human]` (agent prepares)

- `[agent]` Build it: `dotnet publish ... -o dist/release` then `iscc /DMyAppVersion=1.4.0-rc installer\ClipHive.iss` (installer lands in `dist/`). If ISCC isn't installed: `winget install JRSoftware.InnoSetup`.
- `[human]` Run the Setup as a **standard user**: no UAC prompt; installs under `%LOCALAPPDATA%\Programs\ClipHive`. Then uninstall: it must **ask** before deleting history + key; answer **No** and confirm `%LOCALAPPDATA%\ClipHive` still exists.

## 10 · Cleanup `[agent]`

Restore the user's real data if it was displaced:
`Stop-Process -Name ClipHive -Force; Remove-Item "$env:LOCALAPPDATA\ClipHive" -Recurse; Move-Item "$env:LOCALAPPDATA\ClipHive.bak" "$env:LOCALAPPDATA\ClipHive"` — then relaunch their normal install.

## Results

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 1 | Own-copy marker: paste doesn't reorder | | |
| 2 | Exclusion formats never recorded (×3) | | |
| 3 | Ignore patterns + source_app populated | | |
| 4 | Ctrl+Alt+V keeps Unicode intact | | |
| 5 | Sidebar never flash-closes | | |
| 6 | Hotkey conflict → balloon, tray visible | | |
| 7 | Corrupt key.dat → recovery dialog works | | |
| 8 | v1.3.x DB migrates (user_version=2, dedupe) | | |
| 9 | Installer: no UAC, uninstall asks first | | |

When every row is filled, report the table. If anything failed, include exact observed
behavior + relevant rows from `Get-ClipRows` — the fixes will be made on the
`master` branch and re-verified through CI.
