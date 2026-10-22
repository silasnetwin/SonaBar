# Startup, window frame and colour system (2026-10-22)

This pass fixed the two reported start-up failures, moved the window to the native Windows
frame and replaced the scattered colours with one palette. Every statement below was measured
on this machine with the release build and the user's real profile.

## 1. The endless "Getting ready" state

### Reproduced cause

`diagnostics.log` stopped after `T0` and `T1` of a dictation. The missing `capture_target` and
`T2` lines proved that the UI thread never finished awaiting the target capture:
`TextInsertionService.CaptureTarget()` called `AutomationElement.FocusedElement` and walked the
target application's UI Automation tree synchronously. When the focused application was busy,
that call blocked a thread-pool thread, the `await` continuation never resumed on the UI
thread, and the window stopped painting - a white, non-responding frame that also swallowed
the readiness message.

### Fix

- `src/Voxora.WindowsIntegration/AutomationProbe.cs` owns one dedicated STA thread with its own
  message pump. `TryRun<T>` executes the capture there with a hard timeout and abandons the
  worker if the target refuses to answer.
- **A field inside SineFlow itself is inspected on the interface thread.** When the focused
  window belongs to the running program, Windows describes it from this same process: the
  helper thread waits for the interface thread while the interface thread waits for the helper
  thread, and the window stops painting. `TextInsertionService.IsOwnWindow` detects that case and
  runs the inspection through `Dispatcher.Invoke`; every other application still goes through the
  probe with its timeout. Reproduced by pressing the shortcut with the SineFlow window focused:
  before the fix the program stopped answering for the whole recording, afterwards the capture
  completes in ~30 ms and the window keeps responding.
- `TextInsertionService` uses the probe with `CaptureTimeout = 900 ms` and reports a plain
  message instead of waiting forever. The service is now `IDisposable`.
- `AppController` bounds the recording start (`WaitAsync(6 s)`) so a stalled audio device cannot
  keep the machine in `Starting`.

## 2. The long start

### Reproduced cause

`App.OnStartup` called `controller.PrepareQualityAsync()`. On any onboarded profile without the
model for the selected quality this silently started a ~465 MB download (`small.bin.partial`
appeared in the profile). The window was only shown after that work, so the first start after
an update looked like a hang. The download was not visible anywhere in the UI.

### Fix

- The window is shown first, heavy work runs afterwards: `data_ready` -> `services_ready` ->
  `window_shown` -> `InitializeAsync()` (models, engines) -> `ready`.
- A missing model is now an explicit state (`ReadinessStage.NeedsModel`) with the label
  "No speech model yet" and the detail "Download a model." The Models page offers the download
  with its own progress, and nothing downloads by itself.
- Stage timings are written to `%LOCALAPPDATA%\SNine Studios\SineFlow\Logs\diagnostics.log` as
  `event=Startup stage=<stage> elapsed_ms=<n>`.

### Measured (release build, 1240x820, models present)

| Stage | Before | After |
| --- | --- | --- |
| process start | 0 ms | 0 ms |
| window answers Window messages | not before the download finished | ~700-930 ms |
| `ready` (engine loaded) | after ~60 s download or never | ~900-1130 ms |

`artifacts/verify-ui.ps1` measures the same numbers from the outside (window answering after
~700-930 ms, ready after ~900-1130 ms) on repeated runs.

## 3. Readiness states

`src/Voxora.Core/Readiness.cs` defines `ReadinessStage` (`Starting`, `Loading`, `NeedsModel`,
`Ready`, `Recording`, `Transcribing`, `ApplyingCommand`, `Inserting`, `Error`) with a label and
an optional explanation. `AppController.Readiness` publishes it; the sidebar pill and the status
line bind to it. The controller raises the change for `StageLabel` as well, so the footer text
follows the state.

A failing engine never blocks the settings: the state shows the error text and the navigation
stays usable.

## 4. Native window frame

`ThemedWindow.UseNativeChrome()` keeps `WindowStyle.SingleBorderWindow`, so Windows draws
minimise, maximise, close, resizing and the correct DPI behaviour. `WindowIntegration.ApplyTitleBar`
colours the caption and the border through DWM (`DWMWA_USE_IMMERSIVE_DARK_MODE`,
`DWMWA_CAPTION_COLOR`, `DWMWA_BORDER_COLOR`, `DWMWA_WINDOW_CORNER_PREFERENCE`) whenever the
theme changes. The custom macOS-style traffic lights (`BuildChrome`) were deleted; the window
title is a plain "SineFlow".

## 5. Overlay corners

The overlay used `AllowsTransparency = true`, which makes a layered window and lets some drivers
paint the rounded corners black. `RecordingOverlay` is now an opaque, topmost window without a
frame and is clipped to a rounded shape with `CreateRoundRectRgn` + `SetWindowRgn`
(`WindowIntegration.ApplyRoundedRegion`), reapplied on size and DPI changes. The silhouette is
clean in every state and the desktop shows through the corners.

## 6. Colour system

`src/Voxora.App/Palette.cs` is the only place with colour values. `Palette.Dark` and
`Palette.Light` define the same keys; `PaletteService.Apply` writes them into the application
resources and hands the window colour to the title bar. `Themes/Controls.xaml` contains geometry
and styles only and refers to colours with `DynamicResource`. New installs start in the dark
palette; an existing choice (`Light`, `Dark`, `System`) is preserved. High contrast is applied on
top of either palette.

Dark values: canvas `#0E1116`, sidebar `#121519`, elevated card `#171B21`, line `#2A2F37`,
primary text `#EDF1F7`, secondary text `#A4ADBA`, accent `#5C93F0`, accent text on light `#8FB9FF`.
Green is gone from surfaces; the only strong colour is the accent.

## 7. Overview and settings layout

- The duplicate page headline is hidden on Overview; the hero is a normal card, not a banner.
- The three entry points share the content width in equal columns.
- Model rows are compact: name and metadata on the left, actions on the right.
- Settings destinations fill two equal columns.

## 8. Tests

`tests/Voxora.Tests/StartupAndThemeTests.cs` covers the readiness states, the capture timeout,
the probe timeout and recovery, the palette contrast and key coverage, and the overlay window
properties. `ThemeTests` now asserts the native frame instead of the old borderless window and
the opaque overlay instead of a layered one.

Result: 183 of 183 tests pass in Release.
