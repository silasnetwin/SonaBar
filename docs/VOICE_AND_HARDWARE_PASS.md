# Voice, hardware and branding pass - 22 October 2026 (updated 23 October 2026)

This pass added the parts of the dictation workflow that were still missing, and it fixed two
defects that would have made the product unusable. Every number below was measured on the
development machine, not estimated.

## The two defects that were found

### 1. The automatic language setting produced no text at all

`whisper.cpp` decides its language inside the same call, but it returns right after the
detection when it is asked to detect only:

```cpp
// third_party/whisper.cpp/src/whisper.cpp
if (params.detect_language) {
    return 0;
}
```

The native bridge set `detect_language` whenever no language was chosen, so the request
finished after the detection and `whisper_full_n_segments` stayed at zero. SonaBar's
default setting is `Language = "auto"`, so a fresh installation recognised the language and
then inserted nothing.

The bridge now asks for the automatic language with `language = "auto"` and
`detect_language = false`. whisper.cpp detects the language, replaces the parameter with the
detected code and continues into the transcription.

* Before: `{"language":"de","segments":[]}`
* After: `{"language":"de","segments":[{"startMs":0,"endMs":4820,"text":" Ich magte am Montag …"}]}`

`tests/SonaBar.NativeSmoke` now transcribes the reference audio twice, once with a fixed
language and once with `auto`, and fails if the automatic run loses the text or reports a
different language.

### 2. Writing a wave file with `WaveFileWriter.WriteSample(short)` corrupted the samples

In NAudio 2.2.1 a 16 bit wave file written through `WriteSample(short)` contained wrong
values: `2` was stored as `-2`, `1000` as `-1000`, `25647` as `7104` and `-5` as `-32763`.
The trimmed dictation would have been played back as noise.

The recorder itself was never affected, because it writes the raw device bytes and merges the
result through `WaveFileWriter.CreateWaveFile16`. The new voice activity pass now uses the
same `CreateWaveFile16` path, and `tests/SonaBar.Tests/HardwareAndVoiceTests.cs` compares
every sample of a trimmed file with the source recording.

## What was added

### Automatic language, hardware and model profile

`SonaBar.Core/HardwareProfile.cs` holds the measured machine and the rules that act on it.
`SonaBar.WindowsIntegration/HardwareDetector.cs` reads it with real system calls: total and
free memory through `GlobalMemoryStatusEx`, the graphics adapter and its dedicated memory
through DXGI, the acceleration runtimes by loading `vulkan-1.dll` and `nvcuda.dll`, and the
free disk space of the data drive.

On the development machine:

| Value | Measured |
|---|---|
| Operating system | Windows 11, build 26200 |
| Logical processors | 24 |
| Memory | 15.8 GB total |
| Graphics adapter | NVIDIA GeForce RTX 4060, 7.8 GB dedicated memory |
| Acceleration runtimes | Vulkan and CUDA both installed |
| Free disk space | 136.8 GB |
| Recommended profile | Balanced (Whisper `small`) |

The recommendation uses the **total** memory, not the free memory, because the free amount
changes from minute to minute and the profile would otherwise flip between runs. Free memory
only downgrades the profile when the machine is under real pressure. The strongest profile is
never chosen automatically, because it measured several times slower on the processor.

Existing installations keep the profile they had: the settings migration sets
`AutoQuality = false` for a settings file that does not contain the flag. New installations
start with the automatic choice.

### Voice activity detection

`SonaBar.Core/SilenceTrim.cs` removes the leading and trailing silence of a finished
recording. Only the outer edges are touched, a pause inside a sentence stays exactly where it
was, and a recording is never shortened below 30 % of its length or by more than 90 %. The
decision happens after the user stops talking, so a short silence never ends a recording.

Measured on the reference audio with two seconds of dead air in front and behind:

| File | Length | Transcript |
|---|---|---|
| Padded | 15.00 s | `- And so, my fellow Americans, …` |
| Trimmed by voice activity | 11.30 s | `And so, my fellow Americans, …` |

The recognition time did not change: whisper.cpp pads every request to a fixed thirty second
window, so the encoder does the same work either way. The trim removes a hallucinated
fragment at the start of the padded recording and shortens the temporary file. It is kept
because it makes the result predictable, not because it makes the app faster.

