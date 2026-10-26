# WhisperBar parity audit — 3 October 2026 (updated 25 October 2026)

Sources: [current product page](https://whisperbar.pro/) and [full public changelog through 1.20.1](https://whisperbar.pro/changelog.html), checked on 3 October 2026. WhisperBar descriptions below are public claims, not independently reproduced behavior. SonaBar status is based on repository inspection and the tests stated below. “Implemented” does not mean equal accuracy or polish.

| Feature | WhisperBar | SonaBar | Implementation | Tested | Notes |
|---|---|---|---|---|---|
| Global dictation | Shortcut, waveform, cursor insertion | Implemented | Windows hotkey, WASAPI, whisper.cpp, guarded insertion | Unit and build | Physical microphone and external apps remain manual |
| Default shortcut | ⌘⇧Space | Ctrl+Shift+Space | Existing default, custom value persisted | Unit | Windows equivalent |
| Push to talk | Hold and release | Implemented | Global hotkey key-up path | Build | Real key timing remains manual |
| Overlay and cancellation | Floating status; cancel through enhancement | Implemented | Nonactivating WPF pill and cancellation token | Build | Mixed-DPI and inference cancellation remain manual |
| Optional sounds | Start, done, error | Implemented | Original generated WAV cues, Off/Minimal/Standard, best-effort playback | Unit/build | Output-device failure is ignored by design |
| Audio ducking | Lowers audio during recording | Implemented | Windows audio session/volume recovery | Unit | Device changes remain manual |
| Speech engines | Qwen3-ASR, Parakeet, Whisper | Implemented | Local whisper.cpp, bundled NeMo-Speech.cpp Parakeet C ABI, and authenticated Qwen3-ASR Python sidecar | Native smoke (Whisper, Parakeet and Qwen) | No comparable accuracy or speed benchmark; Qwen end-to-end sidecar timing is measured on CPU |
| Automatic language | Model-dependent detection | Implemented | Whisper auto language; Parakeet automatic decode; Qwen automatic language request | Native smoke | The bridge asked whisper.cpp to detect only, which returned before the transcription; corrected and covered by a native check. Parakeet accepts `auto` but the runtime returns no detected language code, so history keeps `auto`. Qwen returns a parsed language when automatic detection is used. |
| Custom words | Recognition and enhancement guidance | Implemented | Whisper prompt, writing context, explicit correction rules | Unit | Effect depends on model |
| Enhance and self-corrections | Grammar, fillers, context, instructions | Partial | Fast deterministic cleanup, spoken commands for bullets, professional email, tone, spelling, fillers and summary, explicit actions through local writer | Unit/native smoke in this pass | No general semantic intent classifier; automatic writer is deliberately limited |
| Spoken translation | Explicit natural commands | Partial | Deterministic end-command patterns, local translation and protected details | Unit/native smoke in this pass | Ambiguous commands and every language pair untested |
| Selected text | Floating menu, keyboard, typed/spoken instructions | Implemented | Selection controller and compact WPF panel | Unit/build | Browser and Office replacement manual |
| Snippets | Spoken text expansion | Implemented | Exact spoken triggers, variables, management UI and opt-in typed trigger expansion with multi-word/punctuation triggers | Unit/build | Typed expansion is off by default; the hook skips SonaBar and password fields and clears on click or line break; a physical-keyboard test remains manual |
| History | Search, date filter, quick copy | Implemented | SQLite, retention and privacy choice | Unit | Sort control and raw/result comparison need more work |
| Lifetime statistics | Count, time, words, languages | Implemented | Separate aggregate table | Unit | Not reset by clearing history |
| Files and folders | Audio/video and background batch | Implemented | FFmpeg-backed queue, picker/drop, TXT/SRT/VTT | Unit/build | Real long-file priority and all codecs untested |
| Meetings | Mic/system/both, live text, speakers, notes, PDF | Partial | WASAPI loopback, segments, ONNX speaker embeddings, exports | Native smoke in prior pass | Multi-speaker quality and long meetings untested |
| Per-app Return | Optional | Implemented | Application profile | Unit/build | Real target apps manual |
| Opaque game windows | Paste-only compatibility | Implemented | Compatibility insertion profile, including a Roblox default | Unit/build | Paste cannot be read back; the game must show the result |
| Clipboard | Configurable preservation | Implemented | Sequence-aware capture and restore | Unit | Rich target matrix manual |
| Model downloads | Resume and fallback | Implemented | Partial download verification, installed model fallback | Unit/build | Interrupted network test manual |
| Pricing | One-time purchase | Free | No checkout, subscription, trial timer or licence server | Code audit | Intentional difference: SonaBar stays free |
| Privacy | Local core, no telemetry | Implemented | Local models, optional history, temporary audio cleanup | Code audit | Optional external integrations are absent |
| Diagnostics/help | Local logs and support details | Implemented | T0–T11, model and delivery logs, copy summary and in-app Help page under **About → Help** | Build/smoke | Help is self-contained; no external documentation viewer |
| Settings/tray/onboarding | Compact sidebar and utility surfaces | Partial | WPF sidebar, tray, onboarding, General workspace hub and empty states | Build/smoke | Direct visual comparison not verified |
| App icon | macOS app icon | Windows original | SonaBar multi-size ICO and PNG assets | Existing assets inspected | Windows integration requires clean install checks |

## This pass

- Added an in-app Help page with quick start, delivery troubleshooting, privacy, free-software and diagnostics guidance. It is reachable under **About → Help** and is included in the page smoke test.
- Added optional feedback sounds with original generated WAV cues. **Minimal** plays the finished tone; **Standard** adds start and error tones; **Off** remains the default. Playback is best-effort and never turns a missing output device into a dictation failure. The Audio page includes a preview button, disabled while sounds are off.
- Added opt-in typed snippet expansion. A trigger followed by space, Enter, Tab or punctuation expands in the focused application, with word-boundary matching and `{date}`, `{time}` and `{clipboard}` variables. Multi-word and punctuation triggers are buffered, a mouse click or unmatched line break clears the buffer, and injected input is ignored. It is off by default, skips SonaBar's own window and password fields, and warns that games or elevated apps may reject injected text.
- Polished the workspace: a ten-destination General hub, Help under **About → Help**, clearer page headings, empty states for History, Statistics, Snippets and Applications, and a factual About build line.
- Confirmed that SonaBar remains free: no checkout, subscription, trial timer, licence server or fake purchase UI. Pricing, independent SonaBar branding and the Windows-native frame are intentional differences from WhisperBar.
- Gemma 4 is not bundled yet. The public `unsloth/gemma-4-4b-it-GGUF` repository returns HTTP 401 without authenticated access, so SonaBar keeps its licensed Qwen writing models until an authorised, non-gated source is supplied.
- Preserved the existing uncommitted insertion, waveform, selected-text, meeting and branding work.
- Bundled the Parakeet TDT 0.6B v3 native runtime and verified it end to end on CPU. `build.ps1` now builds the pinned NeMo-Speech.cpp ASR C ABI, links vcpkg's separate Abseil flag archives, adds NeMo's include directory explicitly, copies the required DLL closure, and packages the OpenMP runtime needed by ggml.
- Added SentencePiece, Abseil and protobuf license texts to the packaged notices.
- Added the Qwen3-ASR 1.7B engine as a pinned seven-file model bundle and an authenticated named-pipe Python sidecar. The model is loaded offline, the app maps `tl` to `fil`, and the sidecar supports warm, transcribe, cancel and unload requests.
- Added the embedded CPython 3.12.15 runtime packaging step with exact PyTorch 2.14.1 CPU and Transformers 5.20.2 versions, runtime guards, and upstream license texts.
- The language picker now follows the selected engine's capabilities, and a saved unsupported code falls back to automatic detection before dictation.
- Fixed the automatic language setting. The native bridge asked whisper.cpp to detect the language only, and whisper.cpp returns right after the detection. With the default setting SonaBar recognised the language and then inserted nothing. The bridge now detects and transcribes in one call, and the native smoke test checks it.
- Added hardware detection and an automatic speech profile with the measured machine explained in plain words.
- Added a foreground tracker and last-external-window restore so the Start button in SonaBar returns to the app behind it before target capture instead of targeting SonaBar's own button.
- Added voice activity detection for the leading and trailing silence of a recording, without touching a pause inside a sentence.
- Added the spoken formatting commands with examples in the settings. Casing runs without the writing model.
- Rewrote the icon pipeline around a vector master in `resources/SonaBar.svg`.
- Fixed a settings file that was reset in full when it contained a readable enum name.
- Changed the recording gate and recognition path to accept an installed compatible Whisper model when the preferred one is absent.
- Silent or accidental recordings now dismiss quietly.
- A failed explicit spoken translation or rewrite now leaves the raw transcript available to copy and inserts nothing. Ordinary cleanup may still fall back to the transcript.

## Verification and remaining gaps

After these edits, 223/223 automated tests pass. The full `./build.ps1 -Configuration Release` package command is the release gate; the .NET build reports 0 warnings and 0 errors. The application smoke test builds and lays out all 20 pages with no failures. Package sizes are recorded in the release notes rather than in this audit, so they cannot drift from the shipped artifacts. Native smoke tests must be run against the packaged output; the commands and latest results are in the release notes. No direct WhisperBar binary comparison, physical microphone, browser/Office insertion, multi-monitor/DPI, multi-speaker meeting or clean-machine installer test is claimed. A physical-keyboard typed-snippet test, broader spoken intent, Gemma 4 packaging and measured parity remain open.
