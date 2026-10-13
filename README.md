# C64 Console

A Windows console with the look and feel of the Commodore 64: light-blue border, blue screen, a chunky 8×8 font and the blinking `READY.` cursor.

![Boot screen](docs/boot-screen.png)

See [PLAN.md](PLAN.md) for the full roadmap.

## Status

**Milestone 1 (skeleton) is done.** The app opens a C64-style window with the boot screen and a blinking cursor. You can type, use Enter and Backspace, and text scrolls. Typing is only echoed for now. A real shell (cmd / PowerShell) comes in milestone 2.

## Run it

**Download:** every push builds `C64Console.exe` on GitHub Actions. Open the latest run of the **build** workflow and download the `C64Console-win-x64` artifact. It is self-contained, so you don't need to install .NET.

**Build from source** (Windows, .NET 8 SDK):

```powershell
dotnet run --project src/C64Console.App
```

Run the tests (any OS):

```sh
dotnet test tests/C64Console.Core.Tests
```

Render the boot screen to a PNG (any OS). The arguments are the output file, the scale, and the column count:

```sh
dotnet run --project tools/C64Console.Preview -- docs/boot-screen.png 2 40
```

## Project layout

| Path | What it is |
|------|------------|
| `src/C64Console.Core` | Platform-independent logic: palette, font, screen buffer, frame renderer, boot screen, cursor blink |
| `src/C64Console.App` | WPF window. It shows the rendered frame, scaled by a whole number for crisp pixels |
| `src/C64Console.Core/Assets/font.txt` | The 8×8 font as editable ASCII art |
| `tests/C64Console.Core.Tests` | xUnit tests for Core |
| `tools/C64Console.Preview` | Renders a screenshot PNG without Windows |

## About the font

The font is an original 8×8 design drawn in the C64 style. It does **not** use Commodore's character ROM. To change a glyph, edit the `#`/`.` art in `font.txt`; the file is embedded in the build and checked by the tests.
