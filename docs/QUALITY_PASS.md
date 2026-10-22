# Qualitätsüberarbeitung – 2. Oktober 2026

Die bestehende .NET/WPF-Architektur wurde beibehalten. Änderungen liegen in den vorhandenen Audio-, Transkriptions-, Modell-, Windows- und UI-Komponenten; Textverarbeitung und Theme-Ressourcen sind zentral ergänzt.

## Behobene Ursachen

- Abgeleitete WPF-Fenster erhielten den impliziten Window-Style nicht. Dadurch konnten helle Texte mit einem hellen Standardhintergrund kombiniert werden. Ein expliziter gemeinsamer Fensterstil, dynamische Theme-Ressourcen und passende Control-Templates beheben die Vererbung. Button-Inhalte erben nun die jeweils richtige Textfarbe.
- Der Aufnahmestart spielte selbst einen Windows-Systemsound ab. Dieser Aufruf ist entfernt; Windows-Soundeinstellungen werden nicht verändert.
- Das bisherige Resampling dämpfte im 5-kHz-Test die Sprachband-Energie. Sinc-Resampling erhält diese Frequenzen besser; keine aggressive Rauschunterdrückung wurde hinzugefügt.
- Aufnahmeinitialisierung läuft außerhalb des UI-Threads; die Pill erscheint vorher. Aufnahmestartfehler werden nicht mehr doppelt aufgeräumt. Stop-Aufrufe teilen dieselbe Abschluss-Task.
- Fehler bei der Texteingabe wurden von einem anschließenden Done-Hinweis überschrieben. Erfolgs- und Fehleranzeige sind getrennt; das Abschlussfeedback überlebt den Wechsel zurück zu Idle.
- Modelle werden wiederverwendet und beim Modellwechsel korrekt neu geladen. Download-Fortschritt ist gedrosselt; festgefahrene Downloads haben ein Timeout und bleiben fortsetzbar.
- Mikrofontest- und Rewrite-Aufnahme räumen Listener und Geräte auch bei Fehlern auf. Einstellungen mit strukturell falschen JSON-Werten werden gesichert und wiederhergestellt.
- Einfügen prüft das ursprüngliche Textfeld erneut. Undo/Rewrite verwenden nur eine verifizierbare Textstelle; Verlaufseinträge und Dateiergebnisse können keine alte Einfügestelle versehentlich weiterverwenden.
- Overlay-Positionierung nutzt Monitor-Arbeitsflächen, physische Koordinaten und Monitor-DPI, einschließlich negativer Bildschirmkoordinaten.

## Bedienung und Textpipeline

- Fast, Balanced und Maximum; Balanced ist Standard. Intern Base, Small und Large V3 Turbo. Bewusste erweiterte Modellauswahl bleibt verfügbar.
- Persönliche Begriffe sowie explizite „erkannt als → gemeint ist“-Korrekturen.
- Automatische Zeichensetzung und konservative Korrektur; eindeutige gesprochene Uhrzeitkorrekturen werden vor der Modellverarbeitung aufgelöst.
- Gesprochene abschließende Übersetzungsbefehle und „Mach mir daraus einen Prompt für ChatGPT“.
- Empfohlenes lokales Schreibmodell Qwen3 4B, optional kompakteres Qwen2.5 1.5B; Vulkan und CPU.
- Kompaktes Menü mit Copy, sicherem Undo, erneutem Verarbeiten des Originaltranskripts, Grammatik, Schreibstilen, Kürzen/Erweitern, Übersetzen, Prompt und Groß-/Kleinschreibung.
- Normal, lowercase und UPPERCASE als eine gegenseitig ausschließende Einstellung; einschließlich deutscher ß-Behandlung.

Geschützte Namen, Zahlen und Uhrzeiten werden kontrolliert. Mehrdeutige Uhrzeiten werden während Übersetzungen durch geschützte Platzhalter ersetzt. Verdächtige automatische Ergebnisse werden verworfen und der Originaltext bleibt erhalten. Diese Prüfungen sind keine vollständige semantische Verifikation. Andere Namen, implizite Aussagen oder komplexe Übersetzungen können weiterhin Modellfehler enthalten.

## Tatsächlich ausgeführte Validierung

- Vollständiger Release-Build, Restore, Tests, Self-contained Publish, Portable-ZIP und Installer; keine Compilerwarnungen oder Fehler.
- 71 automatisierte Tests bestanden, darunter Migration, Persistenz, Modellintegrität, Textpipeline, Audioqualität und Zwischenablage.
- Analyzer-Prüfung mit `dotnet format analyzers --verify-no-changes --severity warn` bestanden.
- 18 UI-Seiten jeweils in Light/Dark aufgebaut; Kontrast und Fenster-/Root-Hintergrund geprüft. Overview, Dictation und General bei 100/125/150/200 % als WPF-Bitmaps gerendert und zentrale Renderings visuell geprüft. Das ersetzt keine gemischten realen Monitor-DPI-Tests.
- Echte lokale Inferenz mit Balanced/Small und Qwen3 4B: Referenzsprache, Grammatik, Englisch, Französisch, Chinesisch und Prompt-Transformation. Zusätzlich ONNX-Sprecherzuordnung und PDF-Export.
- Derselbe native Test bestand im reinen CPU-Betrieb. Die Testläufe dauerten auf diesem Rechner etwa 14,6 s (automatische Schreibbeschleunigung) bzw. 19,3 s (CPU) für die gesamte Testsammlung, nicht pro Diktat und nicht als allgemeines Latenzversprechen.
- Fertig gepackte App im isolierten Smoke-Test gestartet: Tray erstellt, Settings-Layout aufgebaut, 10 Modelle im Katalog, 4 Mikrofone erkannt, Zustand Idle. Der Test registriert keine konkurrierenden globalen Shortcuts und verwendet ein separates Datenverzeichnis.

Beispielergebnisse der echten Modelltests:

- `Hello, I'm Mara, I'm 34 and I work at a hospital.`
- `Demain nous nous rencontrons à 11 heures.`
- `你好 Mara，我们将在 11点 相遇。`
- `Hilf mir beim Lernen für eine Matheprüfung über quadratische Funktionen.`

Berichte: `artifacts/tests.trx`, `artifacts/build.log`, `artifacts/native-smoke.json`, `artifacts/native-smoke-cpu.json`, `artifacts/ui/`, `artifacts/packaged-smoke/smoke-test.json`.

## Noch manuell zu prüfen

Die gestoppte Computersteuerung wurde nicht wieder aufgenommen. Physische Shortcut-/Push-to-talk-Tests, Aufnahme mit echter lockerer deutscher Sprache, Einfügen und Undo in Discord/Word/Chrome, Mikrofonentfernung/Zugriffsverweigerung, Neustart und gemischte Monitor-DPI-Szenarien sind für diese Überarbeitung nicht abschließend live validiert. Die vollständige Matrix steht in [MANUAL_TEST_CHECKLIST.md](MANUAL_TEST_CHECKLIST.md).

Die Pakete sind weiterhin unsignierte Entwicklungsbuilds. Die vorhandene Installation wird nicht automatisch überschrieben. Modelle liegen außerhalb der Installation und bleiben beim Update erhalten. Vor einer öffentlichen Veröffentlichung gelten weiterhin die dokumentierten Release- und Lizenzprüfungen.
