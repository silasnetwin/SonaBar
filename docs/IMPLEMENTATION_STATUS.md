# Implementation Status

Last updated: 2026-10-23. This working tree is intentionally dirty; preserve every existing change and do not reset, clean, or reinitialize submodules.

## Completed

- Rebranded application, WPF shell, assets, installer, documentation, and release output as **SineFlow by SNine Studios**.
- Implemented the local dictation workflow, history, output processing, file and meeting flows, theme work, updater checks, installer update handoff, startup registration, and GitHub release scaffolding.
- Replaced managed Whisper.net runtime packages with a direct `whisper.cpp` v1.9.4 C ABI bridge in `src/Voxora.Transcription.Native/`.
- Added pinned submodules: `third_party/whisper.cpp` at `927cfce` (`v1.9.4`) and `third_party/NeMo-Speech.cpp` at `4f96762` (`v0.1.0`).
- Added a NeMo-Speech.cpp C ABI Parakeet engine, router, verified Parakeet TDT 0.6B v3 catalog entry, model attribution, release signing hooks, checksums, and update manifest generation.
- **Bundled and verified the Parakeet native runtime.** `build.ps1` now builds the pinned NeMo-Speech.cpp ASR C ABI, copies only the required runtime DLLs (`nemo_speech_asr*`, `ggml*`, `abseil_dll`, `libprotobuf`) plus the OpenMP runtime (`vcomp140.dll`) and packages them with SineFlow. The CMake wrapper links vcpkg's separate Abseil flag archives and adds NeMo's `include` directory explicitly, both of which upstream assumes away. Added SentencePiece, Abseil and protobuf license texts.
- **Added the Qwen3-ASR 1.7B engine.** The catalog stores a seven-file bundle pinned to `Qwen/Qwen3-ASR-1.7B-hf` revision `bcd2b5b7f32b480ab5790554cfa8347f246a14f3`, with per-file SHA-256 verification and resumable downloads. The model runs through an isolated Python sidecar over a random per-launch Windows named pipe with mutual HMAC authentication. The sidecar has no network listener and loads the model with `local_files_only=True`; cancellation uses a Transformers stopping criterion and a bounded process-kill fallback. The app maps its `tl` language code to the model's `fil` code and exposes 30 languages plus automatic detection.
- **Bundled the Qwen Python runtime.** `scripts/package-qwen.ps1` downloads the pinned python-build-standalone CPython 3.12.15 runtime, installs the exact versions in `scripts/qwen-requirements.txt` (PyTorch 2.14.1 CPU, Transformers 5.20.2, tokenizers, NumPy, SoundFile, and support libraries), prunes build-only files, and copies the sidecar into `qwen/`. The packaged runtime is 607 MB and imports `torch`, `transformers`, `soundfile`, and `tokenizers` under `python.exe -I`. `build.ps1` verifies the runtime files before packaging and includes the upstream license texts.
- The speech language picker is now driven by the selected engine. Qwen exposes its 30 languages plus automatic detection, Parakeet exposes its 25 European languages plus automatic detection, and saved unsupported codes fall back to automatic detection instead of failing a dictation.
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

- Inference: `src/Voxora.Transcription/WhisperEngine.cs`, `ParakeetEngine.cs`, `QwenEngine.cs`, `QwenSidecar.cs`, `QwenProtocol.cs`, `Qwen/qwen_sidecar.py`, `src/Voxora.Transcription.Native/CMakeLists.txt`, `SineFlowWhisperNative.cpp`, `src/Voxora.Parakeet.Native/CMakeLists.txt`.
- Models/contracts: `src/Voxora.Core/Domain.cs`, `src/Voxora.Models/ModelManager.cs`, `catalog.json`.
- Voice and hardware: `src/Voxora.Core/HardwareProfile.cs`, `src/Voxora.Core/SilenceTrim.cs`, `src/Voxora.Core/TextPipeline.cs`, `src/Voxora.WindowsIntegration/HardwareDetector.cs`, `src/Voxora.Audio/AudioService.cs`.
- Product/UI: `src/Voxora.App/`, `src/Voxora.Audio/`, `src/Voxora.WindowsIntegration/`, `src/Voxora.Meetings/`, `src/Voxora.Storage/`.
- Branding: `resources/SineFlow.svg`, `resources/SineFlow-mark.svg`, `scripts/update-sineflow-logo.ps1`.
- Packaging/release: `build.ps1`, `scripts/package-qwen.ps1`, `scripts/qwen-requirements.txt`, `installer/Voxora.Setup/`, `.github/workflows/build-windows.yml`, `.github/workflows/release.yml`.
- Licensing: `THIRD_PARTY_NOTICES.md`, `resources/licenses/SentencePiece-LICENSE.txt`, `resources/licenses/Abseil-LICENSE.txt`, `resources/licenses/protobuf-LICENSE.txt`, `resources/licenses/qwen/`.
- Tests: `tests/Voxora.Tests/`, `tests/Voxora.Tests/QwenTests.cs`, `tests/Voxora.NativeSmoke/Program.cs`.

