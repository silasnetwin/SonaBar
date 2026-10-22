# Contributing

Thanks for helping make SineFlow better. This repository is private, so all work happens
through the issue tracker and pull requests here.

## Before you start

- Read `AGENTS.md`, `docs/IMPLEMENTATION_STATUS.md` and `docs/MANUAL_TEST_CHECKLIST.md`.
- Keep the product promises intact: local first, private by default, no telemetry, no
  hidden downloads, no cloud fallback.
- Never commit downloaded models, credentials, signing material, build output or personal
  data. `dist/`, `artifacts/` and `.tools/` are ignored on purpose.

## Set up

```powershell
git clone https://github.com/silasnetwin/SineFlow.git
cd SineFlow
git submodule update --init --recursive
./build.ps1 -Configuration Release
```

A full build needs the .NET 10 SDK plus Visual Studio 2022 with the C++ desktop workload
and CMake for `whisper.cpp` and `llama.cpp`.

## Making a change

1. Describe the user-visible problem in an issue first. A reproduction beats a guess.
2. Add a focused xUnit test next to the affected behaviour in `tests/Voxora.Tests`.
3. Keep changes inside the owning module; platform code stays in `Voxora.WindowsIntegration`.
4. Run `dotnet test Voxora.slnx -c Release` before opening the pull request.
5. If you touched audio, models or packaging, run the native smoke test as well.

## Code style

- C# with nullable reference types, file-scoped namespaces, four spaces.
- `PascalCase` for types and public members, `camelCase` for locals and private fields,
  `Async` suffix for asynchronous methods.
- Colours only from `src/Voxora.App/Palette.cs`; styles use `DynamicResource`, never a
  hard-coded brush.
- User-facing text is plain English: short sentences, no jargon, no exclamation marks.

## Pull requests

A good pull request explains:

- what changed for the user,
- which test commands you ran and what they reported,
- which device or application checks are still open,
- screenshots for visible layout changes, in both dark and light.

Please do not include unrelated refactors, generated files or changes to the licensing
metadata.
