# C64 Console: Plan

A Windows console (terminal) app that looks and feels like a Commodore 64. It runs a real Windows shell (`cmd.exe`, PowerShell or WSL) inside a C64-style screen: light-blue border, dark-blue background, PETSCII-style font, blinking block cursor, the `READY.` prompt and an optional CRT effect.

> **Scope:** This is a *console emulator* with C64 styling. It is **not** a hardware emulator that runs C64 software (6510 CPU, VIC-II, SID). If you want that, see "Alternative scope" at the end.

---

## 1. Goals

| # | Goal | Must / Nice |
|---|------|-------------|
| G1 | Run a real Windows shell (cmd / PowerShell / pwsh / WSL) interactively | Must |
| G2 | Authentic C64 look: 16-colour palette, light-blue on blue, wide border, 8×8 pixel font, pixel-perfect scaling | Must |
| G3 | C64 boot screen: `**** COMMODORE 64 BASIC V2 ****`, `64K RAM SYSTEM  38911 BASIC BYTES FREE`, `READY.` | Must |
| G4 | Blinking block cursor (the C64 blinks at ~1.5 Hz with reverse video) | Must |
| G5 | Two layouts: **Authentic** 40×25 and **Modern** 80×25 or resizable (40 columns is too narrow for most shell output) | Must |
| G6 | CRT effect: scanlines, slight curvature, bloom, vignette (can be switched off) | Nice |
| G7 | Sound: key clicks, startup "chirp" | Nice |
| G8 | Ctrl+F1..F8 keys set the text colour, like the C64's CTRL/C= + 1–8 | Nice |
| G9 | Fullscreen (Alt+Enter), settings file, installer | Must |

## 2. Recommended technology

| Concern | Choice | Why |
|---------|--------|-----|
| Language / runtime | **C# / .NET 8** | Native Windows, simple P/Invoke for ConPTY, easy to ship as a single exe |
| Window + rendering | **WPF** with a `WriteableBitmap` framebuffer, or **Win2D / Direct2D** if we need shaders | Draw into a low-res bitmap, then scale up with nearest-neighbour for crisp pixels |
| CRT shader | Direct2D custom effect / HLSL pixel shader (or `ShaderEffect` in WPF) | Scanlines and curvature on the GPU |
| Shell hosting | **Windows ConPTY** (`CreatePseudoConsole`, Windows 10 1809+) | The official way to host cmd/PowerShell with full VT output |
| VT/ANSI parsing | A small custom VT100/xterm parser (CSI, SGR, cursor movement, erase) | ConPTY sends VT sequences; we only need a subset |
| Font | **C64 Pro Mono** (Style64), or our own 8×8 bitmap font | Check the licence. We must not ship the original Commodore character ROM. |
| Audio | NAudio or `System.Media` with short WAV samples | Key clicks and startup sound |
| Packaging | `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`, then MSIX or Inno Setup | One file that runs without installing .NET |
| Tests | xUnit for the VT parser and screen buffer | The parser is the most bug-prone part |

**Alternative stacks:** Rust + `wgpu` + `portable-pty`, or C++ + Direct2D. These are faster but take longer to build. Electron + `xterm.js` + `node-pty` is quick to build, but the app is large and pixel-perfect rendering is harder.

## 3. Architecture

```
┌────────────────────────── C64Console.exe ──────────────────────────┐
│                                                                    │
│  Keyboard ──► InputMapper ──► ConPTY input pipe ──► cmd/pwsh/wsl   │
│                                                        │           │
│  ConPTY output pipe ◄───────────────────────────────────┘          │
│        │                                                           │
│        ▼                                                           │
│   VtParser ──► ScreenBuffer (cells: char, fg, bg, reverse)         │
│                       │                                            │
│                       ▼                                            │
│   Renderer: glyph atlas (8×8) → framebuffer (e.g. 384×272          │
│             with border) → nearest-neighbour scale → CRT shader    │
│                                                                    │
│   ThemeManager (palette, border, layout)   Settings (JSON)         │
│   BootSequence (fake C64 banner)           SoundManager            │
└────────────────────────────────────────────────────────────────────┘
```

### Main components

1. **PseudoConsoleHost**: creates the pipes and the pseudo console, starts the shell with `STARTUPINFOEX`, resizes it with `ResizePseudoConsole` and cleans up when the shell exits.
2. **VtParser**: a state machine (ground / escape / CSI / OSC). It handles SGR colours, cursor moves (CUP, CUU…), erase (ED, EL), scroll regions and the alternate screen buffer.
3. **ScreenBuffer**: a grid of cells plus scrollback, cursor state, dirty-region tracking and wide-character fallback.
4. **ColorMapper**: maps ANSI's 16 colours and 256/truecolour to the nearest of the **16 C64 colours** (Pepto/Colodore palette). Modern mode can optionally show true colours.
5. **Renderer**: blits glyphs from a pre-coloured atlas into the framebuffer, draws the border, applies the cursor blink and scales by an integer factor (2×, 3×, 4×) to fit the window.
6. **InputMapper**: turns key events into VT input sequences (arrows, Ctrl, function keys) and handles app hotkeys (Alt+Enter, Ctrl+F1..F8, Ctrl+Shift+C/V).
7. **BootSequence**: shows the C64 banner when the app starts, then hands over to the shell. A custom prompt (`READY.` in PowerShell, `PROMPT` in cmd) adds to the illusion.
8. **Settings**: `%APPDATA%\C64Console\settings.json` stores the shell, layout, scale, CRT on/off, sound on/off and palette.

