# SineFlow color system — 5 October 2026

Reference: [WhisperBar](https://whisperbar.pro/), reviewed for its visual hierarchy: a cool near-white canvas, dark text, white surfaces and a restrained green accent. SineFlow uses its own colors and Windows controls; no site assets or CSS were copied.

| Role | Light | Dark |
| --- | --- | --- |
| Canvas | `#F8F9F8` | `#101713` |
| Sidebar | `#FFFFFF` | `#151F19` |
| Card | `#FFFFFF` | `#1A2520` |
| Main text | `#17211C` | `#F4F8F5` |
| Secondary text | `#5F6D66` | `#AFBDB3` |
| Accent | `#177A52` | `#79D6A7` |
| Accent wash | `#E4F2EA` | `#244331` |
| Border | `#E0E8E2` | `#304237` |

The palette is applied through shared WPF resources, including navigation, fields, the recording overlay, and the installer. New installations start in Light; saved user theme choices remain unchanged. System and Dark are still available in General settings. Windows high-contrast colors continue to override the palette.

Automated theme tests cover contrast and page rendering in Light and Dark at multiple scales. Rendered previews are in `artifacts/ui-palette-2026-10-05/`.
