# Changelog

All notable changes to ClipHive will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-04-20

### Added
- Clipboard history sidebar with keyboard-driven navigation (↑/↓, Enter, Escape, Delete)
- Real-time search filtering with case-insensitive matching
- AES-256-GCM encryption for all stored clipboard data
- DPAPI machine-scoped key derivation (data stays on this machine only)
- SQLite backend with WAL mode for reliable concurrent access
- Auto-clear policies: 2 hours, 3 days, 15 days, 1 month, or never
- Pin items to keep them at the top of history indefinitely
- System tray icon with context menu
- Configurable global hotkey (default: Ctrl+Shift+V)
- Optional start-with-Windows via registry
- Settings window for hotkey, auto-clear, and startup preferences
- Slide-in sidebar animation (120 ms ease-out from right)
- Single-file self-contained installer (no .NET pre-installed required)
- Zero network calls — verified by code audit and runtime inspection

[Unreleased]: https://github.com/gokulMv/ClipHive/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/gokulMv/ClipHive/releases/tag/v1.0.0
