# Repository Guidelines

SonaBar is the product name and SilasNet is the publisher. Ship production behavior only: no fake models, progress, settings, or UI controls. Keep core workflows local first, private by default, telemetry-free, and compatible with existing settings whenever possible. Whisper inference uses direct `whisper.cpp` v1.9.4; Parakeet uses the official direct NeMo-Speech.cpp C ABI; Qwen ASR must remain in an authenticated Windows named-pipe sidecar with no network listener. Never commit downloaded model binaries. Add tests for critical behavior and never silently remove an existing feature.

## Project Structure & Module Organization

SonaBar is a native Windows application targeting .NET 10. Source projects live in `src/`: `SonaBar.App` contains the WPF shell and pages; `SonaBar.Core` contains contracts, settings, and text processing; the remaining projects isolate audio capture, transcription, enhancement, storage, Windows integration, models, meetings, and updates. Keep platform specific code in its owning module.

Tests are in `tests/SonaBar.Tests` (xUnit) and `tests/SonaBar.NativeSmoke` (real local inference smoke test). Installer code is in `installer/SonaBar.Setup`; icons and licenses are under `resources/`. Release outputs belong in `dist/` and temporary verification artifacts in `artifacts/`.

## Build, Test, and Development Commands

- `./build.ps1 -Configuration Release` restores, builds, tests, publishes, and packages the portable ZIP and installer.
- `./build.ps1 -Configuration Release -UpdateFeed https://updates.example.com/releases.json` also embeds the production update feed in the release package.
- `.tools/dotnet/dotnet test SonaBar.slnx` runs the xUnit suite after the local .NET 10 SDK is available.
- `.tools/dotnet/dotnet run --project tests/SonaBar.NativeSmoke -c Release -- third_party/whisper.cpp/samples/jfk.wav artifacts/native-smoke.json` verifies bundled native inference with reference audio.

## Coding Style & Naming Conventions

Use C# with nullable reference types enabled, four-space indentation, file scoped namespaces, and concise expression bodies where they remain readable. Use PascalCase for types and public members, camelCase for locals and private fields, and `Async` suffixes for asynchronous methods. Keep UI colors in shared WPF resources and use `DynamicResource` references so Light, Dark, and high contrast themes remain coherent.

## Testing Guidelines

Add focused xUnit coverage beside the affected behavior. Name tests as behavior statements, for example `NewestValidStableReleaseIsSelected`. Mock I/O and HTTP; do not require a microphone, network, or installed models for unit tests. Run the native smoke test only when changing model, audio, or packaging paths.

## Commit & Pull Request Guidelines

Use imperative, specific commit subjects, such as `Add verified background updater`. Describe user visible changes, test commands and outcomes, and any device checks still required. Include screenshots for WPF layout changes and never commit generated `dist/`, model files, credentials, or private update-feed URLs.

## Security & Release Configuration

Keep update feeds and downloads on HTTPS. Release manifests require a version, channel, installer URL, and SHA-256. Verify third-party license obligations before publishing, and preserve the app's local-only transcription and history defaults.