### C64 palette (Colodore)

| Idx | Name | Hex | Idx | Name | Hex |
|----|------|-----|----|------|-----|
| 0 | Black | `#000000` | 8 | Orange | `#8E5029` |
| 1 | White | `#FFFFFF` | 9 | Brown | `#553800` |
| 2 | Red | `#813338` | 10 | Light red | `#C46C71` |
| 3 | Cyan | `#75CEC8` | 11 | Dark grey | `#4A4A4A` |
| 4 | Purple | `#8E3C97` | 12 | Grey | `#7B7B7B` |
| 5 | Green | `#56AC4D` | 13 | Light green | `#A9FF9F` |
| 6 | Blue | `#2E2C9B` | 14 | Light blue | `#706DEB` |
| 7 | Yellow | `#EDF171` | 15 | Light grey | `#B2B2B2` |

Defaults: border = 14 (light blue), background = 6 (blue), text = 14 (light blue).

### Screen geometry

- C64 text area: 40×25 cells × 8×8 px = **320×200**. The visible border brings this to about **384×272**.
- Authentic mode keeps exactly these sizes. Modern mode uses 80×25 (640×200, shown with a 1:2 pixel aspect) or a free grid that resizes with the window.

## 4. Milestones

| Phase | Deliverable | Est. effort |
|-------|-------------|-------------|
| **0. Quick win (optional, 1 hour)** | A Windows Terminal profile with a C64 colour scheme, C64 Pro Mono font and a CRT `experimental.pixelShaderPath` HLSL shader. Proves the look before writing any code. | 0.5 day |
| **1. Skeleton** | .NET 8 solution, WPF window, framebuffer, 8×8 font atlas, static C64 boot screen with blinking cursor | 2–3 days |
| **2. Shell hosting** | ConPTY host plus a minimal VT parser. `cmd.exe` runs, you can type and see output, and Enter, Backspace and arrow keys work. | 3–5 days |
| **3. VT completeness** | SGR colours mapped to the C64 palette, erase/scroll, alternate screen (so `vim` and `less` work in WSL), resize, scrollback with the mouse wheel, copy/paste | 5–7 days |
| **4. Look & feel polish** | Authentic/Modern layouts, integer scaling, fullscreen, CRT shader, Ctrl+F1..F8 colour keys, custom `READY.` prompt scripts for cmd and PowerShell | 3–4 days |
| **5. Sound & extras** | Key clicks, startup chirp, settings UI (in the C64 style), choice of shell | 2 days |
| **6. Ship** | Unit tests for the parser and buffer, GitHub Actions CI (`windows-latest`), single-file publish, MSIX or Inno Setup installer, README with screenshots | 2–3 days |

**Total:** about 3–4 weeks for one developer, part time.

## 5. Proposed repository layout

```
/src
  C64Console.App/          WPF app: window, input, settings
  C64Console.Core/         VtParser, ScreenBuffer, ColorMapper (no UI; testable)
  C64Console.Pty/          ConPTY P/Invoke wrapper
  C64Console.Rendering/    Glyph atlas, framebuffer, CRT shader
/assets
  fonts/  shaders/  sounds/
/tests
  C64Console.Core.Tests/
/extras
  windows-terminal/        Phase 0 profile, colour scheme and HLSL shader
  prompts/                 READY.-style prompts for cmd and PowerShell
.github/workflows/build.yml
```

## 6. Risks and how we handle them

| Risk | Mitigation |
|------|------------|
| 40 columns breaks most CLI output | Modern 80-column / resizable mode is the default; Authentic mode is for nostalgia |
| Unicode and box-drawing characters don't exist in PETSCII | Fall back to the closest PETSCII graphic or an ASCII substitute, with an optional fallback font |
| Font/ROM copyright | Use C64 Pro Mono within its licence, or draw our own 8×8 font; never ship Commodore ROMs |
| ConPTY quirks (resize reflow, Windows 10 versions) | Require Windows 10 1809+; test on Windows 10 and 11 |
| Mapping truecolour down to 16 colours looks wrong | Nearest-colour mapping in a perceptual colour space (CIE Lab); a "true colour" toggle |
| Rendering speed with big output (e.g. `dir /s`) | Dirty-rectangle rendering, batched parser input, cap the frame rate at 60 fps |

## 7. Alternative scope: a real C64 emulator

If you want to **run C64 programs** (BASIC, games, `.prg`/`.d64` files), this is a much bigger project: a 6510 CPU, VIC-II video, SID sound, CIA timers, 1541 disk emulation, and legally obtained ROMs. In that case the recommendation is to build on or embed **VICE** (open source) rather than starting from scratch. It is several months of work.

## 8. Next steps

1. Confirm the scope (console with the C64 look vs. real C64 emulator).
2. Confirm the stack (C#/.NET 8 + WPF is the recommendation).
3. Start with Phase 0 (Windows Terminal theme) for a quick visual check, then Phase 1.
