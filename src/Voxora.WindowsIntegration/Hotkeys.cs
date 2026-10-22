using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Voxora.Core;

namespace Voxora.WindowsIntegration;

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly HwndSource source = new(new HwndSourceParameters("SineFlow hotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
    private readonly Dictionary<int, Hotkey> bindings = [];
    private readonly Native.HookProc hookProc;
    private nint hook;
    private int? held;
    public event Action<int, bool>? Triggered;
    public event Action<string>? Error;
    public GlobalHotkeyService()
    {
        source.AddHook(WindowProc); hookProc = KeyboardProc;
        hook = Native.SetWindowsHookEx(13, hookProc, Native.GetModuleHandle(null), 0);
        if (hook == 0) throw new InvalidOperationException("Could not initialize keyboard release detection.");
        SystemEvents.PowerModeChanged += PowerChanged; SystemEvents.SessionSwitch += SessionChanged;
    }
    public void Register(AppSettings settings)
    {
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
        if (code >= 0 && ((int)wParam == 0x101 || (int)wParam == 0x105) && held is int id && bindings.TryGetValue(id, out var key) && (uint)Marshal.ReadInt32(lParam) == key.Key)
        {
            held = null; source.Dispatcher.BeginInvoke(() => Triggered?.Invoke(id, false));
        }
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }
    private void Recover() => source.Dispatcher.BeginInvoke(() =>
    {
        held = null;
        foreach (var (id, key) in bindings) { Native.UnregisterHotKey(source.Handle, id); if (!Native.RegisterHotKey(source.Handle, id, key.Modifiers | 0x4000, key.Key)) Error?.Invoke($"Shortcut conflict after Windows resumed: {key}"); }
    });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Recover(); }
    private void SessionChanged(object sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionUnlock) Recover(); }
    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.SessionSwitch -= SessionChanged;
        foreach (var id in bindings.Keys) Native.UnregisterHotKey(source.Handle, id);
        Native.UnhookWindowsHookEx(hook); source.Dispose();
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
        if (enabled) key.SetValue("SineFlow", $"\"{Environment.ProcessPath}\" --tray"); else key.DeleteValue("SineFlow", false);
    }
}
