# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.x     | ✅ Active support  |

## Reporting a Vulnerability

**Please do NOT open a public GitHub issue for security vulnerabilities.**

Use one of these channels:

1. **GitHub Private Vulnerability Reporting** (preferred) — [Report a vulnerability](https://github.com/levitasOrg/cliphive/security/advisories/new)
2. **Email** — security@cliphive.dev

### What to include

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Any suggested fixes (optional)

### Response SLA

- **Acknowledgement:** within 48 hours
- **Initial assessment:** within 5 business days
- **Fix timeline:** based on severity — Critical ≤ 7 days, High ≤ 30 days, Medium ≤ 90 days

### Disclosure policy

We follow coordinated disclosure. We ask that you give us reasonable time to address the issue before making it public. We will credit reporters in the release notes unless you prefer to remain anonymous.

## Security Design

ClipHive is designed with privacy as a first principle:

- **No network calls** — the app never makes outbound connections
- **AES-256-GCM encryption** — all clipboard data (including OCR-extracted image text) is encrypted before being written to SQLite
- **DPAPI user-scope key** (since v1.2.0) — a random 256-bit key is protected with Windows DPAPI (CurrentUser scope) and stored at `%LOCALAPPDATA%\ClipHive\key.dat`; only the same Windows account can unprotect it, so the database cannot be decrypted by another user or on another machine (roaming-profile setups excepted, where DPAPI keys legitimately follow the account)
- **Keyed dedupe fingerprints** — duplicate detection uses HMAC-SHA256 under a key derived from the encryption key, never a bare hash of the plaintext, so the database contains nothing that confirms content guesses offline
- **Password-manager aware** — content marked with the standard exclusion clipboard formats (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory=0`, `Clipboard Viewer Ignore`) is never recorded
- **Minimal attack surface** — no web server, no IPC server, no plugins, no scripting engine
