# SonaBar UI and reliability pass — 3 October 2026

## Reference and design

The [WhisperBar product site](https://whisperbar.pro/) informed the visual direction: generous whitespace, strong headings, quiet green accents, clear primary actions and short explanations. SonaBar keeps its own identity, copy, icon and native Windows layout. No WhisperBar assets or source code were copied.

The WPF window now has a task-first sidebar, a clear page header, a focused home screen, consistent panels and controls, a visible status area and a persistent save action on settings pages. Rarely used pages sit under More. Light and dark themes use the same hierarchy. The selection editor has a scrollable, resizable layout; the separate writing assistant now has a clear input, instruction and result flow.

The title bar and installer now use the SonaBar waveform mark. `resources/SonaBar.svg` is the vector master; `resources/SonaBar-mark.svg` is the transparent variant. `scripts/update-sonabar-logo.ps1` renders the seven PNG sizes and the multi-size ICO from that vector, so the app, taskbar and installer icons stay in sync. The earlier raster `SonaBar-Original.png` is no longer part of the source tree.

## Bugs found and fixed

- Pausing the file queue more than once could replace the task being awaited and leave the queue stuck. Queue controls now reflect the actual state and a new queue starts unpaused.
- Export names could collide within one second and overwrite a previous file. Exports are written to a temporary file, then moved to a unique final name without overwriting existing content. A cancellation removes the draft.
- The meeting elapsed-time timer was created but never started. It now runs during capture. Start, stop and cancel buttons reflect the recording state, including after navigation and failure cleanup.
- Model progress bars appeared empty before download. They now appear only during a real download. Pause, delete and model-selection buttons follow availability.
- The standalone writing assistant offered “Replace selection” even when no selection was associated with it. It now offers Copy result after a result exists.
- A saved language code absent from the current language menu no longer makes the dictation/language page fail to build.

## Validation

The full test suite passes (105 tests). Release build reports no warnings or errors. Theme tests construct every page in light and dark mode, check contrast, and render screenshots in `artifacts/ui-review/`; the main pages are also rendered at 100%, 125%, 150% and 200% scale. Home, Dictation and Files were inspected at the 780 × 580 minimum window size. The file-export regression test checks that an existing export survives and a cancelled draft is removed.

The portable package passed its isolated smoke test: tray created, settings rendered, ten catalog models found, four microphones detected, settings valid and controller idle. Portable ZIP and installer hashes match `dist/SHA256SUMS.txt`. Device-level checks still require hands-on verification: global shortcuts and text insertion in third-party apps, real microphone and meeting capture, mixed monitor DPI, and clean-machine installer behavior. The release remains unsigned until SilasNet provides a signing certificate.

The native inference smoke test also passed with reference audio, local speech recognition, local writing and translation, speaker embeddings, and PDF export. Its report is `artifacts/native-smoke-redesign.json`.
