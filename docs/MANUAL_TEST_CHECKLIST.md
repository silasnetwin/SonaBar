# Windows release verification

Record Windows version, display scale, audio device, selected model, and the exact package hash for every run. A checkbox is a required manual check, not a claim that it has already passed.

## Installation and first run
- [ ] Install without elevation on a clean Windows 11 account without .NET, Python, Node, FFmpeg or VC runtime installed.
- [ ] Repeat on Windows 10 22H2 x64.
- [ ] Verify Start Menu and optional Desktop shortcuts, uninstall entry, launch, model download and offline launch.
- [ ] Cancel/resume model download; verify the partial file is retained and checksum is checked.
- [ ] Disconnect network and dictate after downloading models.
- [ ] Second instance activates the first. Closing settings leaves the tray running; Quit releases hotkeys.
- [ ] Upgrade while preserving model files and settings; uninstall while preserving user data.

## Application matrix
For **Notepad, Word, Chrome, Edge, Discord, VS Code, JetBrains IDE, and Windows Terminal**, check:
- [ ] Dictation inserts at the original caret.
- [ ] Smart formatting preserves meaning and language without requiring a second mode.
- [ ] Clipboard restore retains text, HTML, RTF, bitmap and file lists.
- [ ] A newer clipboard change during insertion is not overwritten.
- [ ] Selected-text editing changes only the intended selection; cancellation changes nothing.
- [ ] Password fields refuse recording/rewrite/insertion.
- [ ] Closed target preserves the transcription; elevated app reports blocked insertion.
- [ ] Per-app Enter is off by default. Enable only for the intended app, then verify it sends exactly once.
- [ ] Switch fields and apps while inference runs; ensure original target handling is safe.
- [ ] Roblox compatibility insertion: focus stays in the Roblox text field, no false failure is reported, and the result message is `Paste sent — check the app/target`. Record the speech-stop-to-paste latency.
- [ ] Start a dictation with the button in the SonaBar window while a browser or editor is behind it. The app must restore that external window, insert there, and must not target SonaBar's own button.

## Audio and shortcuts
- [ ] Toggle and push-to-talk, including a very short key press.
- [ ] The single Dictation shortcut can be customized; conflicts are reported and the prior binding remains usable.
- [ ] Sleep/resume, lock/unlock, default-device changes and microphone disconnection.
- [ ] Audio ducking restores the exact previous volume after success, error, cancel and next-launch recovery.
- [ ] Microphone test shows actual levels and releases the device when navigating away.
- [ ] Cancel during speech inference and local writing leaves no inserted text.

## UI and privacy
- [ ] Light, dark, system and high-contrast theme; keyboard-only navigation and screen reader labels.
- [ ] 100%, 125%, 150%, 175% and 200% DPI, mixed-DPI monitors, monitor removal.
- [ ] Overlay never takes focus; moving it persists a visible position after restart.
- [ ] Custom words, snippet import/export, tabs/newlines and clipboard/date/time variables.
- [ ] Typed snippets: off by default; enable in Snippets, then type a trigger plus space, Enter, Tab and punctuation in Notepad and a browser. Verify one replacement, no lost delimiter, longest-alternative matching and `{date}`, `{time}` and `{clipboard}` variables.
- [ ] Multi-word and punctuation triggers such as `be right back` and `e.g.` expand; clicking elsewhere in the same field before the delimiter does not replace earlier text.
- [ ] Typed snippets do not fire in SonaBar, password fields or elevated targets; disabling the option restores literal typing.
- [ ] History search/filter/delete and disabled-history behavior; clear history retains lifetime totals.
- [ ] Empty History, Statistics, Snippets and Applications show useful guidance without a blank list frame; History links to Privacy when storage is off.
- [ ] **About → Help** opens, internal links navigate, and Audio preview plays the finished tone only when feedback sounds are enabled.
- [ ] Inspect logs: no audio, transcript, selected text, snippet content or prompt content.

## Files and meetings
- [ ] Every supported media extension, corrupted/encrypted input, folders, queue pause/resume/cancel.
- [ ] Existing exports are not overwritten; TXT/SRT/VTT timestamps and UTF-8 text are valid.
- [ ] Microphone, system and combined meeting capture; recordings longer than one hour.
- [ ] Live transcript appears; input backlog and end-of-recording processing finish without losing sections.
- [ ] Speaker recognition with 2–4 speakers, overlapping speech and very short utterances; review all labels.
- [ ] Renaming updates all segments. Export TXT, Markdown and PDF with non-English names.
- [ ] Local summary does not invent decisions or action owners.

## Release checks
- [ ] Build/tests and native inference verification are green.
- [ ] Installer and ZIP SHA-256 match SHA256SUMS.txt.
- [ ] Review dependency licenses, supply corresponding LGPL sources with public downloads, sign with publisher certificate.
- [ ] Run the clean-machine matrix before representing the build as production-ready.
