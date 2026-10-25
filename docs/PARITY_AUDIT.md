# SonaBar / WhisperBar – Funktions- und Qualitätsaudit

Stand: 3. Oktober 2026. Quellen: [WhisperBar](https://whisperbar.pro/), [Changelog 1.20.1 / 1.19.0](https://whisperbar.pro/changelog.html), vorhandener SonaBar-Code und beigefügter Screenshot. Anbieterangaben sind keine unabhängigen Qualitätsmessungen. Kein WhisperBar-Code oder Asset wird übernommen.

## Ausgangszustand, vor diesem Pass

| Bereich | WhisperBar laut offizieller Dokumentation | SonaBar vor Änderungen | Entscheidung |
|---|---|---|---|
| Global Dictation | Hotkey-Diktat | Vorhanden, Fehler vor Aufnahmestart teilweise stumm | Fehlerfluss und schnelle Rückmeldung korrigieren |
| Live Recording Feedback | Kompakte Waveform | Echte WASAPI-Level, nichtaktivierende Pill | Behalten, Zugriff und Zustände verbessern |
| Direct Text Insertion | Accessibility-Integration | UIA für leere Felder, sonst häufig Clipboard | Native Edit-Controls und Unicode gezielt ergänzen |
| Enhancement | Lokal, kontextbezogen | Qwen lokal, konservative Schutzprüfungen | Behalten, Kontext/Füllwörter ergänzen |
| Self Corrections | Beworben | Nur enge numerische Uhrzeitmuster deterministisch | Namen, Wochentage, Mengen und Zahlwörter ergänzen |
| App-aware Formatting | Mail-/App-Kontext | Exe-Profile nur im Enhanced-Modus | Expliziten Kontext mit neutralem Fallback ergänzen |
| Selected Text Editing | Floating-Aktionen | Großer modaler Rewrite-Dialog, eigener Shortcut | Kompakte Auswahl-Session, auch über normalen Hotkey |
| Typed Rewrite Instruction | Vorhanden | Vorhanden, mehrere Klicks | Direkt im Floating-Fenster |
| Spoken Rewrite Instruction | Vorhanden | Aufnahme, Stop, Rewrite, Replace getrennt | Select → Speak → Replace integrieren |
| Custom Words | Vorhanden | Whisper-Prompt + Schreibmodell + explizite Korrekturen | Begriffe kontrolliert priorisieren, kein unsicheres Lernen |
| Translation | Auswahl/Diktat | Lokal plus gesprochene Befehle und Schutzprüfungen | Behalten, in Auswahlaktionen integrieren |
| Auto Language Detection | Vorhanden | Whisper multilingual/auto | Behalten; keine allgemeine Genauigkeitsbehauptung |
| Multiple Quality Modes | Mehrere Engines | Fast/Balanced/Maximum | Einfache Modi behalten; Engine-Wechsel nur mit Nachweis |
| Local Processing | Eingebaute Modelle lokal | Alle Kernfunktionen lokal, ohne Accounts | Behalten |
| Meeting Mode | Mikrofon/System/live | Separater Modus, 20-s-Abschnitte | Behalten, Langtext-Notizen und Cleanup verbessern |
| Speaker Separation | Beworben | ONNX-Embeddings/Clustering | Vorhanden, Qualität nicht mit WhisperBar verglichen |
| Batch File Transcription | Hintergrund-Queue | Queue blockiert normalen Controllerzustand | Diktate priorisieren, Queue im Hintergrund |
| Audio/Video Import | Dateien/Ordner | Picker, Drop, FFmpeg, TXT/SRT/VTT | Behalten; Queue-Zustände und Exportabsicherung |
| Snippets | Sprach-Expansion | Exakte Sprachtrigger mit Variablen | Behalten; kein globaler Tastenlogger |
| Keyboard-first Actions | Kontextmenü-Navigation | Großer Dialog/teilweise Kontextmenüs | Enter/Pfeile/Escape und Voice-Shortcut |
| Privacy | Lokale Kernfunktionen | History an, keine Frist; Audio temporär | Neue Installationen opt-in, Frist, Originalzugriff |

## UI-Befund

Der Screenshot priorisiert zwei Abläufe: Diktieren und Auswahl verändern. SonaBar besitzt die meisten Nebenfunktionen bereits, zeigt jedoch zu viele gleichrangige Settings und technische Modellinformationen. Die Änderung konzentriert sich auf eine kleine Auswahloberfläche, nutzerbezogene Einstellungen und kurzes Onboarding. Bestehende Themes, Qualitätsmodi, Casing und Sicherheitsprüfungen bleiben erhalten.

## Engine-Prüfung

[Sherpa-ONNX dokumentiert Parakeet v3](https://k2-fsa.github.io/sherpa/onnx/pretrained_models/offline-transducer/nemo-transducer-models.html) und [Qwen3-ASR](https://k2-fsa.github.io/sherpa/onnx/qwen3-asr/pretrained.html). Ein zusätzlicher ASR-Backendpfad benötigt mehrteilige Modelle, Decoder-/Token-Zuordnung, sichere Downloads sowie eigene Sprach-, Zeitstempel-, Speicher- und Windows-Tests. Die Existenz eines Windows-Runtimes beweist keinen Vorteil gegenüber der bereits getesteten Whisper-Pipeline. In diesem Pass wird daher kein ungeprüfter Engine-Wechsel zum Standard gemacht. Dieser Unterschied bleibt ausdrücklich offen; keine behauptete Engine- oder Genauigkeitsparität.

## Geplante Abnahme

Unit-/Integrationsfälle für Auswahl-Sicherheit, Selbstkorrekturen, Füllwörter, Kontext, History-Fristen, Queue-Priorisierung und Original/Undo. Reale lokale Inferenz, Release-Build, Analyzer, Installer, Portable und isolierter Start. Physische App-/Mikrofon-/Multi-Speaker-Tests werden getrennt von synthetischen Tests dokumentiert.

Der Abschlussvergleich wird nach der Implementierung unten ergänzt.

## Abschlussvergleich – tatsächlich implementierter Stand

| Bereich | WhisperBar | SonaBar nach diesem Pass | Nachweis / Einschränkung |
|---|---|---|---|
| Global Dictation | Ja | Ja | Nichtaktivierende Aufnahme-Pill, globaler Hotkey und Push-to-talk bleiben erhalten. |
| Live Recording Feedback | Ja | Ja | Echte WASAPI-Pegelwerte steuern die Waveform; Preparing, Listening, Processing, Done und Error sind getrennte Zustände. |
| Direkte Texteinfügung | Ja | Ja | UI Automation, native Windows Edit/RichEdit, Unicode und Clipboard-Fallback; Passwort-, Fokus- und Änderungsprüfungen verhindern blindes Einfügen. |
| Enhancement und Selbstkorrekturen | Ja | Ja | Lokaler Schreiber, konservative Faktenprüfung, deterministische Zeit-/Tag-/Mengen-/Namenskorrekturen. |
| App-Kontext | Ja | Ja | Explizite Exe-Profile plus vorsichtige Kontextregeln für Mail, Chat, Prompt, Code und Terminal; unklare Browser-Titel bleiben neutral. |
| Ausgewählten Text bearbeiten | Ja | Ja, verbessert | Ein normaler Diktat-Hotkey erkennt eine sicher les- und ersetzbare Auswahl und öffnet eine kleine Auswahl-Session. |
| Getippte / gesprochene Anweisung | Ja | Ja | Describe-a-change-Feld, Presets und Select → Speak → Replace; Ctrl+Enter, Escape, Tab/Enter und der gleiche Hotkey sind nutzbar. |
| Übersetzen, Prompt, Casing | Ja | Ja | In Auswahlaktionen und über gesprochene Endanweisungen; geschützte Namen, Zahlen und Zeiten werden vor der lokalen Transformation geprüft. |
| Custom Words | Ja | Ja | Benutzerbegriffe fließen begrenzt und priorisiert in den Whisper-Prompt sowie in die Schreibpipeline ein. |
| Automatische Sprache / Qualitätsmodi | Ja | Ja | Multilingual Whisper mit Auto-Sprache sowie Fast, Balanced und Maximum. Kein unbelegter Wechsel zu Qwen3-ASR/Parakeet. |
| Local-first / Datenhaltung | Ja | Ja | Audio ist temporär; neue Installationen speichern Transcript-History nicht automatisch, die Speicherfrist ist einstellbar. |
| Meeting / Speaker separation | Ja | Teilweise | Getrennter Meeting-Modus, Live-Abschnitte, Sprecher-Embeddings, Export und lokale Zusammenfassung existieren. Die Speaker-Qualität wurde nicht gegen WhisperBar gemessen. |
| Dateien und Queue | Ja | Ja, verbessert | Audio/Video, Ordner, Drag & Drop und TXT/SRT/VTT; die Queue wartet auf eine freie Transkriptions-Engine und wird von einer Diktatanfrage nicht als App-Sperre behandelt. |
| Snippets | Ja | Ja | Vorhandene gesprochene Snippets bleiben erhalten; kein invasiver globaler Tastenlogger. |

## Validierung

- Release Build: erfolgreich, 0 Warnungen, 0 Fehler.
- Tests: 85/85 erfolgreich, einschließlich der neuen Korrektur-, Füllwort-, Kontext-, Wörterbuch- und History-Tests.
- Paketierter App-Smoketest: Tray, Settings, Modellkatalog, vier erkannte Mikrofone und Idle-Zustand erfolgreich.
- Native CPU-Smoke-Test: lokale Whisper-Transkription, lokales Schreiben, gesprochene Übersetzung, Prompt-Transformation, Sprecher-Embeddings und PDF-Export erfolgreich.
- Nicht automatisiert: physisches Mikrofonsprechen, globaler Hotkey in jeder Drittanbieter-App, Auswahlersetzung in jeder Chromium-/Office-Variante sowie Mehrmonitor-/DPI-Varianten. Die Einfüge- und Auswahllogik bricht bei fehlender sicherer Verifikation ab, statt Text in ein unbekanntes Feld zu schreiben.
