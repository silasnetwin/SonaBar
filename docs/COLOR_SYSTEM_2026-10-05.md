# SonaBar color system — 5 October 2026

Reference: [WhisperBar](https://whisperbar.pro/), reviewed for its visual hierarchy: a cool near-white canvas, dark text, white surfaces, a restrained blue accent and a clear green confirmation. SonaBar uses its own colors and Windows controls; no site assets or CSS were copied.

| Role | Light | Dark |
| --- | --- | --- |
| Canvas / window | `#ECECEC` | `#14161A` |
| Sidebar | `#E8E8E8` | `#111318` |
| Card | `#FFFFFF` | `#1D2127` |
| Main text | `#1D1D1F` | `#E9ECF2` |
| Secondary text | `#6E6E73` | `#A4ADBA` |
| Accent | `#0066CC` | `#4C8DF6` |
| Accent wash | `#E5F1FF` | `#1B2A44` |
| Border | `#D9D9D9` | `#2A2F37` |
| Success | `#34C759` | `#46C48C` |

The palette is applied through shared WPF resources, including navigation, fields, the recording overlay, the Windows caption controls and the installer. New installations start in Light; saved user theme choices remain unchanged. System and Dark are still available in General settings. Windows high-contrast colors continue to override the palette.

Automated theme tests cover contrast and page rendering in Light and Dark at multiple scales. The screenshots in `docs/media` show the light theme with the custom Windows title bar.
