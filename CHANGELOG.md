# Changelog

All notable changes to SonaBar are documented here. Versions follow
[Semantic Versioning](https://semver.org/); dates use ISO 8601.

## [Unreleased]

### Added

- **One compact everyday window.** SonaBar now opens in a 430 x 660
  `MainWindow` with the current state, mode, shortcut, model, effective
  processing path, the last transcription, copy and a single start/stop action.
  The old 1080 x 780 workspace no longer exists. All 20 pages are reached from
  the **Go to** selector, and the header gear opens compact settings. Closing the
  window keeps SonaBar in the tray; **Tray > Tools** provides Home, History,
  Transcribe File, Meeting Mode and All settings shortcuts.
- **Compact settings.** Mode, shortcut capture, minimum push-to-talk hold,
  installed model or automatic profile, language, processing device and Whisper
  CPU threads are editable in `SettingsDialog` without leaving the compact
  window. Writing models, custom words, snippets, applications, history and
  diagnostics stay available on their pages through **Go to**.
- **Compact-only focus and layout polish.** The 430 x 660 window fits without a
  scrollbar, the Home page uses the header gear instead of a second Settings
  button, and the read-only last transcription no longer takes a tab stop.
  WPF's dotted focus rectangle is replaced by a solid accent border or a soft
  icon highlight, so keyboard focus stays visible without the broken-looking
  dashed box.
- **Custom compact chrome.** The native caption buttons are replaced by vector
  pin, settings, minimize and close controls beside a circular SonaBar mark, a
  privacy pill and a manual title-bar drag surface. The pin toggles always-on-top
  for the current session only. Windows 11 supplies rounded DWM corners; Windows
  10 may show square corners.
- **Selected-text editing stays on the original passage.** The captured passage
  is carried through focus changes and Chromium accessibility-node replacement,
  restored immediately before input, and verified again. If it cannot be
  restored unambiguously, SonaBar refuses the edit instead of appending at a
  lost caret or duplicating text.
- **Translation retries with a preservation contract.** A response that loses
  protected names or numbers, or that mostly repeats the source wording, is
  retried once with explicit preservation instructions and validated again.
  A second unchanged response is rejected instead of being reported as a
  successful translation.
- **Honest processing controls.** `ProcessingDevice` replaces the old free-form
  backend label, with Automatic, CPU and GPU choices. Whisper accepts an explicit
  CPU thread count, falls back to CPU when GPU loading fails, and reports the
  effective backend in the compact window and diagnostics. Parakeet and Qwen
  remain CPU-only and show that their runtimes manage their own threads.
- **Minimum push-to-talk hold.** An optional 0.5-2.0 second threshold discards
  accidental short presses instead of sending them to the speech model.
- **Parakeet speech-context plumbing.** Installed custom words are passed to
  NeMo-Speech.cpp as one native speech-context request, capped at 64
  deduplicated phrases. The current TDT head logs that it ignores speech
  contexts without a Flashlight decoder, so SonaBar does not claim a Parakeet
  accuracy gain; Whisper and local writing still use the words.
- **Settings schema 5.** Existing `Backend` values migrate to `ProcessingDevice`;
  `CpuThreads` and `MinHoldMs` are clamped when a settings file is hand-edited.

- **In-app Help page.** **About → Help** now covers the dictation workflow,
  delivery troubleshooting for elevated and game windows, privacy, diagnostics
  and the free-software policy. The page is built by the page smoke test.
- **Source code link in About.** The About page links directly to the public
  GitHub repository. The README is refreshed for the public repository with the
  current download, privacy, build and verification information.
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
  click or line break, and warns that games or elevated apps may reject injected
  text.
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

- **Starting SonaBar again returns the compact window.** A second launch now
  signals the running instance and exits silently instead of showing a blocking
  "already running" message box. The warning remains only when the existing
  process is a different build path, and the decision is covered by focused
  tests. The compact `MainWindow` is the only window the second launch restores.
- **Short push-to-talk presses stay short.** The key-up timestamp is captured
  immediately, so a slow microphone or model start cannot turn an accidental tap
  into a valid hold. The compact button also stops and inserts while recording
  instead of cancelling the dictation.
- **Onboarding now returns to the compact window.** Finishing the Welcome flow
  shows the everyday `MainWindow` instead of leaving SonaBar tray-only until the
  next launch.
- **GPU fallback reaches the user.** The router now forwards Whisper's CPU
  fallback notice, and the effective processing summary changes to
  `GPU unavailable (CPU fallback)`. Changing the processing device forces the
  next model load to use the new choice instead of reusing the old backend.
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
- **Rich Chromium/Electron composers receive one atomic paste.** Discord/Slate,
  Slack, Teams, VS Code/Cursor/OpenCode, ChatGPT, Notion, Obsidian, Element,
  Signal, Telegram, WhatsApp and contenteditable/CodeMirror/Monaco/ProseMirror
  editors now skip UI Automation `SetValue` and long SendInput batches and paste
  once through the clipboard. Long fallback text is sent in small Unicode
  chunks, a partial insertion into an empty field is replaced with select-all +
  paste, and a paste that the app normalizes is reported as
  `Paste sent — check the app/target` instead of a false failure. The Edge
  `slateTextArea` probe verified the full paste and the normalized
  `changed_assumed` path.
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
  attaches to both the current foreground thread and the target thread, retries
  activation, and keeps a window-switch fallback. It no longer unmaximizes a
  target that was intentionally maximized. A browser, editor or game behind
  SonaBar or its overlay no longer fails with "Windows prevented restoring focus."
- **Slow transcriptions keep a usable insertion target.** A UI Automation
  selection captured before a long transcription can expire while the target
  field is still healthy. SonaBar now logs the stale selection, reacquires the
  focused editable node, retries `SetFocus` and foreground activation, and
  continues at the current caret instead of failing the whole dictation.
  Replacement paths still validate the original passage and refuse an unsafe
  edit. New diagnostics distinguish `selection_restore_failed`,
  `selection_reacquired` and `final_after_restore;attempt=...`.
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

- **Page polish.** General is a ten-destination hub for the main workflows.
  Page headings and subtitles are clearer, the Snippets and Applications labels
  match their pages, and About shows the factual build line
  `Version 0.1.3 · Windows x64 · unsigned`.
- **WhisperBar-inspired interface.** The shell now uses a light palette by
  default, native Windows chrome with minimize and close, the **Go to** page
  selector, white cards, green positive actions and a blue record control.
  SonaBar is an independent SilasNet application and is not affiliated with
  WhisperBar. The window is fixed at a compact size and keeps per-monitor DPI.
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
