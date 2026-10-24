using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;
using Voxora.Core;

namespace Voxora.WindowsIntegration;

public sealed record InsertionTarget(nint Window, uint ProcessId, string Executable, AutomationElement? Element, TextPatternRange? Selection = null,string WindowTitle="",NativeTextSnapshot? NativeSelection=null);
public sealed record SelectedPassage(InsertionTarget Target,string Text);
public sealed record InsertionReceipt(InsertionTarget Target,string Inserted,string Replaced,TextPatternRange? Range, bool WholeEmptyField);
public sealed class ClipboardService
{
    public DataObject Capture()
    {
        var source = Clipboard.GetDataObject(); var copy = new DataObject();
        if (source is null) return copy;
        if(Clipboard.ContainsImage())
        {
            using var bitmap=Clipboard.GetImage();
            if(bitmap is not null)copy.SetImage(new System.Drawing.Bitmap(bitmap));
        }
        foreach (var format in source.GetFormats(false))
        {
            if(format==DataFormats.Bitmap&&copy.ContainsImage())continue;
            var value = source.GetData(format, false);
            if (value is null) continue;
            object clone = value switch
            {
                string s => s,
                byte[] b => b.ToArray(),
                string[] s => s.ToArray(),
                MemoryStream s => new MemoryStream(s.ToArray()),
                System.Drawing.Image b => b.Clone(),
                ICloneable c => c.Clone(),
                _ when value.GetType().IsValueType => value,
                _ => throw new InvalidOperationException("This application's clipboard format cannot be safely preserved. Choose Unicode insertion or Replace clipboard in Settings.")
            };
            if(clone is System.Drawing.Image bitmap)copy.SetImage(bitmap);else copy.SetData(format, false, clone);
        }
        return copy;
    }
    public void Restore(DataObject data) => SetDataObject(data);
    public string Text() => Clipboard.ContainsText() ? Clipboard.GetText() : "";
    public uint SetText(string text)
    {
        SetDataObject(new DataObject(DataFormats.UnicodeText,text));
        return Native.GetClipboardSequenceNumber();
    }
    public void RestoreIfUnchanged(DataObject data, uint sequence) { if (Native.GetClipboardSequenceNumber() == sequence) Restore(data); }
    private static void SetDataObject(DataObject data)
    {
        ExternalException? last=null;
        // Windows clipboard ownership is transiently contended by shell integrations and target
        // applications. Retrying here protects the user's previous clipboard after a paste.
        for(var attempt=0;attempt<8;attempt++)
        {
            try { Clipboard.SetDataObject(data, true, 5, 40); return; }
            catch(ExternalException error) { last=error; Thread.Sleep(40*(attempt+1)); }
        }
        throw new InvalidOperationException("Windows kept the clipboard busy, so SineFlow could not safely restore it.",last);
    }
}
public sealed class TextInsertionService(ClipboardService clipboard) : IDisposable
{
    private readonly AutomationProbe probe = new();
    /// <summary>
    /// Runs an inspection on the interface thread. Set by the application at start-up. It is
    /// needed for fields inside SineFlow itself, because Windows has to call back into this
    /// process to describe them and the helper thread would wait for the interface thread,
    /// which in turn waits for the helper thread.
    /// </summary>
    public static Func<Func<InsertionTarget>, InsertionTarget>? InterfaceThread { get; set; }
    /// <summary>How long Windows may take to describe the focused field before we give up.</summary>
    public static readonly TimeSpan CaptureTimeout = TimeSpan.FromMilliseconds(900);
    public InsertionReceipt? LastReceipt {get;private set;}
    public void ForgetReceipt()=>LastReceipt=null;
    /// <summary>
    /// Reads the focused field. The inspection runs on a dedicated automation thread with a
    /// hard limit, because a busy target application can otherwise freeze the whole program.
    /// </summary>
    public InsertionTarget CaptureTarget()
    {
        // A field inside SineFlow is inspected on the interface thread: Windows describes it
        // from this process, and asking a helper thread would deadlock both threads.
        if (IsOwnWindow(Native.GetForegroundWindow()))
            return InterfaceThread?.Invoke(CaptureTargetCore) ?? CaptureTargetCore();
        var target = probe.TryRun(CaptureTargetCore, CaptureTimeout);
        if (target is null) throw new InvalidOperationException("Windows did not answer while the focused field was inspected. Try the shortcut again in an editable field.");
        return target;
    }
    /// <summary>True when the window belongs to this program.</summary>
    public static bool IsOwnWindow(nint window)
    {
        if (window == nint.Zero) return false;
        Native.GetWindowThreadProcessId(window, out var processId);
        return IsOwnProcess(processId);
    }
    /// <summary>True when the reported process is this program.</summary>
    public static bool IsOwnProcess(uint processId) => processId == (uint)Environment.ProcessId;
    private InsertionTarget CaptureTargetCore()
    {
        var hwnd = Native.GetForegroundWindow(); var thread = Native.GetWindowThreadProcessId(hwnd, out var pid);
        string name;
        try { using var process = Process.GetProcessById((int)pid); name = process.ProcessName + ".exe"; } catch { name = "Unknown"; }
        AutomationElement? element = null;
        try { element = AutomationElement.FocusedElement; } catch (ElementNotAvailableException) { }
        // Some hosted editors report a generic focused pane or an outer browser document.
        // The OS caret can lead to the actual edit provider. Accept it only within the same
        // process, and never let it replace a precise Edit provider with a broader Document.
        try
        {
            var probeCaret = element is null || element.Current.ControlType != ControlType.Edit || IsBrowserChrome(element);
            if(probeCaret)
            {
                var info=new Native.GUITHREADINFO{Size=(uint)Marshal.SizeOf<Native.GUITHREADINFO>()};
                if(Native.GetGUIThreadInfo(thread,ref info)&&info.Caret!=0)
                {
                    Native.GetWindowThreadProcessId(info.Caret,out var caretPid);
                    var point=new Native.POINT{X=info.CaretRect.Left+1,Y=info.CaretRect.Top+1};
                    if(caretPid==pid&&Native.ClientToScreen(info.Caret,ref point))
                    {
                        var candidate=AutomationElement.FromPoint(new System.Windows.Point(point.X,point.Y));
                        if(candidate.Current.ProcessId==pid&&IsEditableElement(candidate)&&
                           (candidate.Current.ControlType==ControlType.Edit||element is null||!IsEditableElement(element)))
                            element=candidate;
                    }
                }
            }
        }
        catch(ElementNotAvailableException) { }
        TextPatternRange? selection=null;
        try {if(element?.TryGetCurrentPattern(TextPattern.Pattern,out var pattern)==true)selection=((TextPattern)pattern).GetSelection().FirstOrDefault()?.Clone();}catch(InvalidOperationException){}
        string title="";try{title=AutomationElement.FromHandle(hwnd).Current.Name;}catch(InvalidOperationException){}
        var target = new InsertionTarget(hwnd, pid, name, element, selection,title,NativeTextControl.Capture(element)); Guard(target); return target;
    }
    public static string DescribeTarget(InsertionTarget target)
    {
        var control = "Unavailable";
        var patterns = "none";
        try
        {
            if (target.Element is not null)
            {
                control = target.Element.Current.ControlType.ProgrammaticName;
                var list = new List<string>();
                if (target.Element.TryGetCurrentPattern(ValuePattern.Pattern, out _)) list.Add("Value");
                if (target.Element.TryGetCurrentPattern(TextPattern.Pattern, out _)) list.Add("Text");
                patterns = list.Count == 0 ? "none" : string.Join(',', list);
            }
        }
        catch (ElementNotAvailableException) { control = "Unavailable"; }
        return $"hwnd=0x{target.Window.ToInt64():X};pid={target.ProcessId};process={Path.GetFileName(target.Executable)};control={control};patterns={patterns};native={(target.NativeSelection is not null)};selection={(target.Selection is not null)}";
    }
    public static void Guard(InsertionTarget target)
    {
        if (target.Element is null) throw new InvalidOperationException("Windows could not inspect this field safely. Focus an editable field and try again.");
        try
        {
            if (target.Element.Current.IsPassword) throw new InvalidOperationException("SineFlow never reads or types into password fields.");
            if(!target.Element.Current.IsEnabled)throw new InvalidOperationException("This text field is disabled.");
        }
        catch(ElementNotAvailableException)
        {
            throw new InvalidOperationException("The original text field is no longer available. Your text is available in SineFlow.");
        }
    }
    private static bool IsBrowserChrome(AutomationElement element)
    {
        try
        {
            return element.Current.ClassName.Contains("Omnibox",StringComparison.OrdinalIgnoreCase) ||
                   element.Current.Name.Contains("Address and search bar",StringComparison.OrdinalIgnoreCase);
        }
        catch(ElementNotAvailableException) { return false; }
    }
    /// <summary>
    /// Returns true for a field that can receive text. Browsers expose contenteditables as
    /// Document or Edit providers with a TextPattern but no reliable ValuePattern, so a
    /// missing ValuePattern is not evidence that the field is read-only.
    /// </summary>
    private static bool IsEditableElement(AutomationElement element)
    {
        try
        {
            if(element.Current.IsPassword||!element.Current.IsEnabled)return false;
            var controlType=element.Current.ControlType;
            if(controlType!=ControlType.Edit&&controlType!=ControlType.Document)return false;
            if(element.TryGetCurrentPattern(ValuePattern.Pattern,out var value)&&value is ValuePattern field&&!field.Current.IsReadOnly)return true;
            if(element.TryGetCurrentPattern(TextPattern.Pattern,out var pattern)&&pattern is TextPattern text)
            {
                var selection=text.GetSelection().FirstOrDefault();
                if(selection?.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is bool readOnly)return !readOnly;
                // An empty contenteditable can have no selection range. Its TextPattern is
                // still the provider used by Chrome and other browsers for inserted text.
                return true;
            }
            return false;
        }
        catch(ElementNotAvailableException) { return false; }
        catch(InvalidOperationException) { return false; }
    }
    public static bool IsEditable(InsertionTarget target)
    {
        Guard(target);
        if(target.NativeSelection is not null)return true;
        return IsEditableElement(target.Element!);
    }
    public SelectedPassage? SelectionFor(InsertionTarget target)
    {
        var text=target.NativeSelection?.Selected??target.Selection?.GetText(14001)??"";
        if(string.IsNullOrWhiteSpace(text))return null;
        if(text.Length>14000)throw new InvalidOperationException("Select a shorter passage (up to 14,000 characters).");
        if(!IsEditable(target))throw new InvalidOperationException("The selected text is read-only or Windows cannot verify that it is editable.");
        return new(target,text);
    }
    public static void ValidateSelection(SelectedPassage passage)
    {
        if(!IsEditable(passage.Target))throw new InvalidOperationException("The original selection is no longer editable.");
        if(passage.Target.NativeSelection is {} native)NativeTextControl.Validate(native);
        else if(passage.Target.Selection?.GetText(14001)!=passage.Text)throw new InvalidOperationException("The selected passage changed. Select it again to edit safely.");
    }
    public async Task ReplaceSelectionAsync(SelectedPassage passage,string text,ClipboardMode mode,CancellationToken token)
    {
        ValidateSelection(passage);await FocusAsync(passage.Target);ValidateSelection(passage);token.ThrowIfCancellationRequested();
        await InsertAsync(passage.Target,text,new(passage.Target.Executable,InsertionStrategy.Automatic,false,""),mode,token);
    }
    public Task RestoreFocusAsync(InsertionTarget target)=>FocusAsync(target);
    public async Task<string> SelectedTextAsync(InsertionTarget target)
    {
        Guard(target);
        if (target.Element!.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
        {
            var selected = ((TextPattern)pattern).GetSelection();
            var value = string.Join("", selected.Select(r => r.GetText(-1)));
            if (value.Length > 0) return value;
        }
        var previous = clipboard.Capture(); uint sequence = Native.GetClipboardSequenceNumber();
        await FocusAsync(target);
        Native.Chord(0x43);
        for (var i = 0; i < 20 && Native.GetClipboardSequenceNumber() == sequence; i++) await Task.Delay(25);
        var changed = Native.GetClipboardSequenceNumber();
        if (changed == sequence) throw new InvalidOperationException("No selected text was found. Select some text first.");
        try { var text = clipboard.Text(); if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("The selection does not contain text."); return text; }
        finally { clipboard.RestoreIfUnchanged(previous, changed); }
    }
    public async Task<string> InsertAsync(InsertionTarget target, string text, ApplicationProfile profile, ClipboardMode mode, CancellationToken token,Action<string>? phase=null,Action<string>? diagnostic=null)
    {
        LastReceipt=null;
        token.ThrowIfCancellationRequested();
        diagnostic?.Invoke("delivery=start;"+DescribeTarget(target));
        if (!Native.IsWindow(target.Window))
        {
            clipboard.SetText(text);
            diagnostic?.Invoke("delivery=failed;reason=target_window_missing;fallback=copy_only");
            throw new InvalidOperationException("The original application closed before text could be delivered. Your text is ready to copy.");
        }
        Guard(target);
        await FocusAsync(target,diagnostic); phase?.Invoke("T9"); token.ThrowIfCancellationRequested();
        var current = AutomationElement.FocusedElement;
        if (current is null || current.Current.IsPassword)
        {
            diagnostic?.Invoke("delivery=failed;reason=focus_not_safe");
            throw new InvalidOperationException("Insertion stopped: the current field cannot be safely inspected.");
        }
        target=RefineTarget(target);
        Guard(target);
        var beforeText=InspectableText(target);
        // ValuePattern replaces the whole value, so only use it for an empty field.
        bool inserted=false,wholeEmptyField=false;
        var replaced=target.Selection?.GetText(-1)??"";
        if(profile.Strategy==InsertionStrategy.Automatic&&target.NativeSelection is {} native)
        {
            diagnostic?.Invoke("backend=native_edit;attempted=true");
            try
            {
                NativeTextControl.Replace(native,text);
                replaced=native.Selected;
                inserted=true;
                diagnostic?.Invoke("backend=native_edit;result=verified_exact");
            }
            catch(Exception error) when (error is not OperationCanceledException)
            {
                // A native replacement can fail after the field changed or after the control
                // rejected the message. Never abort delivery here; fall through to the UIA,
                // Unicode and clipboard paths, but first check whether the text already landed.
                diagnostic?.Invoke($"backend=native_edit;result=failed;exception={error.GetType().Name};fallback=continue");
                var after=InspectableText(target);
                if(after is not null&&ConfirmedChange(after,text,beforeText))
                {
                    replaced=native.Selected;
                    inserted=true;
                    diagnostic?.Invoke("backend=native_edit;result=confirmed_after_error");
                }
            }
        }
        if (!inserted&&profile.Strategy == InsertionStrategy.Automatic && target.Element!.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && value is ValuePattern vp && !vp.Current.IsReadOnly && vp.Current.Value.Length == 0)
        {
            diagnostic?.Invoke("backend=uia_value;attempted=true");
            try
            {
                vp.SetValue(text);
                if(vp.Current.Value!=text)throw new InvalidOperationException("Windows did not confirm that the text was inserted.");
                inserted=true;wholeEmptyField=true;
                diagnostic?.Invoke("backend=uia_value;result=verified_exact");
            }
            catch(Exception error) when (error is not OperationCanceledException)
            {
                diagnostic?.Invoke($"backend=uia_value;result=not_confirmed;exception={error.GetType().Name};fallback=continue");
            }
        }
        if (!inserted && (profile.Strategy == InsertionStrategy.Unicode||profile.Strategy==InsertionStrategy.Automatic&&text.Length<=2000&&IsEditable(target)&&HasInspectableTextPattern(target)))
        {
            diagnostic?.Invoke("backend=sendinput_unicode;attempted=true");
            Native.Unicode(text);
            var confirmation=await ConfirmDeliveryAsync(target,text,beforeText,token);
            if(confirmation==DeliveryConfirmation.Confirmed)
            {
                inserted=true;
                diagnostic?.Invoke("backend=sendinput_unicode;result=verified");
            }
            else
            {
                diagnostic?.Invoke($"backend=sendinput_unicode;result={confirmation.ToString().ToLowerInvariant()};fallback=clipboard");
                if(confirmation==DeliveryConfirmation.ChangedWithoutText)
                {
                    var after=InspectableText(target);
                    if(after is not null&&ContainsPartialInsertion(after,text,beforeText))
                        throw new InvalidOperationException("SineFlow inserted part of the text but could not verify the complete result. Your text is available to copy.");
                }
            }
        }
        if (!inserted)
        {
            diagnostic?.Invoke("backend=clipboard_paste;attempted=true;preserve="+(mode==ClipboardMode.Preserve));
            var previous = mode == ClipboardMode.Preserve ? clipboard.Capture() : null;
            var sequence = clipboard.SetText(text);
            try
            {
                Native.Chord(0x56);
                var confirmation=await ConfirmDeliveryAsync(target,text,beforeText,token);
                if(confirmation==DeliveryConfirmation.Confirmed)
                {
                    inserted=true;
                    diagnostic?.Invoke("backend=clipboard_paste;result=verified");
                }
                else if(confirmation==DeliveryConfirmation.Opaque&&IsEditable(target))
                {
                    // Some editors expose no readable text at all. The paste shortcut was
                    // delivered to an editable field; failing here would only report a false
                    // error after a successful paste.
                    inserted=true;
                    diagnostic?.Invoke("backend=clipboard_paste;result=assumed;reason=opaque_editable_target");
                }
                else
                {
                    diagnostic?.Invoke($"backend=clipboard_paste;result={confirmation.ToString().ToLowerInvariant()};reason=not_confirmed");
                    throw new InvalidOperationException("SineFlow could not confirm that the destination accepted the text. Your text is ready to copy.");
                }
            }
            finally { if (previous is not null) clipboard.RestoreIfUnchanged(previous, sequence); }
        }
        if (profile.PressEnter) { token.ThrowIfCancellationRequested(); Native.SendKey(13); Native.SendKey(13, true); }
        if(!profile.PressEnter)
        {
            TextPatternRange? range=null;
            try
            {
                if(target.Element!.TryGetCurrentPattern(TextPattern.Pattern,out var textPattern))
                {
                    var document=((TextPattern)textPattern).DocumentRange;var candidate=document.FindText(text,false,false);
                    if(candidate is not null)
                    {
                        var tail=document.Clone();tail.MoveEndpointByRange(TextPatternRangeEndpoint.Start,candidate,TextPatternRangeEndpoint.End);
                        if(tail.FindText(text,false,false)is null)range=candidate;
                    }
                }
                if(range is not null||wholeEmptyField)LastReceipt=new(target,text,replaced,range,wholeEmptyField);
            }
            catch(ElementNotAvailableException){}
        }
        diagnostic?.Invoke("delivery=confirmed");
        return "Text inserted";
    }
    public async Task ReplaceReceiptAsync(InsertionReceipt receipt,string replacement,ClipboardMode mode,CancellationToken token)
    {
        Guard(receipt.Target);
        if(receipt.Range is not null)
        {
            if(receipt.Range.GetText(-1)!=receipt.Inserted)throw new InvalidOperationException("The inserted text has changed. Select the passage again to edit it safely.");
            var target=receipt.Target with {Selection=receipt.Range,NativeSelection=null};
            if(replacement.Length==0){await FocusAsync(target);token.ThrowIfCancellationRequested();Native.SendKey(8);Native.SendKey(8,true);LastReceipt=null;return;}
            await InsertAsync(target,replacement,new(receipt.Target.Executable,InsertionStrategy.Clipboard,false,""),mode,token);
        }
        else if(receipt.WholeEmptyField&&receipt.Target.Element!.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern)&&pattern is ValuePattern value&&value.Current.Value==receipt.Inserted)
        {
            await FocusAsync(receipt.Target);token.ThrowIfCancellationRequested();value.SetValue(replacement);
            LastReceipt=receipt with {Inserted=replacement,Replaced=receipt.Inserted};
        }
        else throw new InvalidOperationException("The original field is no longer safely editable. The revised text is available to copy.");
    }
    private static async Task FocusAsync(InsertionTarget target,Action<string>? diagnostic=null)
    {
        Native.GetWindowThreadProcessId(target.Window, out var pid);
        diagnostic?.Invoke($"focus=before;foreground=0x{Native.GetForegroundWindow().ToInt64():X};target_valid={Native.IsWindow(target.Window)};target_pid_matches={pid==target.ProcessId}");
        if (pid != target.ProcessId) throw new InvalidOperationException("The original application is no longer available.");
        var activated=Native.SetForegroundWindow(target.Window);
        for(var attempt=0;attempt<5&&Native.GetForegroundWindow()!=target.Window;attempt++)await Task.Delay(15);
        diagnostic?.Invoke($"focus=after_restore;foreground=0x{Native.GetForegroundWindow().ToInt64():X};set_foreground={activated}");
        if (Native.GetForegroundWindow() != target.Window) throw new InvalidOperationException("Windows prevented restoring focus. Your text is available in SineFlow.");
        try { target.Element?.SetFocus(); }
        catch (InvalidOperationException) { }
        catch (ElementNotAvailableException) { }
        try { target.Selection?.Select(); }
        catch (InvalidOperationException) { throw new InvalidOperationException("The original text selection is no longer available."); }
        catch (ElementNotAvailableException) { throw new InvalidOperationException("The original text selection is no longer available."); }
        if(target.NativeSelection is {} native)NativeTextControl.Select(native);
        var focused=AutomationElement.FocusedElement;
        var sameProcess=focused is not null&&focused.Current.ProcessId==target.ProcessId;
        // Chrome exposes the focused contenteditable as a Document and may move focus to a
        // child Edit after SetFocus. Both are the same editable target; exact UIA identity is
        // too strict for those providers, but the process must still match.
        var matches=focused is not null&&target.Element is not null&&
            (Automation.Compare(focused,target.Element)||
             (target.Element.Current.ControlType==ControlType.Document&&sameProcess&&IsEditableElement(focused)));
        diagnostic?.Invoke("focus=element;matches_target="+matches+";password="+(focused?.Current.IsPassword??true));
        if(focused is null||focused.Current.IsPassword||target.Element is null||!matches)throw new InvalidOperationException("The original text field could not be restored safely. Your text is available in SineFlow.");
        // Avoid shortcuts still held by the user modifying a paste or inserted text.
        for (int i = 0; i < 8 && new[] { 16,17,18,91,92 }.Any(k => Native.GetAsyncKeyState(k) < 0); i++) await Task.Delay(15);
        if (new[] { 16,17,18,91,92 }.Any(k => Native.GetAsyncKeyState(k) < 0)) throw new InvalidOperationException("Release modifier keys before inserting text.");
    }
    private static bool HasInspectableTextPattern(InsertionTarget target)
    {
        try { return target.Element?.TryGetCurrentPattern(ValuePattern.Pattern,out _) == true || target.Element?.TryGetCurrentPattern(TextPattern.Pattern,out _) == true; }
        catch (ElementNotAvailableException) { return false; }
    }
    private static string? InspectableText(InsertionTarget target)
    {
        try
        {
            if(target.Element is null)return null;
            // Chrome and other browser contenteditables expose a Document with a
            // ValuePattern that describes the page (often its URL) and a TextPattern
            // that contains the actual editable text. Prefer TextPattern there.
            if(target.Element.Current.ControlType==ControlType.Document&&
               target.Element.TryGetCurrentPattern(TextPattern.Pattern,out var documentPattern)&&
               documentPattern is TextPattern document)
            {
                var content=document.DocumentRange.GetText(100001);
                if(content.Length<=100000)return content;
            }
            if(target.Element.TryGetCurrentPattern(ValuePattern.Pattern,out var valuePattern)&&valuePattern is ValuePattern value)
                return value.Current.Value;
            if(target.Element.TryGetCurrentPattern(TextPattern.Pattern,out var pattern)&&pattern is TextPattern text)
            {
                var content=text.DocumentRange.GetText(100001);
                return content.Length<=100000?content:null;
            }
        }
        catch(ElementNotAvailableException) { }
        return null;
    }
    private static int Occurrences(string haystack,string needle)
    {
        if(string.IsNullOrEmpty(needle))return 0;
        var count=0;var offset=0;
        while((offset=haystack.IndexOf(needle,offset,StringComparison.Ordinal))>=0){count++;offset+=needle.Length;}
        return count;
    }
    private static string NormalizeText(string value)=>value.Replace("\r\n","\n").Replace('\r','\n').Trim();
    private static string CollapseWhitespace(string value)=>string.Join(' ',value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
    internal static bool ConfirmedChange(string after,string inserted,string? before)
    {
        if(string.IsNullOrEmpty(inserted))return false;
        var normalizedAfter=NormalizeText(after);
        var normalizedInserted=NormalizeText(inserted);
        var normalizedBefore=before is null?null:NormalizeText(before);
        if(Occurrences(normalizedAfter,normalizedInserted)>(normalizedBefore is null?0:Occurrences(normalizedBefore,normalizedInserted)))return true;
        // Rich editors can normalize whitespace or line breaks. Compare once more with all
        // whitespace collapsed before declaring that the insertion did not happen.
        var compactAfter=CollapseWhitespace(normalizedAfter);
        var compactInserted=CollapseWhitespace(normalizedInserted);
        var compactBefore=normalizedBefore is null?null:CollapseWhitespace(normalizedBefore);
        return compactInserted.Length>0&&Occurrences(compactAfter,compactInserted)>(compactBefore is null?0:Occurrences(compactBefore,compactInserted));
    }
    internal static bool ContainsPartialInsertion(string after,string inserted,string? before)
    {
        var normalizedAfter=NormalizeText(after);
        var normalizedBefore=before is null?"":NormalizeText(before);
        var normalizedInserted=NormalizeText(inserted);
        if(normalizedInserted.Length==0)return false;
        var minimum=Math.Min(12,Math.Max(4,normalizedInserted.Length/3));
        for(var length=minimum;length<=normalizedInserted.Length;length++)
        {
            var prefix=normalizedInserted[..length];
            if(Occurrences(normalizedAfter,prefix)>Occurrences(normalizedBefore,prefix))return true;
        }
        return false;
    }
    private enum DeliveryConfirmation { Confirmed, Unchanged, ChangedWithoutText, Opaque }
    private static async Task<DeliveryConfirmation> ConfirmDeliveryAsync(InsertionTarget target,string text,string? beforeText,CancellationToken token)
    {
        if(target.NativeSelection is not null)return DeliveryConfirmation.Confirmed; // NativeTextControl already confirms exact replacement.
        if(target.Element is null)return DeliveryConfirmation.Opaque;
        var inspectable=false;
        string? last=null;
        for(var attempt=0;attempt<12;attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var after=InspectableText(target);
                if(after is not null)
                {
                    inspectable=true;last=after;
                    if(ConfirmedChange(after,text,beforeText))return DeliveryConfirmation.Confirmed;
                    if(beforeText is not null&&attempt>=5&&NormalizeText(after)==NormalizeText(beforeText))
                        return DeliveryConfirmation.Unchanged;
                }
            }
            catch(ElementNotAvailableException){throw new InvalidOperationException("The destination field disappeared before insertion could be verified.");}
            if(attempt<11)await Task.Delay(25,token);
        }
        if(!inspectable)return DeliveryConfirmation.Opaque;
        if(beforeText is not null&&last is not null&&NormalizeText(last)==NormalizeText(beforeText))return DeliveryConfirmation.Unchanged;
        return DeliveryConfirmation.ChangedWithoutText;
    }
    private static InsertionTarget RefineTarget(InsertionTarget target)
    {
        try
        {
            var focused=AutomationElement.FocusedElement;
            if(focused is null||focused.Current.IsPassword||focused.Current.ProcessId!=target.ProcessId)return target;
            if(target.Element is not null&&Automation.Compare(focused,target.Element))return target;
            if(!IsEditableElement(focused))return target;
            TextPatternRange? selection=null;
            try
            {
                if(focused.TryGetCurrentPattern(TextPattern.Pattern,out var pattern)&&pattern is TextPattern text)
                    selection=text.GetSelection().FirstOrDefault()?.Clone();
            }
            catch(InvalidOperationException) { }
            return target with { Element=focused, Selection=selection, NativeSelection=NativeTextControl.Capture(focused) };
        }
        catch(ElementNotAvailableException) { return target; }
    }
    public void Dispose() => probe.Dispose();
}
