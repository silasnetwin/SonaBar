<p align="center">
  <img src="docs/media/logo.png" width="88" height="88" alt="SineFlow">
</p>

<h1 align="center">SineFlow</h1>

<p align="center">
  <strong>Speak naturally. Text follows.</strong><br>
  Private, offline speech-to-text for Windows.
</p>

<p align="center">
  <a href="../../releases/latest"><img alt="Latest release" src="https://img.shields.io/badge/release-0.1.3%20early%20access-5C93F0"></a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2011%20x64-0E1116?logo=windows11&logoColor=white">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white">
  <img alt="Engine" src="https://img.shields.io/badge/speech-whisper.cpp-5C93F0">
  <img alt="Offline" src="https://img.shields.io/badge/cloud-none-171B21">
  <img alt="Telemetry" src="https://img.shields.io/badge/telemetry-none-171B21">
  <img alt="License" src="https://img.shields.io/badge/license-proprietary-6B7280">
</p>

<p align="center">
  <a href="../../releases/latest/download/SineFlow-Setup-x64.exe"><img alt="Download for Windows" src="https://img.shields.io/badge/Download-Installer%20(.exe)-5C93F0?style=for-the-badge&logo=windows&logoColor=white"></a>
  <a href="../../releases/latest/download/SineFlow-Portable-x64.zip"><img alt="Portable ZIP" src="https://img.shields.io/badge/Portable-ZIP%20(.zip)-171B21?style=for-the-badge&logo=windows&logoColor=white"></a>
</p>

<p align="center">
  <sub>Windows 11 x64 · no account required</sub>
</p>

<p align="center">
  <a href="#download">Download</a> ·
  <a href="#features">Features</a> ·
  <a href="#screenshots">Screenshots</a> ·
  <a href="#how-it-works">How it works</a> ·
  <a href="#build-from-source">Build</a> ·
  <a href="#faq">FAQ</a>
</p>

---