## Verified

- `.tools\dotnet\dotnet.exe build Voxora.slnx -c Release`: passed, 0 warnings, 0 errors.
- `.tools\dotnet\dotnet.exe test Voxora.slnx -c Release`: passed, 193/193, including Qwen protocol, language mapping, bundle verification, bundle download coverage, and idempotent engine disposal.
- Real start with the local profile: window answering after 700-930 ms, ready after 900-1130 ms, dictation start to text in ~300 ms, no hang and no white window.
- `tests/Voxora.NativeSmoke` against the real models: passed, including the new automatic language check, translation into English, French and Chinese, the German prompt, speaker embeddings and PDF export.
- Parakeet native smoke: `.tools\dotnet\dotnet.exe run --project tests\Voxora.NativeSmoke -c Release --no-build -- .tools\jfk.wav artifacts\parakeet-smoke.json --model=parakeet-tdt-0.6b-v3 --speech-only` passed on CPU. The transcript is `and so my fellow americans ask not what your country can do for you ask what you can do for your country` in 1.11 s per pass; the automatic-language pass produced the same text. The runtime does not return a detected language code, so history keeps the requested `auto` value for Parakeet.
- Packaged-runtime smoke: the same test was repeated from `artifacts/package-smoke` with the native DLLs copied from `dist/SineFlow`; it passed, including the automatic-language pass. The portable ZIP contains all required Parakeet DLLs and `vcomp140.dll`, and no unused `libprotobuf-lite.dll`. SHA-256 values in `dist/SHA256SUMS.txt` match both release files.
- Qwen3-ASR model bundle: all seven files matched the catalog. `model.safetensors` is exactly `4,076,193,080` bytes with SHA-256 `2db53c7d81bd9b8cbc6a074e89be2c968a0d373fb4ee68bb1b1e14f7042dfee1`. A direct Transformers CPU probe (16 threads) produced the reference JFK transcript. With the default `bfloat16`, load took 3.53 s and generation took 12.16 s for automatic detection and 12.08 s for forced English; peak working set was 4,457 MB. With `float32`, load took 6.18 s and generation took 10.68 s and 8.14 s; peak working set was 8,043 MB. `bfloat16` stays the default for 8 GB machines; `SINEFLOW_QWEN_DTYPE=float32` remains available for systems with more memory.
- Qwen3-ASR native smoke passed with the final `dist/SineFlow/qwen` runtime and sidecar. The clean run took 35.81 s for the forced-English and automatic-language passes, including two sidecar starts, model loads, and a sidecar unload; a repeat against the rebuilt package passed in 51.79 s under build load. Both transcripts were the reference JFK text, and automatic detection returned `en`. The same test also passed earlier with `artifacts/qwen-runtime-package` (62.19 s with a cold OS file cache).
- Qwen3-ASR lifecycle probe passed with the venv runtime: warm 9.89 s, end-to-end transcribe 19.29 s (including processor input preparation), cancellation `CANCEL_OK`, reuse 29.13 s, unload `UNLOAD_OK`, and restart 29.69 s. Cancellation after a prefill that exceeds the 3-second grace kills and restarts the sidecar; the next request recovers automatically.
- `./build.ps1 -Configuration Release`: passed with 193/193 tests; the .NET build reported 0 warnings and 0 errors. `dist/SineFlow` is 1,064.6 MiB. The portable ZIP is 359.62 MiB (`377,085,914` bytes, SHA-256 `d3727bed27a8918c8f6cd18790ead9b9cf4d8c6650357a99e3675c33e0df4acd`); the installer is 492.95 MiB (`516,899,946` bytes, SHA-256 `63ff5c09264f6f01c5801d4090390142b293960ab56c2b0e417db68e1620004a`). `dist/SineFlow/qwen` contains `python.exe`, `qwen_sidecar.py` and `runtime-info.json`; no model weights are packaged under `dist`.
- Application smoke test: tray created, settings opened, 12 catalogue models, 4 microphones, all 19 pages built and laid out without an error; the process then shut down cleanly with exit code 0.
- Direct whisper.cpp CMake build passed with Visual Studio CMake. The package includes `SineFlowWhisperNative.dll`, `whisper.dll`, `ggml.dll`, `ggml-base.dll`, and `ggml-cpu.dll`.

## Unfinished / Known Issues

- Parakeet is batch-only and does not expose a detected language code through NeMo-Speech.cpp, so the history language remains `auto` when automatic selection is used.
- German was verified with locally synthesised speech. A dictation on a real microphone is still part of the manual checklist.
- The processor topology API reports an inconsistent layout on some Windows builds, so the application reports the logical processor count only.
- The manual device/DPI/sleep/insertion matrix still needs to run against packaged binaries and downloaded models.
- The Parakeet and Qwen runtime and packaging changes are committed and pushed; no release tag has been published yet.

## Exact Next Steps

1. Run the manual matrix from `docs/MANUAL_TEST_CHECKLIST.md` with a real microphone, the browser, Office and a chat application.
2. Create a tag only after separate approval for publication.
