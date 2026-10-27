using System.Runtime.InteropServices;
using System.Text;

namespace SonaBar.WindowsIntegration;
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct POINT {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] internal struct RECT {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] internal struct GUITHREADINFO
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public RECT CaretRect;
    }
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread,ref GUITHREADINFO info);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window,uint flags);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint window,ref POINT point);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(POINT point,uint flags);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor,int type,out uint x,out uint y);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint window,nint insertAfter,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window,out RECT rect);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint attach,uint attachTo,bool attachInput);
    [DllImport("user32.dll")] internal static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd,int command);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern void SwitchToThisWindow(nint hwnd,bool altTab);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern uint SendInput(uint count, INPUT[] input, int size);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] internal static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    internal static extern int ToUnicodeEx(uint key, uint scanCode, byte[] state, StringBuilder buffer, int count, uint flags, nint layout);
    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] internal struct KBDLLHOOKSTRUCT { internal uint VkCode, ScanCode, Flags, Time; internal nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { internal uint type; internal InputUnion data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] internal KEYBDINPUT keyboard; [FieldOffset(0)] internal MOUSEINPUT mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { internal ushort key, scan; internal uint flags, time; internal nuint extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { internal int x,y; internal uint mouseData,flags,time; internal nuint extra; }
    /// <summary>The top-level window that owns a focus or child handle.</summary>
    internal static nint RootWindow(nint hwnd) => hwnd == 0 ? 0 : GetAncestor(hwnd, 2);
    /// <summary>
    /// Returns the keyboard focus of the foreground window's UI thread. Checking this right
    /// before SendInput catches a background SonaBar window that regained focus while the
    /// target was being prepared.
    /// </summary>
    internal static nint FocusedWindow(nint foregroundWindow)
    {
        if(foregroundWindow==0)return 0;
        var thread=GetWindowThreadProcessId(foregroundWindow,out _);
        var info=new GUITHREADINFO{Size=(uint)Marshal.SizeOf<GUITHREADINFO>()};
        return GetGUIThreadInfo(thread,ref info)?info.Focus:0;
    }
    /// <summary>
    /// Restores a target window even when Windows' foreground lock rejects a plain
    /// SetForegroundWindow call. The attachment is released again in every case.
    /// </summary>
    internal static bool ForceForegroundWindow(nint hwnd)
    {
        if(hwnd==0||!IsWindow(hwnd))return false;
        if(GetForegroundWindow()==hwnd)return true;
        var foreground=GetForegroundWindow();
        var current=GetCurrentThreadId();
        var foregroundThread=foreground==0?0:GetWindowThreadProcessId(foreground,out _);
        var targetThread=GetWindowThreadProcessId(hwnd,out _);
        var foregroundAttached=false;
        var targetAttached=false;
        try
        {
            // Attaching to the target thread as well as the current foreground thread is
            // what lets Windows hand activation back to Chromium/Electron composers after
            // a long transcription. Without it, SetForegroundWindow can report success
            // while the foreground lock immediately restores the previous window.
            if(foregroundThread!=0&&foregroundThread!=current)
                foregroundAttached=AttachThreadInput(current,foregroundThread,true);
            if(targetThread!=0&&targetThread!=current&&targetThread!=foregroundThread)
                targetAttached=AttachThreadInput(current,targetThread,true);
            // Do not unmaximize a target that the user intentionally maximized.
            if(IsIconic(hwnd))ShowWindow(hwnd,9);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            for(var attempt=0;attempt<8&&GetForegroundWindow()!=hwnd;attempt++)Thread.Sleep(15);
            if(GetForegroundWindow()!=hwnd)
            {
                SwitchToThisWindow(hwnd,true);
                for(var attempt=0;attempt<8&&GetForegroundWindow()!=hwnd;attempt++)Thread.Sleep(15);
            }
            return GetForegroundWindow()==hwnd;
        }
        finally
        {
            if(targetAttached)AttachThreadInput(current,targetThread,false);
            if(foregroundAttached)AttachThreadInput(current,foregroundThread,false);
        }
    }
    internal static nint WindowAt(int x, int y) => WindowFromPoint(new POINT { X = x, Y = y });
    internal static void ClickAt(int x, int y)
    {
        GetCursorPos(out var previous);
        SetCursorPos(x, y);
        Send([
            new INPUT { type = 0, data = new InputUnion { mouse = new MOUSEINPUT { flags = 0x0002 } } },
            new INPUT { type = 0, data = new InputUnion { mouse = new MOUSEINPUT { flags = 0x0004 } } }
        ]);
        SetCursorPos(previous.X, previous.Y);
    }
    internal static void SendKey(ushort key, bool up = false) => Send([new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { key = key, flags = up ? 2u : 0 } } }]);
    internal static void Chord(ushort key)
    {
        SendKey(0x11); try { SendKey(key); SendKey(key, true); } finally { SendKey(0x11, true); }
    }
    internal static void Unicode(string text)
    {
        var list = new List<INPUT>();
        foreach (char c in text)
        {
            list.Add(new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { scan = c, flags = 4 } } });
            list.Add(new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { scan = c, flags = 6 } } });
        }
        Send(list.ToArray());
    }
    /// <summary>
    /// Sends Unicode in small batches. Electron and Slate composers can drop characters
    /// when a long transcription arrives as one huge input batch.
    /// </summary>
    internal static async Task UnicodeAsync(string text,CancellationToken token=default)
    {
        if(string.IsNullOrEmpty(text))return;
        const int chunkSize=32;
        for(var offset=0;offset<text.Length;offset+=chunkSize)
        {
            token.ThrowIfCancellationRequested();
            var length=Math.Min(chunkSize,text.Length-offset);
            Unicode(text.Substring(offset,length));
            if(offset+length<text.Length)await Task.Delay(2,token);
        }
    }
    /// <summary>Sends the literal text, using Enter for line breaks so multi-line snippets work in edit controls.</summary>
    internal static void Text(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (character is not ('\r' or '\n' or '\t')) continue;
            if (i > start) Unicode(text[start..i]);
            if (character == '\t') Unicode("\t");
            else
            {
                SendKey(0x0D); SendKey(0x0D, true);
                if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            start = i + 1;
        }
        if (start < text.Length) Unicode(text[start..]);
    }
    internal static void Backspaces(int count)
    {
        for (var i = 0; i < count; i++) { SendKey(0x08); SendKey(0x08, true); }
    }
    private static void Send(INPUT[] inputs) { if (Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length) throw new InvalidOperationException("Windows blocked text input. Apps running as administrator cannot receive input from SonaBar."); }
}
