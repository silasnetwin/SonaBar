using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Interop;
using SonaBar.Core;

namespace SonaBar.WindowsIntegration;

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly HwndSource source = new(new HwndSourceParameters("SonaBar hotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
    private readonly Dictionary<int, Hotkey> bindings = [];
    private readonly Native.HookProc hookProc, mouseProc;
    private readonly List<Snippet> typedSnippets = [];
    private readonly StringBuilder typedBuffer = new();
    private nint hook, mouseHook;
    private int? held;
    private bool typedEnabled;
    private nint typedForeground, typedFocus;
    private int typedSuppressKeyUp;
    private long typedSuppressUntil;
    private bool typedReplacing;
    public event Action<int, bool>? Triggered;
    public event Action<string>? Error;
    /// <summary>Expands a matched snippet without touching the clipboard unless its content asks for it.</summary>
    public Func<Snippet, string>? ExpandTypedSnippet { get; set; }
    /// <summary>Lets the host pause typed expansion without tearing down the keyboard hook.</summary>
    public Func<bool>? TypedExpansionBlocked { get; set; }
    public GlobalHotkeyService()
    {
        source.AddHook(WindowProc); hookProc = KeyboardProc; mouseProc = MouseProc;
        hook = Native.SetWindowsHookEx(13, hookProc, Native.GetModuleHandle(null), 0);
        if (hook == 0) throw new InvalidOperationException("Could not initialize keyboard release detection.");
        // A click can move the caret inside the same field. Clear the typed buffer so a
        // later delimiter cannot replace text that was typed before the click.
        mouseHook = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
        SystemEvents.PowerModeChanged += PowerChanged; SystemEvents.SessionSwitch += SessionChanged;
    }
    public void Register(AppSettings settings)
    {
        typedEnabled = settings.ExpandTypedSnippets;
        typedSnippets.Clear();
        if (settings.Snippets is { Count: > 0 }) typedSnippets.AddRange(settings.Snippets);
        typedBuffer.Clear(); typedForeground = 0; typedFocus = 0; typedSuppressKeyUp = 0; typedReplacing = false;
        var next = new Dictionary<int, Hotkey> { [1] = settings.PlainHotkey, [3] = settings.EditHotkey };
        if(next.All(pair=>bindings.TryGetValue(pair.Key,out var existing)&&existing==pair.Value))return;
        var old = bindings.ToDictionary();
        foreach (var id in bindings.Keys) Native.UnregisterHotKey(source.Handle, id);
        bindings.Clear();
        try { foreach (var (id, key) in next) Add(id, key); }
        catch
        {
            foreach (var id in bindings.Keys) Native.UnregisterHotKey(source.Handle, id); bindings.Clear();
            foreach (var (id, key) in old) { if (Native.RegisterHotKey(source.Handle, id, key.Modifiers | 0x4000, key.Key)) bindings[id] = key; }
            if(old.TryGetValue(1,out var plain))settings.PlainHotkey=plain;
            if(old.TryGetValue(3,out var edit))settings.EditHotkey=edit;
            throw;
        }
    }
    private void Add(int id, Hotkey key)
    {
        if (!Native.RegisterHotKey(source.Handle, id, key.Modifiers | 0x4000, key.Key)) throw new InvalidOperationException($"This shortcut is already being used: {key}.");
        bindings[id] = key;
    }
    private nint WindowProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0312) { held = (int)wParam; Triggered?.Invoke((int)wParam, true); handled = true; }
        return 0;
    }
    private nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var message = (int)wParam;
            var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            var injected = (info.Flags & 0x10) != 0;
            if (!injected && (message is 0x100 or 0x104) && HandleTypedKeyDown(info)) return 1;
            if (!injected && (message is 0x101 or 0x105)) HandleTypedKeyUp(info);
            if ((message == 0x101 || message == 0x105) && held is int id && bindings.TryGetValue(id, out var key) && info.VkCode == key.Key)
            {
                held = null; source.Dispatcher.BeginInvoke(() => Triggered?.Invoke(id, false));
            }
        }
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }
    private nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (int)wParam is 0x0201 or 0x0204 or 0x0207 or 0x020B)
        {
            typedBuffer.Clear(); typedForeground = 0; typedFocus = 0; typedSuppressKeyUp = 0;
        }
        return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
    }
    private bool HandleTypedKeyDown(Native.KBDLLHOOKSTRUCT info)
    {
        if (typedSuppressKeyUp != 0 && Environment.TickCount64 > typedSuppressUntil) typedSuppressKeyUp = 0;
        if (typedSuppressKeyUp != 0 && (int)info.VkCode == typedSuppressKeyUp) return true;
        if (typedReplacing) return true;
        if (!typedEnabled || typedSnippets.Count == 0 || TypedExpansionBlocked?.Invoke() == true)
        {
            typedBuffer.Clear(); return false;
        }
        var foreground = Native.GetForegroundWindow();
        var focus = foreground == 0 ? 0 : Native.FocusedWindow(foreground);
        if (foreground != typedForeground || focus != typedFocus)
        {
            typedForeground = foreground; typedFocus = focus; typedBuffer.Clear();
        }
        if (foreground == 0 || focus == 0 || IsOwnWindow(foreground))
        {
            typedBuffer.Clear(); return false;
        }
        if (HasCommandModifier())
        {
            typedBuffer.Clear(); return false;
        }
        var key = (int)info.VkCode;
        if (key == 0x08)
        {
            if (typedBuffer.Length > 0) typedBuffer.Length--;
            return false;
        }
        if (key is 0x1B or 0x25 or 0x26 or 0x27 or 0x28 or 0x24 or 0x23 or 0x21 or 0x22 or 0x2D or 0x2E or 0xE5)
        {
            typedBuffer.Clear(); return false;
        }
        var text = KeyText(info, foreground, out var deadKey);
        if (deadKey) { typedBuffer.Clear(); return false; }
        if (text.Length == 0) return false;
        if (key == 0x0D)
        {
            if (TryExpand(key, '\r', out _)) return true;
            // Enter moves the caret to a new line. A trigger cannot span that line,
            // so do not let a later space replace text typed before the line break.
            typedBuffer.Clear(); return false;
        }
        if (key == 0x09)
        {
            if (TryExpand(key, '\t', out _)) return true;
            typedBuffer.Clear(); return false;
        }
        if (text.Length == 1 && TypedSnippetMatcher.IsDelimiter(text[0]))
        {
            if (TryExpand(key, text[0], out var blocked)) return true;
            // A space or punctuation mark can be part of a longer trigger such as
            // "be right back" or "e.g.". Keep it in the buffer when it did not
            // complete a match; the target still receives the character.
            if (!blocked) { typedBuffer.Append(text); TrimBuffer(); }
            return false;
        }
        typedBuffer.Append(text);
        TrimBuffer();
        return false;
    }
    private void HandleTypedKeyUp(Native.KBDLLHOOKSTRUCT info)
    {
        if (typedSuppressKeyUp != 0 && (int)info.VkCode == typedSuppressKeyUp) typedSuppressKeyUp = 0;
    }
    private bool TryExpand(int key, char delimiter, out bool blocked)
    {
        blocked = false;
        var match = TypedSnippetMatcher.Match(typedBuffer.ToString(), typedSnippets);
        if (match is null) return false;
        var foreground = Native.GetForegroundWindow();
        var focus = foreground == 0 ? 0 : Native.FocusedWindow(foreground);
        if (foreground != typedForeground || focus != typedFocus || IsSensitive(focus))
        {
            typedBuffer.Clear(); blocked = true; return false;
        }
        if (ExpandTypedSnippet is null)
        {
            typedBuffer.Clear(); blocked = true; return false;
        }
        string content;
        try { content = ExpandTypedSnippet(match.Snippet) ?? ""; }
        catch (Exception)
        {
            typedBuffer.Clear(); blocked = true; Error?.Invoke("The snippet could not be expanded. Check the clipboard and try again."); return false;
        }
        typedBuffer.Clear();
        typedSuppressKeyUp = key; typedSuppressUntil = Environment.TickCount64 + 750; typedReplacing = true;
        try
        {
            source.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (!source.Dispatcher.HasShutdownStarted) ReplaceTypedSnippet(match, content, delimiter, foreground, focus);
                }
                catch (Exception)
                {
                    Error?.Invoke("Windows blocked the snippet. Apps running as administrator cannot receive input from SonaBar.");
                }
                finally { typedReplacing = false; typedSuppressKeyUp = 0; }
            });
        }
        catch (InvalidOperationException) { typedReplacing = false; typedSuppressKeyUp = 0; }
        return true;
    }
    private void TrimBuffer()
    {
        if (typedBuffer.Length > 256) typedBuffer.Remove(0, typedBuffer.Length - 256);
    }
    private static void ReplaceTypedSnippet(TypedSnippetMatch match, string content, char delimiter, nint foreground, nint focus)
    {
        if (Native.GetForegroundWindow() != foreground || Native.FocusedWindow(foreground) != focus) return;
        Native.Backspaces(match.Trigger.Length);
        Native.Text(content);
        AppendDelimiter(content, delimiter);
    }
    private static void AppendDelimiter(string content, char delimiter)
    {
        if (delimiter == '\r')
        {
            if (!content.EndsWith('\n') && !content.EndsWith('\r')) { Native.SendKey(0x0D); Native.SendKey(0x0D, true); }
            return;
        }
        if (delimiter == '\t') { Native.SendKey(0x09); Native.SendKey(0x09, true); return; }
        if (delimiter == ' ')
        {
            if (content.Length == 0 || !char.IsWhiteSpace(content[^1])) Native.Unicode(" ");
            return;
        }
        Native.Unicode(delimiter.ToString());
    }
    private static string KeyText(Native.KBDLLHOOKSTRUCT info, nint foreground, out bool deadKey)
    {
        deadKey = false;
        var state = new byte[256];
        if (!Native.GetKeyboardState(state)) return "";
        state[info.VkCode] = 0x80;
        if (IsDown(0x10)) state[0x10] = 0x80;
        var thread = Native.GetWindowThreadProcessId(foreground, out _);
        var layout = Native.GetKeyboardLayout(thread);
        var scan = Native.MapVirtualKey(info.VkCode, 0);
        var buffer = new StringBuilder(16);
        var count = Native.ToUnicodeEx(info.VkCode, scan, state, buffer, buffer.Capacity, 0, layout);
        if (count < 0) { deadKey = true; return ""; }
        if (count == 0) return "";
        return buffer.ToString(0, Math.Min(count, buffer.Length));
    }
    private static bool HasCommandModifier() => IsDown(0x11) || IsDown(0x12) || IsDown(0x5B) || IsDown(0x5C);
    private static bool IsDown(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
    private static bool IsOwnWindow(nint window)
    {
        Native.GetWindowThreadProcessId(window, out var processId);
        return processId == (uint)Environment.ProcessId;
    }
    private static bool IsSensitive(nint focus)
    {
        if (focus == 0) return true;
        try
        {
            var style = (long)Native.GetWindowLongPtr(focus, -16);
            if ((style & 0x20) != 0) return true;
            var element = AutomationElement.FromHandle(focus);
            if (element is not null && element.Current.IsPassword) return true;
            var focused = AutomationElement.FocusedElement;
            return focused is not null && focused.Current.IsPassword;
        }
        catch (Exception) { return false; }
    }
    private void Recover() => source.Dispatcher.BeginInvoke(() =>
    {
        held = null; typedBuffer.Clear(); typedForeground = 0; typedFocus = 0; typedSuppressKeyUp = 0; typedReplacing = false;
        foreach (var (id, key) in bindings) { Native.UnregisterHotKey(source.Handle, id); if (!Native.RegisterHotKey(source.Handle, id, key.Modifiers | 0x4000, key.Key)) Error?.Invoke($"Shortcut conflict after Windows resumed: {key}"); }
    });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Recover(); }
    private void SessionChanged(object sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionUnlock) Recover(); }
    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.SessionSwitch -= SessionChanged;
        foreach (var id in bindings.Keys) Native.UnregisterHotKey(source.Handle, id);
        Native.UnhookWindowsHookEx(hook);
        if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
        source.Dispose();
    }
}
public static class WindowIntegration
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;

    /// <summary>
    /// Paints the native title bar in the dark colour scheme and gives it the colour of the
    /// application, so the window frame belongs to the product instead of standing out.
    /// </summary>
    public static void ApplyTitleBar(nint handle, bool dark, int captionColor)
    {
        if (handle == nint.Zero) return;
        try
        {
            var mode = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref mode, sizeof(int));
            var caption = captionColor;
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(int));
            var border = dark ? 0x002A2F37 : 0x00DFE3E9;
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));
            // Windows 11 rounds window corners itself; older systems simply ignore the request.
            var corners = 2;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corners, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>
    /// Clips a window to a rounded shape using a window region. The corners then belong to
    /// the desktop behind the window instead of being painted, which avoids the black corners
    /// a layered window produces and keeps the window on the graphics processor.
    /// </summary>
    public static void ApplyRoundedRegion(nint handle, int width, int height, int radius)
    {
        if (handle == nint.Zero || width <= 0 || height <= 0) return;
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == nint.Zero) return;
        // The window takes ownership of the region, so it must not be deleted here.
        SetWindowRgn(handle, region, true);
    }

    public static void PositionOverlay(nint window,double width,double height,int? savedX,int? savedY)
    {
        var cursor=System.Windows.Forms.Cursor.Position;
        var point=savedX.HasValue&&savedY.HasValue?new System.Drawing.Point(savedX.Value,savedY.Value):cursor;
        if(!System.Windows.Forms.Screen.AllScreens.Any(screen=>screen.WorkingArea.Contains(point)))point=cursor;
        var area=System.Windows.Forms.Screen.FromPoint(point).WorkingArea;
        var monitor=Native.MonitorFromPoint(new Native.POINT{X=point.X,Y=point.Y},2);
        if(Native.GetDpiForMonitor(monitor,0,out var dpi,out _)!=0)dpi=96;
        var pixelWidth=Math.Min(area.Width,(int)Math.Ceiling(width*dpi/96));var pixelHeight=Math.Min(area.Height,(int)Math.Ceiling(height*dpi/96));
        var x=savedX.HasValue?point.X:area.Left+(area.Width-pixelWidth)/2;
        var y=savedY.HasValue?point.Y:area.Bottom-pixelHeight-(int)(32*dpi/96);
        Native.SetWindowPos(window,0,Math.Clamp(x,area.Left,area.Right-pixelWidth),Math.Clamp(y,area.Top,area.Bottom-pixelHeight),pixelWidth,pixelHeight,0x0010|0x0004);
    }
    public static (int Left,int Top) WindowPosition(nint window){if(!Native.GetWindowRect(window,out var rect))throw new InvalidOperationException("Could not save overlay position.");return(rect.Left,rect.Top);}
    public static void MakeNonActivating(nint handle) => Native.SetWindowLongPtr(handle, -20, Native.GetWindowLongPtr(handle, -20) | 0x08000000 | 0x00000080);
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("SonaBar", $"\"{Environment.ProcessPath}\" --tray"); else key.DeleteValue("SonaBar", false);
    }
}
