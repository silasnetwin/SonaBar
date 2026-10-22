# Changelog

All notable changes to SineFlow are documented here. Versions follow
[Semantic Versioning](https://semver.org/); dates use ISO 8601.

## [0.1.3] – 2026-10-22

### Fixed

- **The window no longer freezes while a dictation starts.** Inspecting the focused field
  inside SineFlow itself made the automation thread and the interface thread wait for each
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
- One palette for the whole program in `src/Voxora.App/Palette.cs`; dark is the default for
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

- First internal build of the SineFlow shell: dictation, history, models, meeting capture,
  local rewriting, updater scaffolding.
