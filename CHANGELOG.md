# Changelog

## 1.0.0

The first release: a Windows console with the look and feel of the Commodore 64.

- **Milestone 6, ship:** installer (Inno Setup, per user, no admin rights needed), GitHub release workflow, app icon, version shown on the settings screen.
- **Milestone 5, sound and extras:** choice of shell (cmd, Windows PowerShell, PowerShell 7, WSL), C64-style settings screen (Ctrl+Shift+S) saved to `%APPDATA%\C64Console\settings.json`, key clicks and a SID-style start sound.
- **Milestone 4, look and feel:** modern (resizable) and authentic (40×25) layouts, fullscreen (Alt+Enter), CRT effect, the C64 colour keys (Ctrl+F1…F8, Ctrl+Shift+F1…F8), the `READY.` prompt.
- **Milestone 3, VT completeness:** scroll regions, the alternate screen (vim, less, htop), scrollback, resize, copy and paste, 256-colour and 24-bit colour mapped to the C64 palette.
- **Milestone 2, shell hosting:** cmd.exe runs inside the window through ConPTY.
- **Milestone 1, skeleton:** the C64 window, boot screen and blinking cursor.