SineFlow turns speech into text in any Windows application. Hold one shortcut, speak, and the words appear where your cursor is — transcribed on your own machine with [whisper.cpp](https://github.com/ggml-org/whisper.cpp). No account, no cloud, no telemetry, no local network service.

<p align="center">
  <img src="docs/media/overview.png" alt="SineFlow running on Windows 11" width="900">
</p>

## Why SineFlow

- **Offline by design.** Audio never leaves the computer. Models are downloaded once, verified against SHA-256 and reused.
- **Native, not a web view.** A .NET 10 WPF shell with the real Windows title bar, per-monitor DPI handling and a non-activating overlay that never steals focus.
- **One shortcut, everywhere.** Works in browsers, Office, editors, chat clients and terminals through UI Automation, clipboard fallback and Unicode input.
- **Quiet.** A tray icon, a compact status pill and nothing else until you need it.

## Download

| | Package | Size | Notes |
| --- | --- | --- | --- |
| **Installer** | [`SineFlow-Setup-x64.exe`](../../releases/latest/download/SineFlow-Setup-x64.exe) | ~330 MB | Per-user install, Start Menu entry, uninstaller, optional autostart |
| **Portable** | [`SineFlow-Portable-x64.zip`](../../releases/latest/download/SineFlow-Portable-x64.zip) | ~190 MB | Extract and run `SineFlow.App.exe`, no installation |
| **Checksums** | [`SHA256SUMS.txt`](../../releases/latest/download/SHA256SUMS.txt) | 1 KB | Verify every download before running it |

Both packages are self-contained: .NET and the native inference libraries are included. Speech models are downloaded inside the app, not with the installer.

> These links resolve to assets in this private repository, so downloading them requires an account with access.

> **Early access.** 0.1.3 is an unsigned development build. Signed releases and automatic update verification are prepared but not yet published.

### Requirements

| | |
| --- | --- |
| Operating system | Windows 11 x64. Windows 10 22H2 is best effort and not part of the test matrix. |
| Memory | 4 GB for Base, 8 GB for Balanced, 16 GB for the largest model |
| Storage | ~150 MB for the app plus 140 MB – 3 GB per speech model |
| Microphone | Any WASAPI input device |
| Internet | Only for the initial model download and optional updates |

## Install and first run

Run `SineFlow-Setup-x64.exe` for a per-user installation, or extract `SineFlow-Portable-x64.zip` and start `SineFlow.App.exe`. Neither needs administrator rights.

1. **Choose a microphone.** SineFlow lists the available inputs and plays back a short test recording.
2. **Pick a starting profile.** Fast, Balanced or Maximum. Balanced is the everyday choice; the hardware screen explains what your computer can handle.
3. **Download one speech model.** The Models page shows size, license and acceleration for every entry, with resumable downloads and SHA-256 verification.
4. **Press `Ctrl + Shift + Space`** in any text field and start speaking. Press it again to stop.

First you will be offered a short onboarding; afterwards SineFlow lives in the tray.

<p align="center">
  <img src="docs/media/overlay.png" alt="Recording overlay" width="640">
</p>

## What's new in 0.1.3

- **Window first.** The shell is visible in about 0.6 seconds; engine and model work starts afterwards.
- **No silent download.** Start-up no longer pulls a large model in the background. A missing model is an explicit state with a direct link to Models.
- **Native frame and one palette.** Real caption buttons, per-monitor DPI and rounded corners, with dark as the default for new installations.

Full details: [CHANGELOG.md](CHANGELOG.md) · [v0.1.3 release notes](../../releases/tag/v0.1.3).

## Features

### Dictation
- Global hotkey with optional push-to-talk, configurable in Shortcuts.
- Live recording overlay with elapsed time, microphone waveform and a stop button.
- Verified insertion: the target field is inspected first, the text is delivered, then read back where Windows allows it. Password fields are refused, never filled.
- Safe undo and retry from the completion menu, limited to text ranges SineFlow can verify.
- Clipboard fallback that restores your previous clipboard contents.

### Local writing
- Optional local model for punctuation clean-up, conservative corrections and rewrites.
- Spoken commands at the end of a dictation: bullet points, professional email, friendlier tone, spelling only, remove filler words, summary, lowercase, uppercase.
- Translate into English, German, French, Spanish, Italian, Portuguese, Dutch, Polish, Chinese and more.
- Numbers, names and clock times are protected during translation; suspicious results are discarded and the original text is kept.

### Vocabulary and workflows
- Personal dictionary with explicit *heard → intended* replacements.
- Snippets with variables and exact triggers.
- Per-application profiles: tone, insertion strategy, trailing Enter, clipboard mode.
- Statistics for dictations, words, recorded time and average speed.

### Files and meetings
- Transcribe audio and video files in batches; export TXT, SRT and VTT.
- Record meetings from microphone, system audio or both with incremental transcripts.
- Optional local speaker separation, renaming and PDF/Markdown export.

### Privacy
- No account, no telemetry, no advertising identifiers, no cloud transcription.
- No listening socket: the application never opens a local or remote port.
- History is opt-in and can be disabled and securely cleared.
- Every model download is pinned to a SHA-256 digest.

## Screenshots

| Dictation settings | Models |
| --- | --- |
| <img src="docs/media/dictation.png" width="460" alt="Dictation settings"> | <img src="docs/media/models.png" width="460" alt="Model manager"> |

| Preferences | Light theme |
| --- | --- |
| <img src="docs/media/settings.png" width="460" alt="Preferences"> | <img src="docs/media/light-mode.png" width="460" alt="Light theme"> |

Dark is the default. Light and *follow Windows* are one click away, and both palettes share the same contrast rules.

## How it works

```
Microphone ──► WASAPI capture ──► silence trim ──► whisper.cpp (local)
                                                        │
                                   text pipeline ◄──────┘
                    (dictionary, commands, translation, casing)
                                                        │
                     UI Automation ──► clipboard / Unicode insertion
```

| Layer | Responsibility |
| --- | --- |
| `Voxora.Core` | Settings, readiness states, text pipeline, hardware profiles |
| `Voxora.Audio` | WASAPI capture, resampling, ducking |
| `Voxora.Transcription` | whisper.cpp bridge, Parakeet adapter, engine routing |
| `Voxora.Enhancement` | Local writing model (llama.cpp) |
| `Voxora.Models` | Catalogue, verified downloads, integrity |
| `Voxora.WindowsIntegration` | Hotkeys, focus and field inspection, insertion, tray |
| `Voxora.Storage` | SQLite history, atomic settings |
| `Voxora.App` | WPF shell, overlay, pages, updates |

The UI never waits on another application: field inspection runs on a dedicated automation thread with a hard timeout, and a field inside SineFlow is inspected on the interface thread instead, so a busy target can neither freeze the window nor stall a dictation.

## Build from source

Requirements: .NET 10 SDK, Visual Studio 2022 with the C++ desktop workload and CMake for the native inference libraries.

```powershell
git clone https://github.com/silasnetwin/SineFlow.git
cd SineFlow
git submodule update --init --recursive

# Build, test, publish and package (portable ZIP + installer)
./build.ps1 -Configuration Release

# Or step by step
.tools/dotnet/dotnet build Voxora.slnx -c Release
.tools/dotnet/dotnet test  Voxora.slnx -c Release
```

The release build writes `dist/SineFlow-Portable-x64.zip`, `dist/SineFlow-Setup-x64.exe` and `dist/SHA256SUMS.txt`.

### Native inference check

The smoke test transcribes public reference audio with the real models and prints a JSON report:

```powershell
.tools/dotnet/dotnet run --project tests/Voxora.NativeSmoke -c Release -- .tools/jfk.wav artifacts/native-smoke.json
```

### Repository layout

```
src/            application and modules
tests/          xUnit suite and native smoke test
installer/      per-user installer
resources/      icons, brand vectors, third-party licenses
scripts/        asset and dependency tooling
docs/           design notes, verification and manual test matrix
```

## Status

| Area | State |
| --- | --- |
| Dictation, insertion, history, snippets, file transcription | Stable |
| Meeting capture, speaker separation, PDF export | Stable |
| Local writing models, translation, spoken commands | Stable |
| Parakeet (NeMo-Speech.cpp) | Adapter present, native runtime not bundled yet |
| Qwen ASR | Not implemented |
| Code signing, update feed | Prepared, not published |

Known limits are documented in [docs/IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md). Device and application checks live in [docs/MANUAL_TEST_CHECKLIST.md](docs/MANUAL_TEST_CHECKLIST.md).

## FAQ

**Does audio leave the computer?**
No. Capture, transcription and the optional writing model run on the local machine. There is no account system, no telemetry and no listening socket.

**Which model should I start with?**
Balanced. It pairs Whisper small with the everyday profile and fits 8 GB machines. Base is the choice for 4 GB systems and slower hardware; large-v3 is for maximum accuracy on 16 GB machines.

**Does SineFlow type into password fields?**
No. Password fields are recognised and refused; no text is inserted there.

**Can I run it without installing?**
Yes. The portable ZIP contains the same build. Extract it and run `SineFlow.App.exe`; settings and history still live under `%LOCALAPPDATA%`.

**Which applications are supported?**
Any Windows application with a standard text field: browsers, Office, editors, chat clients and terminals. Insertion uses UI Automation first, then clipboard and Unicode input where a field cannot be inspected.

**Is there a Mac or Linux version?**
No. The shell, hotkeys and insertion layer are Windows-specific.

## Documentation

| Document | What it covers |
| --- | --- |
| [CHANGELOG.md](CHANGELOG.md) | Release history and user-visible changes |
| [docs/IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md) | Implemented features and known gaps |
| [docs/MANUAL_TEST_CHECKLIST.md](docs/MANUAL_TEST_CHECKLIST.md) | Device, installer and application checks |
| [SECURITY.md](SECURITY.md) | Private vulnerability reporting |
| [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) | Licenses of bundled components |

## Contributing

This repository is private. Bug reports and suggestions belong in the issue tracker; please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

## Security

Please do not open a public issue for a security problem. See [SECURITY.md](SECURITY.md) for the private reporting route.

## License

SineFlow is proprietary software by SNine Studios. All rights reserved. See [LICENSE](LICENSE).

Bundled third-party components keep their own licenses: whisper.cpp, llama.cpp, .NET, NAudio, SQLite, PDFsharp, sherpa-onnx and WeSpeaker. The full attribution list is in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and `resources/licenses/`.

---

<p align="center">
  <sub>Built for Windows · Made by SNine Studios</sub>
</p>
