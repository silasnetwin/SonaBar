# WhisperBar parity audit — 3 October 2026

Sources: [current product page](https://whisperbar.pro/) and [full public changelog through 1.20.1](https://whisperbar.pro/changelog.html), checked on 3 October 2026. WhisperBar descriptions below are public claims, not independently reproduced behavior. SineFlow status is based on repository inspection and the tests stated below. “Implemented” does not mean equal accuracy or polish.

| Feature | WhisperBar | SineFlow | Implementation | Tested | Notes |
|---|---|---|---|---|---|
| Global dictation | Shortcut, waveform, cursor insertion | Implemented | Windows hotkey, WASAPI, whisper.cpp, guarded insertion | Unit and build | Physical microphone and external apps remain manual |
| Default shortcut | ⌘⇧Space | Ctrl+Shift+Space | Existing default, custom value persisted | Unit | Windows equivalent |
| Push to talk | Hold and release | Implemented | Global hotkey key-up path | Build | Real key timing remains manual |
| Overlay and cancellation | Floating status; cancel through enhancement | Implemented | Nonactivating WPF pill and cancellation token | Build | Mixed-DPI and inference cancellation remain manual |
| Optional sounds | Start, done, error | Gap | Setting exists but no bundled feedback player | No | Off is the only effective option |
| Audio ducking | Lowers audio during recording | Implemented | Windows audio session/volume recovery | Unit | Device changes remain manual |
| Speech engines | Qwen3-ASR, Parakeet, Whisper | Partial | Local whisper.cpp, Fast/Balanced/Maximum | Native smoke in this pass | No comparable accuracy or speed benchmark; other engines absent |
| Automatic language | Model-dependent detection | Fixed in this pass | Whisper auto language | Native smoke in this pass | The bridge asked whisper.cpp to detect only, which returned before the transcription; corrected and covered by a native check |
| Custom words | Recognition and enhancement guidance | Implemented | Whisper prompt, writing context, explicit correction rules | Unit | Effect depends on model |
| Enhance and self-corrections | Grammar, fillers, context, instructions | Partial | Fast deterministic cleanup, spoken commands for bullets, professional email, tone, spelling, fillers and summary, explicit actions through local writer | Unit/native smoke in this pass | No general semantic intent classifier; automatic writer is deliberately limited |
| Spoken translation | Explicit natural commands | Partial | Deterministic end-command patterns, local translation and protected details | Unit/native smoke in this pass | Ambiguous commands and every language pair untested |
| Selected text | Floating menu, keyboard, typed/spoken instructions | Implemented | Selection controller and compact WPF panel | Unit/build | Browser and Office replacement manual |
| Snippets | Spoken text expansion | Implemented | Exact spoken triggers, variables and management UI | Unit | Typed trigger expansion absent |
| History | Search, date filter, quick copy | Implemented | SQLite, retention and privacy choice | Unit | Sort control and raw/result comparison need more work |
| Lifetime statistics | Count, time, words, languages | Implemented | Separate aggregate table | Unit | Not reset by clearing history |
| Files and folders | Audio/video and background batch | Implemented | FFmpeg-backed queue, picker/drop, TXT/SRT/VTT | Unit/build | Real long-file priority and all codecs untested |
| Meetings | Mic/system/both, live text, speakers, notes, PDF | Partial | WASAPI loopback, segments, ONNX speaker embeddings, exports | Native smoke in prior pass | Multi-speaker quality and long meetings untested |
| Per-app Return | Optional | Implemented | Application profile | Unit/build | Real target apps manual |
| Clipboard | Configurable preservation | Implemented | Sequence-aware capture and restore | Unit | Rich target matrix manual |
| Model downloads | Resume and fallback | Implemented | Partial download verification, installed model fallback | Unit/build | Interrupted network test manual |
| Privacy | Local core, no telemetry | Implemented | Local models, optional history, temporary audio cleanup | Code audit | Optional external integrations are absent |
| Diagnostics/help | Local logs and support details | Partial | T0–T11, model and delivery logs, copy summary | Code audit | Dedicated Help page absent |
| Settings/tray/onboarding | Compact sidebar and utility surfaces | Partial | WPF sidebar, tray and onboarding | Build | Direct visual comparison not verified |
| App icon | macOS app icon | Windows original | SineFlow multi-size ICO and PNG assets | Existing assets inspected | Windows integration requires clean install checks |

## This pass

- Preserved the existing uncommitted insertion, waveform, selected-text, meeting and branding work.
- Fixed the automatic language setting. The native bridge asked whisper.cpp to detect the language only, and whisper.cpp returns right after the detection. With the default setting SineFlow recognised the language and then inserted nothing. The bridge now detects and transcribes in one call, and the native smoke test checks it.
- Added hardware detection and an automatic speech profile with the measured machine explained in plain words.
- Added voice activity detection for the leading and trailing silence of a recording, without touching a pause inside a sentence.
- Added the spoken formatting commands with examples in the settings. Casing runs without the writing model.
- Rewrote the icon pipeline around a vector master in `resources/SineFlow.svg`.
- Fixed a settings file that was reset in full when it contained a readable enum name.
- Changed the recording gate and recognition path to accept an installed compatible Whisper model when the preferred one is absent.
- Silent or accidental recordings now dismiss quietly.
- A failed explicit spoken translation or rewrite now leaves the raw transcript available to copy and inserts nothing. Ordinary cleanup may still fall back to the transcript.

## Verification and remaining gaps

After these edits, the full Release package command passed: 164/164 automated tests, Release build with zero warnings and errors, self-contained app publish, portable ZIP and installer. The analyzer verification also passed. Packages are `dist/SineFlow-Portable-x64.zip` and `dist/SineFlow-Setup-x64.exe`. The native smoke test passed against the real models, including the automatic language check. No direct WhisperBar binary comparison, physical microphone, browser/Office insertion, multi-monitor/DPI, multi-speaker meeting or clean-machine installer test is claimed. The current user request is larger than this pass; optional sounds, typed snippet expansion, a Help page, broader spoken intent, external STT engines and measured parity remain open.
