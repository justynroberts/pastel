<p align="center">
  <img src="assets/pastel-logo.png" alt="Pastel logo" width="180">
</p>

<h1 align="center">Pastel</h1>

<p align="center"><strong>The missing Windows clipboard manager.</strong></p>

A clipboard manager for Windows, in the spirit of Pasta on macOS — with a
calm, dark, single-accent design. Fully native WPF — a single ~90 KB EXE, no
runtime to install, builds with the C# compiler that ships inside Windows.

UI font is Inter (installed per-user; falls back to Segoe UI), code clips
render in Cascadia Code.

## Install

Run **`dist\PastelSetup-1.2.0.exe`** — a per-user installer (no admin rights
needed). It installs to `%LOCALAPPDATA%\Programs\Pastel`, adds a Start Menu
entry, installs the Inter font if missing, optionally starts Pastel at
sign-in (on by default), and registers a normal uninstaller in Settings →
Apps. Silent deploy: `PastelSetup-1.2.0.exe /VERYSILENT`.

## Build from source

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

Then run `Pastel.exe` directly, or rebuild the installer with
`ISCC.exe installer\pastel.iss` (Inno Setup 7). The app lives in the system
tray.

- **Ctrl+Alt+V** — open/close your clipboard history anywhere
- Everything you copy is captured automatically: **text, rich text (RTF +
  HTML formatting is preserved), code, links, hex colors (with swatch),
  images (with thumbnail), and files**
- **Click a card** (or press **Enter**) to paste it straight into the app you
  came from; **Shift+Enter** pastes as plain text
- **Type to search** instantly; filter with the **All / Pinned / Text / Links /
  Images** pills
- **Ctrl+1–9** quick-pastes the first nine cards
- **Ctrl+P** pins the selected card (pinned clips survive "clear history" and
  pruning), **Del** deletes, **Esc** hides
- **Right-click a card** for Paste / Paste as plain text / Copy only /
  Pin / Delete
- Tray menu: open, pause capture, start with Windows, clear history, quit

## Details

- History (up to 500 clips) persists in `%LOCALAPPDATA%\Pastel\history.json`;
  images are stored as PNGs alongside it
- Duplicate copies bump the existing card to the top and count up (×N)
- Cards show the source app (Chrome, VS Code, Slack, …) and a live "time ago"
- Code is auto-detected and rendered in Consolas; links show their domain;
  single instance enforced (relaunching just opens the window)

## Source layout

```
build.ps1               # builds with the in-box .NET Framework 4.8 csc.exe
src/Pastel.cs           # the whole app (WPF UI, Win32 clipboard listener, tray)
src/app.manifest        # per-monitor-v2 DPI awareness
src/gen-icon.ps1        # generates pastel.ico (gradient clipboard glyph)
installer/pastel.iss    # Inno Setup 7 script (per-user, bundles Inter fonts)
installer/fonts/        # Inter static TTFs + SIL OFL license
dist/                   # PastelSetup-<version>.exe
```

Test flags: `--show` (open window on launch), `--demo` (seed sample cards into
an empty history), `--datadir <path>` (use an alternate data folder),
`--keepvisible` (disable hide-on-focus-loss, for screenshots).
