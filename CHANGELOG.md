# Changelog

All notable changes to SonaBar are documented here. Versions follow
[Semantic Versioning](https://semver.org/); dates use ISO 8601.

## [Unreleased]

### Added

- **In-app Help page.** **About → Help** now covers the dictation workflow,
  delivery troubleshooting for elevated and game windows, privacy, diagnostics
  and the free-software policy. The page is built by the page smoke test.
- **Optional feedback sounds.** Original generated WAV cues provide **Off**,
  **Minimal** and **Standard** modes. Minimal plays the finished tone only;
  Standard adds the start and error tones. Playback is best-effort and never
  turns an unavailable output device into a dictation failure. The Audio page
  can preview the finished tone and disables preview while sounds are off.
- **Opt-in typed snippet expansion.** The same snippets used after dictation can
  expand while typing in another application. A trigger followed by space,
  Enter, Tab or punctuation is replaced with the snippet content, including
  `{date}`, `{time}` and `{clipboard}` variables. Triggers may contain spaces
  and punctuation, so `be right back` and `e.g.` work. Matching is
  case-insensitive, requires a word boundary and prefers the longest
  alternative. The setting is off by default, ignores injected input, skips
  SonaBar's own window and password fields, clears the typed buffer on a mouse
  click, and warns that games or elevated apps may reject injected text.
- **Parakeet TDT 0.6B v3 now runs from the release package.** The NeMo-Speech.cpp
  runtime and its required ggml, Abseil and protobuf DLLs are bundled, so the
  verified 25-language European model can run on CPU without a separate C++
  toolchain. The native smoke test transcribes the 11-second JFK reference clip
  in about 1.1 seconds on CPU.
- **Qwen3-ASR 1.7B is available as an optional local speech engine.** The app
  downloads a pinned seven-file model bundle, verifies every file, and runs it in
  an isolated Python sidecar over a random per-launch Windows named pipe with
  mutual HMAC authentication. It supports 30 languages plus automatic detection,
  uses no network listener, and maps SonaBar's `tl` code to the model's `fil`
  code. Native smoke and lifecycle probes now pass on CPU with the packaged
  runtime, including automatic detection, cancellation and sidecar restart.
- **The Qwen runtime ships with the app.** `build.ps1` packages a pinned
  python-build-standalone CPython 3.12.15 runtime with PyTorch 2.14.1 CPU,
  Transformers 5.20.2, tokenizers, NumPy and SoundFile. The model weights remain
  a separate verified download and are never committed.
- Added per-file verification and resumable directory bundles to the model
  manager, with aggregate progress and a bundle fingerprint that is invalidated
  when any file changes.
- The language picker now follows the selected speech engine. Qwen shows its 30
  languages plus automatic detection, Parakeet shows its 25 European languages,
  and an older unsupported language setting falls back to automatic detection
  instead of failing a dictation.
- Added upstream SentencePiece, Abseil, protobuf, CPython, PyTorch, Transformers
  and Qwen dependency license texts to the packaged third-party notices.

### Fixed

- **Empty pages no longer show blank frames.** History, Statistics, Snippets and
  Applications replace empty list boxes with short guidance. History offers a
  direct link to Privacy when storage is off.
- **Opaque game windows can receive dictation.** Target capture no longer
  requires a UI Automation element. The explicit **Compatibility** profile now
  pastes into the verified foreground window for games such as Roblox that
  expose no text provider, instead of stopping with "Windows could not inspect
  this field safely." Selection editing still requires an inspectable field.
- **The Start button now targets the app behind SonaBar.** SonaBar remembers the
  last external foreground window and restores it before capture when dictation
  or selection editing starts from its own window. Starting a dictation from the
  app no longer targets its own button and reports a false insertion failure.
- **Chromium keeps its caret after the Start button.** Reactivating a browser
  contenteditable can leave UI Automation focus on the editor while the DOM
  focus falls back to the page body. When dictation starts from SonaBar, the
  insertion service now clicks the stored caret (or the middle of an empty
  field) and verifies foreground and keyboard focus again immediately before
  SendInput, so Chrome and Claude receive the text instead of a SonaBar field.
- **Browser page selections are not field selections.** Chromium can expose the
  page-level selection through a contenteditable's TextPattern. SonaBar now
  requires the selected text to occur inside the field's own content, so
  selecting page text no longer opens the selection editor instead of starting
  dictation.
- **Browser editors receive the focused composer.** Target discovery now searches
  the foreground window's own UI Automation tree for the focused `Edit` or
  `Document` instead of trusting a stale global focus element. Claude and other
  Chromium contenteditables are found reliably, and a delayed UI Automation value
  update no longer causes a duplicate SendInput insertion.
- **Focus restore survives Windows' foreground lock.** Before delivery SonaBar
  attaches to the foreground thread and retries activation, with a window-switch
  fallback. A browser or game behind SonaBar or its overlay no longer fails with
  "Windows prevented restoring focus."
- **Tray status updates no longer throw.** The tooltip now measures the full
  `SonaBar · ` prefix before applying the 63-character Windows limit. The old
  hard-coded prefix length was one character too long, so changing the status
  could raise `ArgumentOutOfRangeException` and show a spurious error notice.
- **Autostart is registered on first launch.** The per-user `Run` value is now
  written during initialization as well as when settings are saved, so a fresh
  install starts with the tray entry when "Launch at login" is enabled. Registry
  errors are logged without preventing the app from starting.
- **SonaBar now exits cleanly after the application smoke test.** The service
  container and the speech router both disposed the Qwen engine; the second
  disposal waited on an already disposed semaphore and could leave a hidden
  process behind. Engine disposal is now idempotent, the smoke test no longer
  opens a modal error box, and shutdown always reaches `Shutdown()` even if a
  cleanup step fails.

### Changed

- **Workspace polish.** The General tab is a ten-destination hub for the main
  workflows. Page headings and subtitles are clearer, the Snippets and
  Applications subnav labels match their pages, and About shows the factual
  build line `Version 0.1.3 · Windows x64 · unsigned`.
- **WhisperBar-inspired interface.** The shell now uses a light palette by
  default, a custom 44 px title bar with Windows minimise, maximise/restore and
  close controls, segmented top tabs, white cards, green positive actions and a
  blue record control. SonaBar is an independent SilasNet application and is
  not affiliated with WhisperBar. Native resize, snap and per-monitor DPI are
  preserved.
- **Dictation delivery is verified end to end.** Native edit, UI Automation,
  Unicode and clipboard paths now confirm the text in the target; browser
  contenteditables are inspected through `TextPattern`, partial insertions are
  detected, and opaque editable targets no longer report a false failure.
- **Game-window compatibility.** Applications such as Roblox that expose no
  Windows text provider use the new **Compatibility** insertion profile. The
  paste is sent to the focused game window and reported as “Paste sent — check
  the app/target” instead of a false “could not insert” error; Windows still
  offers no read-back there, so the game itself must show the result.
- **Two-second dictation.** Balanced (`small`) is the default profile for new
  installations and the documented everyday choice; Maximum remains available
  for difficult audio and file transcription. The footer reports the measured
  time from the end of speech to confirmed insertion.
- **CI build fix.** The Parakeet native build pins the release SentencePiece
  archive so the Windows Release link no longer mixes the debug and release C
  runtimes.

## [0.1.3] - 2026-10-22

### Fixed

- **The window no longer freezes while a dictation starts.** Inspecting the focused field
  inside SonaBar itself made the automation thread and the interface thread wait for each
  other. A field that belongs to the program is now inspected on the interface thread;
  every other application is inspected on the dedicated automation thread with a hard
  timeout.
- **The endless "Getting ready" state is gone.** The start-up no longer begins a silent
  ~465 MB model download before the window appears. A missing speech model is an explicit
  state with a link to the Models page.
- **The window appears first.** Engine and model work now runs after the window is shown;
  start-up stages are written to the diagnostic log.
- **Recording start is bounded.** A stalled audio device can no longer keep the program in
  the starting state.
- **Rounded overlay corners on every driver.** The recording overlay is an opaque window
  clipped to a rounded region instead of a layered window that painted black corners.

### Changed

- Native Windows title bar with a themed caption, border and window corner preference;
  the custom traffic-light controls were removed.
- One palette for the whole program in `src/SonaBar.App/Palette.cs`; dark is the default for
  new installations, and an existing choice is preserved.
- Blue brand mark instead of the previous green gradients.
- Overview, Models and Settings pages compacted: no duplicate headline, equal-width entry
  cards, one-row model entries, two-column settings destinations.
- The sidebar status pill and the footer now follow the readiness state, so "Ready to
  listen" can never be shown while no model is installed.
- The window never opens larger than the screen.

### Added

- Readiness model (`ReadinessStage`) with explicit labels and explanations.
- 20 regression tests covering readiness, the inspection timeout, own-window detection,
  the palette and the overlay surface.
- Diagnostic stage timings (`event=Startup stage=… elapsed_ms=…`).

## [0.1.2] – 2026-10-05

### Added

- Hardware detection with an automatic speech profile.
- Voice activity detection that trims leading and trailing silence.
- Spoken formatting commands and trailing translation.
- Vector-based icon pipeline for every Windows icon size.

### Fixed

- Automatic language detection returned before the transcription, producing no text.
- A readable enum name in `settings.json` no longer resets the whole configuration.

## [0.1.0] – 2026-09-28

- First internal build of the SonaBar shell: dictation, history, models, meeting capture,
  local rewriting, updater scaffolding.
