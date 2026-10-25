using System.Windows;
using System.Windows.Media;

namespace SonaBar.App;

/// <summary>
/// The complete colour system of the application. Every surface, text and status colour is
/// defined here once; the styles only refer to these keys. Dark anthracite surfaces with a
/// restrained blue accent are the default, a light variant is available for bright rooms.
/// </summary>
public sealed record Palette(
    string AppBackground,
    string WindowBackground,
    string Surface,
    string SurfaceElevated,
    string BackgroundElevated,
    string Card,
    string Sidebar,
    string SurfaceHover,
    string Hover,
    string SurfacePressed,
    string SurfaceSelected,
    string Border,
    string Line,
    string BorderStrong,
    string TextPrimary,
    string Ink,
    string TextSecondary,
    string Muted,
    string TextTertiary,
    string Accent,
    string AccentHover,
    string AccentPressed,
    string AccentInk,
    string AccentSoft,
    string Action,
    string ActionHover,
    string ActionInk,
    string Success,
    string Danger,
    string Error,
    string Warning,
    string OverlaySurface,
    string OverlayInk,
    string OverlayAccent,
    string OverlayError)
{
    /// <summary>The dark theme. Deep anthracite with a single restrained blue accent.</summary>
    public static Palette Dark { get; } = new(
        AppBackground: "#14161A",
        WindowBackground: "#14161A",
        Surface: "#171A1F",
        SurfaceElevated: "#1D2127",
        BackgroundElevated: "#1D2127",
        Card: "#1D2127",
        Sidebar: "#111318",
        SurfaceHover: "#232830",
        Hover: "#232830",
        SurfacePressed: "#2A3038",
        SurfaceSelected: "#1E2A40",
        Border: "#2A2F37",
        Line: "#2A2F37",
        BorderStrong: "#3C434E",
        TextPrimary: "#E9ECF2",
        Ink: "#E9ECF2",
        TextSecondary: "#A4ADBA",
        Muted: "#A4ADBA",
        TextTertiary: "#737C8A",
        Accent: "#4C8DF6",
        AccentHover: "#6BA0F8",
        AccentPressed: "#3B79DC",
        AccentInk: "#08111F",
        AccentSoft: "#1B2A44",
        Action: "#4C8DF6",
        ActionHover: "#6BA0F8",
        ActionInk: "#08111F",
        Success: "#46C48C",
        Danger: "#FF7B7B",
        Error: "#FF7B7B",
        Warning: "#E8B341",
        OverlaySurface: "#1E2229",
        OverlayInk: "#E9ECF2",
        OverlayAccent: "#6BA0F8",
        OverlayError: "#FF7B7B");

    /// <summary>
    /// The light theme follows a restrained, WhisperBar-inspired palette: warm neutral
    /// window surfaces, white cards, a saturated blue for the record control and a clear
    /// green for positive actions and checked boxes.
    /// </summary>
    public static Palette Light { get; } = new(
        AppBackground: "#ECECEC",
        WindowBackground: "#ECECEC",
        Surface: "#F2F2F2",
        SurfaceElevated: "#FFFFFF",
        BackgroundElevated: "#FFFFFF",
        Card: "#FFFFFF",
        Sidebar: "#E8E8E8",
        SurfaceHover: "#E5E5E5",
        Hover: "#E5E5E5",
        SurfacePressed: "#DCDCDC",
        SurfaceSelected: "#FFFFFF",
        Border: "#D9D9D9",
        Line: "#D9D9D9",
        BorderStrong: "#C6C6C6",
        TextPrimary: "#1D1D1F",
        Ink: "#1D1D1F",
        TextSecondary: "#6E6E73",
        Muted: "#6E6E73",
        TextTertiary: "#8E8E93",
        Accent: "#0066CC",
        AccentHover: "#0A6BD6",
        AccentPressed: "#0052A3",
        AccentInk: "#FFFFFF",
        AccentSoft: "#E5F1FF",
        Action: "#2FA84F",
        ActionHover: "#38B85A",
        ActionInk: "#FFFFFF",
        Success: "#34C759",
        Danger: "#FF3B30",
        Error: "#FF3B30",
        Warning: "#FF9500",
        OverlaySurface: "#F7F7F7",
        OverlayInk: "#1D1D1F",
        OverlayAccent: "#0A84FF",
        OverlayError: "#FF3B30");

    /// <summary>Every key with its value, so the application resources can be filled in one pass.</summary>
    public IEnumerable<(string Key, string Value)> Entries => new (string, string)[]
    {
        ("AppBackground", AppBackground), ("WindowBackground", WindowBackground),
        ("Surface", Surface), ("Background", Surface), ("SurfaceElevated", SurfaceElevated), ("BackgroundElevated", BackgroundElevated),
        ("Card", Card), ("Sidebar", Sidebar),
        ("SurfaceHover", SurfaceHover), ("Hover", Hover), ("SurfacePressed", SurfacePressed), ("SurfaceSelected", SurfaceSelected),
        ("Border", Border), ("Line", Line), ("BorderStrong", BorderStrong),
        ("TextPrimary", TextPrimary), ("Ink", Ink), ("TextSecondary", TextSecondary), ("Muted", Muted), ("TextTertiary", TextTertiary),
        ("Accent", Accent), ("AccentHover", AccentHover), ("AccentPressed", AccentPressed), ("AccentInk", AccentInk), ("AccentSoft", AccentSoft),
        ("Action", Action), ("ActionHover", ActionHover), ("ActionInk", ActionInk),
        ("Success", Success), ("Danger", Danger), ("Error", Error), ("Warning", Warning),
        ("OverlaySurface", OverlaySurface), ("OverlayInk", OverlayInk), ("OverlayAccent", OverlayAccent), ("OverlayError", OverlayError)
    };

    /// <summary>True when the theme is the dark one, used for the native title bar.</summary>
    public bool IsDark => ReferenceEquals(this, Dark) || TextPrimary == Dark.TextPrimary;

    /// <summary>Converts a colour into the value the window manager expects (0x00BBGGRR).</summary>
    public static int ToWindowColour(string hex)
    {
        var colour = (Color)ColorConverter.ConvertFromString(hex);
        return colour.B | (colour.G << 8) | (colour.R << 16);
    }
}

/// <summary>Applies a <see cref="Palette"/> to the running application.</summary>
public static class PaletteService
{
    /// <summary>Resolves the palette for a theme choice and writes it into the application resources.</summary>
    public static Palette Apply(string choice)
    {
        var dark = choice switch
        {
            "Dark" => true,
            "Light" => false,
            _ => SystemParameters.HighContrast || IsWindowsDark()
        };
        var palette = dark ? Palette.Dark : Palette.Light;
        var resources = Application.Current.Resources;
        foreach (var (key, value) in palette.Entries)
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        return palette;
    }

    /// <summary>Reads the personalization setting of Windows to follow the system theme.</summary>
    public static bool IsWindowsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme", 1) is int value && value == 0;
        }
        catch (Exception) { return false; }
    }
}
