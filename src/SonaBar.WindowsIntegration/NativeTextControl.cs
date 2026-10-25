using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Automation;

namespace SonaBar.WindowsIntegration;

public sealed record NativeTextSnapshot(nint Handle,int Start,int End,string Selected,string Fingerprint,string Value);
public static class NativeTextControl
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint handle,StringBuilder name,int length);
    [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW")] private static extern nint Message(nint handle,uint message,nint w,nint l,uint flags,uint timeout,out nuint result);
    [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW")] private static extern nint Selection(nint handle,uint message,out int start,out int end,uint flags,uint timeout,out nuint result);
    [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW",CharSet=CharSet.Unicode)] private static extern nint Read(nint handle,uint message,nint w,StringBuilder text,uint flags,uint timeout,out nuint result);
    [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW",CharSet=CharSet.Unicode)] private static extern nint Write(nint handle,uint message,nint w,string text,uint flags,uint timeout,out nuint result);
    private static string Fingerprint(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(text)));
    private static string? Value(nint handle)
    {
        if(Message(handle,0x000E,0,0,2,100,out var length)==0||length>262144)return null;
        var buffer=new StringBuilder((int)length+1);
        return Read(handle,0x000D,buffer.Capacity,buffer,2,100,out _)!=0?buffer.ToString():null;
    }
    public static NativeTextSnapshot? Capture(AutomationElement? element)
    {
        var handle=(nint)(element?.Current.NativeWindowHandle??0);if(handle==0)return null;
        var name=new StringBuilder(128);GetClassName(handle,name,name.Capacity);
        if(name.ToString() is not "Edit"&&!name.ToString().StartsWith("RichEdit",StringComparison.OrdinalIgnoreCase))return null;
        if((Native.GetWindowLongPtr(handle,-16)&0x800)!=0)return null;
        var value=Value(handle);if(value is null||Selection(handle,0xB0,out var start,out var end,2,100,out _)==0||start<0||end<start||end>value.Length)return null;
        return new(handle,start,end,value[start..end],Fingerprint(value),value);
    }
    public static void Validate(NativeTextSnapshot snapshot)
    {
        var value=Value(snapshot.Handle);
        if(value is null||Fingerprint(value)!=snapshot.Fingerprint)throw new InvalidOperationException("The original text field changed. Select the passage again before replacing it.");
    }
    public static void Select(NativeTextSnapshot snapshot)
    {
        Validate(snapshot);
        if(Message(snapshot.Handle,0xB1,snapshot.Start,snapshot.End,2,100,out _)==0)throw new InvalidOperationException("Windows could not restore the selected text.");
    }
    public static void Replace(NativeTextSnapshot snapshot,string text)
    {
        Select(snapshot);
        if(Write(snapshot.Handle,0xC2,1,text,2,250,out _)==0)throw new InvalidOperationException("Windows did not confirm text replacement.");
        var expected=snapshot.Value[..snapshot.Start]+text+snapshot.Value[snapshot.End..];
        var actual=Value(snapshot.Handle);
        if(actual is null||!Equivalent(actual,expected))throw new InvalidOperationException("Windows did not confirm that the text was inserted.");
    }
    private static bool Equivalent(string left,string right)=>left.Replace("\r\n","\n",StringComparison.Ordinal)==right.Replace("\r\n","\n",StringComparison.Ordinal);
}
