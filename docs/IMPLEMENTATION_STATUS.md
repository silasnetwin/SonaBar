# Implementation Status

Last updated: 2026-10-22. This working tree is intentionally dirty; preserve every existing change and do not reset, clean, or reinitialize submodules.

## Completed

- Rebranded application, WPF shell, assets, installer, documentation, and release output as **SineFlow by SNine Studios**.
- Implemented the local dictation workflow, history, output processing, file and meeting flows, theme work, updater checks, installer update handoff, startup registration, and GitHub release scaffolding.
- Replaced managed Whisper.net runtime packages with a direct `whisper.cpp` v1.9.4 C ABI bridge in `src/Voxora.Transcription.Native/`.
- Added pinned submodules: `third_party/whisper.cpp` at `927cfce` (`v1.9.4`) and `third_party/NeMo-Speech.cpp` at `4f96762` (`v0.1.0`).
- Added a NeMo-Speech.cpp C ABI Parakeet engine, router, verified Parakeet TDT 0.6B v3 catalog entry, model attribution, release signing hooks, checksums, and update manifest generation.
- Created `AGENTS.md` contributor guidance and `THIRD_PARTY_NOTICES.md` attribution updates.
- **Fixed the automatic language setting.** The native bridge asked whisper.cpp to detect the language only, which makes it return before the transcription. The default setting produced no text at all. See `docs/VOICE_AND_HARDWARE_PASS.md`.
- **Added hardware detection and an automatic speech profile.** Memory, processor count, graphics adapter with dedicated memory, acceleration runtimes and free disk space are measured and explained in plain words.
- **Added voice activity detection** that removes the leading and trailing silence of a recording without ever touching a pause inside a sentence.
- **Added the spoken formatting commands** (bullets, professional email, friendlier tone, spelling only, fillers, summary, lower case, upper case) with examples in the settings.
- **Rewrote the icon pipeline around a vector master.** `resources/SineFlow.svg` and `resources/SineFlow-mark.svg` generate every PNG size and the `.ico`.
- **Fixed the settings file.** A hand edited `settings.json` with a readable enum name used to reset the whole configuration; enums are now written as names and read in both forms.
- **Fixed the endless "Getting ready" state and the long start.** Target capture now runs on a dedicated STA thread with a hard timeout, a field inside SineFlow is inspected on the interface thread, the window appears before any engine work, and a missing model is an explicit state instead of a silent 465 MB download. See `docs/STARTUP_AND_FRAME_2026-10-22.md`.
- **Moved to the native Windows frame and one colour system.** `WindowStyle.SingleBorderWindow` with a DWM-coloured caption, a rounded window region instead of a layered overlay, and every colour in `src/Voxora.App/Palette.cs` (dark by default).

## Main Changed Files

- Inference: `src/Voxora.Transcription/WhisperEngine.cs`, `ParakeetEngine.cs`, `src/Voxora.Transcription.Native/CMakeLists.txt`, `SineFlowWhisperNative.cpp`.
- Models/contracts: `src/Voxora.Core/Domain.cs`, `src/Voxora.Models/ModelManager.cs`, `catalog.json`.
- Voice and hardware: `src/Voxora.Core/HardwareProfile.cs`, `src/Voxora.Core/SilenceTrim.cs`, `src/Voxora.Core/TextPipeline.cs`, `src/Voxora.WindowsIntegration/HardwareDetector.cs`, `src/Voxora.Audio/AudioService.cs`.
- Product/UI: `src/Voxora.App/`, `src/Voxora.Audio/`, `src/Voxora.WindowsIntegration/`, `src/Voxora.Meetings/`, `src/Voxora.Storage/`.
- Branding: `resources/SineFlow.svg`, `resources/SineFlow-mark.svg`, `scripts/update-sineflow-logo.ps1`.
- Packaging/release: `build.ps1`, `installer/Voxora.Setup/`, `.github/workflows/build-windows.yml`, `.github/workflows/release.yml`.
- Tests: `tests/Voxora.Tests/`, `tests/Voxora.NativeSmoke/Program.cs`.

## Verified

- `.tools\dotnet\dotnet.exe build Voxora.slnx -c Release`: passed, 0 warnings, 0 errors.
- `.tools\dotnet\dotnet.exe test Voxora.slnx -c Release`: passed, 184/184.
- Real start with the local profile: window answering after 700-930 ms, ready after 900-1130 ms, dictation start to text in ~300 ms, no hang and no white window.
- `tests/Voxora.NativeSmoke` against the real models: passed, including the new automatic language check, translation into English, French and Chinese, the German prompt, speaker embeddings and PDF export.
- Application smoke test: tray created, settings opened, 11 catalogue models, 4 microphones, all 19 pages built and laid out without an error.
- Direct whisper.cpp CMake build passed with Visual Studio CMake. The package includes `SineFlowWhisperNative.dll`, `whisper.dll`, `ggml.dll`, `ggml-base.dll`, and `ggml-cpu.dll`.

## Unfinished / Known Issues

- Qwen3-ASR 1.7B isolated authenticated named-pipe Python sidecar has not yet been implemented or bundled.
- NeMo-Speech.cpp source is pinned and its C ABI adapter exists, but `nemo_speech_asr_c.dll` is not yet built or copied by `build.ps1`; selecting Parakeet will therefore fail until its native runtime closure is packaged.
- NeMo-Speech.cpp's required `ggml` submodule is initialized at `c03b4e2`; SentencePiece/vcpkg dependencies are still required before its ASR target can build on Windows.
- German was verified with locally synthesised speech. A dictation on a real microphone is still part of the manual checklist.
- The processor topology API reports an inconsistent layout on some Windows builds, so the application reports the logical processor count only.
- Real reference-audio smoke inference and the manual device/DPI/sleep/insertion matrix still need to run against packaged binaries and downloaded models.
- GitHub remote is not configured locally and no commit, push, or release has been created.

## Exact Next Steps

1. Install/pin SentencePiece through Visual Studio vcpkg (`C:\Program Files\Microsoft Visual Studio\2022\Community\VC\vcpkg\vcpkg.exe`), then configure NeMo with ASR on and TTS/NMT/CLI/HTTP/diarization off.
2. Extend `build.ps1` to build `nemo_speech_asr_c`, copy its DLL closure and licenses to `dist/SineFlow`, and add a Parakeet native smoke command.
3. Implement and package the Qwen3-ASR sidecar: embedded Python runtime, pinned `QwenLM/Qwen3-ASR` revision, random per-launch Windows named pipe, mutual secret authentication, cancellation, and no network listener.
4. Run the manual matrix from `docs/MANUAL_TEST_CHECKLIST.md` with a real microphone, the browser, Office and a chat application.
5. Then configure the `origin` remote, commit the dirty tree, push, and create a tag only after user approval for publication.