### Spoken commands

The settings now offer the commands the product promised. All of them are only recognised as
a trailing instruction with an explicit verb or the word "alles", so a sentence that merely
mentions a language or a formatting term keeps its content.

| Spoken | Effect | Needs the writing model |
|---|---|---|
| "Übersetze den Text auf Englisch." | Translates | yes |
| "Mach daraus eine professionelle E-Mail." | Professional email | yes |
| "Formuliere das freundlicher." | Friendlier tone | yes |
| "Korrigiere nur die Rechtschreibung." | Spelling only | yes |
| "Entferne die Füllwörter." | Removes fillers | no, has a deterministic fallback |
| "Mach daraus Stichpunkte." | Bullet list | yes |
| "Fasse das kurz zusammen." | Summary | yes |
| "Alles lowercase." | Lower case | no, runs without a model |
| "Alles in Großbuchstaben." | Upper case | no, runs without a model |

A summary may leave details out but never adds one and never drops a negation. Every other
edit still has to keep every number, name and time.

### The brand mark

`resources/SonaBar.svg` is now the master of the icon set, together with
`resources/SonaBar-mark.svg` for the transparent version. Both are hand written vector
graphics with a `0 0 1024 1024` view box. The master uses seven rounded white waveform
bars on a near-black-to-plum gradient; the mark keeps the bars transparent for use on dark
surfaces. `scripts/update-sonabar-logo.ps1` validates the markup, renders the vector with a
headless Chromium browser and writes the seven PNG sizes and the multi size `.ico` that the
application, the task bar and the installer use. The committed PNG and ICO files are
generated from the vector, so the shapes can never drift apart.

The application ships both vector files next to the executable.

## Measurements

All values were measured on the development machine with 24 logical processors, the CPU
backend and the bundled `whisper.cpp` v1.9.4. The audio is the public reference recording
from `whisper.cpp` (`samples/jfk.wav`, 11 seconds).

| Model | Cold load | Warm recognition, 11 s clip |
|---|---|---|
| `base` (Fast) | 213 ms | 390 – 440 ms |
| `small` (Balanced) | 359 – 626 ms | 1293 ms |

The automatic language setting costs one extra encoder pass, because whisper.cpp encodes the
first thirty seconds again for the detection:

| Setting | Warm recognition, 11 s clip |
|---|---|
| Language fixed to English | 1293 ms |
| Language automatic | 2204 ms |

The profile is therefore the real lever for the goal of a result within about two seconds.
With the Fast profile a short dictation is ready in well under a second, with the Balanced
profile an eleven second dictation needs about one and a half seconds.

German was checked with locally synthesised speech from the Windows speech engine, because
the repository only contains English reference audio. The automatic setting named German in
all eleven files, and the numbers and dates came through correctly, for example
`Die Rechnung betragete 149,95 Euro und kommt am 3. Mai 2026.` The wording itself is less
accurate than with a human voice, because a synthetic voice is not what the model was
trained on. A dictation on a real microphone is still part of the manual test checklist.

## Verification

| Check | Result |
|---|---|
| `dotnet build SonaBar.slnx -c Release` | passed, 0 warnings, 0 errors |
| `dotnet test SonaBar.slnx -c Release` | passed, 193 of 193 |
| `tests/SonaBar.NativeSmoke` | passed: speech, automatic language, writing model, translation into English, French and Chinese, German prompt, speaker embeddings, PDF export |
| Application smoke test | passed: tray, settings, 12 catalogue models, 4 microphones, all 19 pages built and laid out without an error, then clean shutdown with exit code 0 |

## Open items

* The processor topology API of Windows reports an inconsistent layout on this machine and
  faults on the extended variant, so SonaBar reports the logical processor count and not
  the number of physical cores.
* The language of a dictation is detected on every request. Remembering the detected
  language would remove the extra encoder pass, but it was left out because a wrong guess
  would silently produce a wrong transcript.
* German was verified with synthetic speech only.
* Parakeet and Qwen are now packaged and verified on CPU. The remaining manual device,
  browser, Office, DPI and clean-machine checks are tracked in
  `docs/MANUAL_TEST_CHECKLIST.md`.
