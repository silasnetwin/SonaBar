using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;
using SonaBar.Core;

namespace SonaBar.WindowsIntegration;

public sealed record InsertionTarget(nint Window, uint ProcessId, string Executable, AutomationElement? Element, TextPatternRange? Selection = null,string WindowTitle="",NativeTextSnapshot? NativeSelection=null,string ElementName="",string ElementClass="",string ElementControlType="");
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
        throw new InvalidOperationException("Windows kept the clipboard busy, so SonaBar could not safely restore it.",last);
    }
}
public sealed class TextInsertionService(ClipboardService clipboard) : IDisposable
{
    private readonly AutomationProbe probe = new();
    private nint lastExternalWindow;
    private bool restoreCaretOnInsert;
    /// <summary>
    /// Runs an inspection on the interface thread. Set by the application at start-up. It is
    /// needed for fields inside SonaBar itself, because Windows has to call back into this
    /// process to describe them and the helper thread would wait for the interface thread,
    /// which in turn waits for the helper thread.
    /// </summary>
    public static Func<Func<InsertionTarget>, InsertionTarget>? InterfaceThread { get; set; }
    /// <summary>
    /// Remembers the last foreground window that does not belong to SonaBar. The app calls
    /// this from its foreground tracker and before it activates its own window.
    /// </summary>
    public void RememberForegroundWindow()
    {
        var hwnd=Native.GetForegroundWindow();
        if(hwnd==0||!Native.IsWindow(hwnd)||Native.GetWindowTextLength(hwnd)==0||IsOwnWindow(hwnd))return;
        lastExternalWindow=hwnd;
    }
    /// <summary>
    /// When SonaBar is foreground, restore the last external window before capturing the
    /// target. This is what makes the Start button in the app work for the app behind it.
    /// If the remembered window is gone, walk the Z-order for the next visible window.
    /// </summary>
    public void PreferExternalTarget(bool restoreCaretOnInsert=false)
    {
        var foreground=Native.GetForegroundWindow();
        if(!IsOwnWindow(foreground))return;
        if(TryActivateWindow(lastExternalWindow))
        {
            this.restoreCaretOnInsert=restoreCaretOnInsert;
            return;
        }
        var candidate=Native.GetWindow(foreground,2);
        for(var i=0;i<24&&candidate!=0;i++)
        {
            if(Native.IsWindowVisible(candidate)&&Native.GetWindowTextLength(candidate)>0&&TryActivateWindow(candidate))
            {
                lastExternalWindow=candidate;
                this.restoreCaretOnInsert=restoreCaretOnInsert;
                return;
            }
            candidate=Native.GetWindow(candidate,2);
        }
    }
    /// <summary>Drops a pending caret restore when the dictation never reaches delivery.</summary>
    public void CancelCaretRestore()=>restoreCaretOnInsert=false;
    private static bool TryActivateWindow(nint hwnd)
    {
        if(hwnd==0||!Native.IsWindow(hwnd)||IsOwnWindow(hwnd))return false;
        Native.GetWindowThreadProcessId(hwnd,out var pid);
        if(pid==(uint)Environment.ProcessId)return false;
        return Native.ForceForegroundWindow(hwnd);
    }
    /// <summary>How long Windows may take to describe the focused field before we give up.</summary>
    public static readonly TimeSpan CaptureTimeout = TimeSpan.FromMilliseconds(900);
    public InsertionReceipt? LastReceipt {get;private set;}
    /// <summary>True when the paste was sent to an opaque target that Windows cannot read back.</summary>
    public bool LastDeliveryAssumed {get;private set;}
    public string LastDeliveryStatus {get;private set;} = "Text inserted";
    public void ForgetReceipt()=>LastReceipt=null;
    /// <summary>
    /// Reads the focused field. The inspection runs on a dedicated automation thread with a
    /// hard limit, because a busy target application can otherwise freeze the whole program.
    /// </summary>
    public InsertionTarget CaptureTarget()
    {
        // A field inside SonaBar is inspected on the interface thread: Windows describes it
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
    /// <summary>
    /// Finds the focused edit provider inside a specific top-level window. Chrome can report
    /// the page Document as focused while AutomationElement.FocusedElement still returns a
    /// stale element from another process, so the window tree is the reliable source.
    /// </summary>
    private static AutomationElement? FocusedElementForWindow(nint hwnd,uint processId)
    {
        try
        {
            var window=AutomationElement.FromHandle(hwnd);
            if(window is not null)
            {
                var condition=new AndCondition(
                    new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Document)),
                    new PropertyCondition(AutomationElement.HasKeyboardFocusProperty,true),
                    new PropertyCondition(AutomationElement.IsEnabledProperty,true),
                    new PropertyCondition(AutomationElement.IsPasswordProperty,false));
                var focused=window.FindFirst(TreeScope.Descendants,condition);
                if(focused is not null)return IsBrowserChrome(focused)?null:focused;
                // Some Chromium builds expose the contenteditable as an Edit but leave
                // HasKeyboardFocus false on every descendant. Prefer an actual Edit over
                // the page Document so the message box is still found.
                var editable=FindEditableCandidate(window,ControlType.Edit)??FindEditableCandidate(window,ControlType.Document);
                if(editable is not null)return editable;
            }
        }
        catch(ElementNotAvailableException) { }
        catch(InvalidOperationException) { }
        catch(ArgumentException) { }
        try
        {
            var global=AutomationElement.FocusedElement;
            if(global is not null&&global.Current.ProcessId==processId)return global;
        }
        catch(ElementNotAvailableException) { }
        return null;
    }
    private static AutomationElement? FindEditableCandidate(AutomationElement window,ControlType type)
    {
        try
        {
            var condition=new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,type),
                new PropertyCondition(AutomationElement.IsEnabledProperty,true),
                new PropertyCondition(AutomationElement.IsPasswordProperty,false),
                new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty,true));
            var candidates=window.FindAll(TreeScope.Descendants,condition);
            for(var index=0;index<candidates.Count;index++)
            {
                var candidate=candidates[index];
                if(IsBrowserChrome(candidate))continue;
                if(IsEditableElement(candidate))return candidate;
            }
        }
        catch(ElementNotAvailableException) { }
        catch(InvalidOperationException) { }
        return null;
    }
    private static (string Name,string Class,string Type) ElementIdentity(AutomationElement? element)
    {
        if(element is null)return ("","","");
        try { return (element.Current.Name,element.Current.ClassName,element.Current.ControlType.ProgrammaticName); }
        catch(ElementNotAvailableException) { return ("","",""); }
        catch(InvalidOperationException) { return ("","",""); }
    }
    private static InsertionTarget WithElement(InsertionTarget target,AutomationElement element)
    {
        TextPatternRange? selection=null;
        try
        {
            if(element.TryGetCurrentPattern(TextPattern.Pattern,out var pattern)&&pattern is TextPattern text)
                selection=text.GetSelection().FirstOrDefault()?.Clone();
        }
        catch(InvalidOperationException) { }
        catch(ElementNotAvailableException) { }
        var identity=ElementIdentity(element);
        return target with
        {
            Element=element,
            Selection=selection,
            NativeSelection=NativeTextControl.Capture(element),
            ElementName=identity.Name,
            ElementClass=identity.Class,
            ElementControlType=identity.Type
        };
    }
    /// <summary>
    /// Finds a fresh provider for a target captured before a slow transcription. Chromium
    /// recreates accessibility nodes when focus moves; the old AutomationElement can be
    /// stale even though the same composer is still visible.
    /// </summary>
    private static AutomationElement? FindTargetElement(InsertionTarget target)
    {
        if(target.Window==0||!Native.IsWindow(target.Window))return null;
        try
        {
            var window=AutomationElement.FromHandle(target.Window);
            if(window is null)return null;
            var conditions=new List<Condition>
            {
                new PropertyCondition(AutomationElement.IsEnabledProperty,true),
                new PropertyCondition(AutomationElement.IsPasswordProperty,false)
            };
            var type=ControlTypeFromName(target.ElementControlType);
            if(type is not null)conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty,type));
            if(!string.IsNullOrEmpty(target.ElementName))
                conditions.Add(new PropertyCondition(AutomationElement.NameProperty,target.ElementName));
            else if(!string.IsNullOrEmpty(target.ElementClass))
                conditions.Add(new PropertyCondition(AutomationElement.ClassNameProperty,target.ElementClass));
            var found=window.FindFirst(TreeScope.Descendants,new AndCondition(conditions.ToArray()));
            if(found is not null&&!IsBrowserChrome(found)&&IsEditableElement(found))return found;
            if(!string.IsNullOrEmpty(target.ElementName)&&!string.IsNullOrEmpty(target.ElementClass))
            {
                var classConditions=new List<Condition>
                {
                    new PropertyCondition(AutomationElement.IsEnabledProperty,true),
                    new PropertyCondition(AutomationElement.IsPasswordProperty,false),
                    new PropertyCondition(AutomationElement.ClassNameProperty,target.ElementClass)
                };
                if(type is not null)classConditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty,type));
                found=window.FindFirst(TreeScope.Descendants,new AndCondition(classConditions.ToArray()));
                if(found is not null&&!IsBrowserChrome(found)&&IsEditableElement(found))return found;
            }
        }
        catch(ElementNotAvailableException) { }
        catch(InvalidOperationException) { }
        catch(ArgumentException) { }
        return null;
    }
    private static ControlType? ControlTypeFromName(string name)
    {
        if(name.EndsWith(".Edit",StringComparison.Ordinal))return ControlType.Edit;
        if(name.EndsWith(".Document",StringComparison.Ordinal))return ControlType.Document;
        return null;
    }
    private static bool MatchesTarget(InsertionTarget target,AutomationElement? focused)
    {
        if(focused is null)return false;
        try
        {
            if(IsBrowserChrome(focused))return false;
            if(target.Element is not null&&Automation.Compare(focused,target.Element))return true;
            if(focused.Current.ProcessId!=target.ProcessId)return false;
            if(!IsEditableElement(focused))return false;
            if(!string.IsNullOrEmpty(target.ElementName)&&focused.Current.Name==target.ElementName)return true;
            if(!string.IsNullOrEmpty(target.ElementClass)&&focused.Current.ClassName==target.ElementClass)return true;
            if(target.Element is not null&&target.Element.Current.ControlType==ControlType.Document)return true;
        }
        catch(ElementNotAvailableException) { }
        catch(InvalidOperationException) { }
        return false;
    }
    private InsertionTarget CaptureTargetCore()
    {
        var hwnd = Native.GetForegroundWindow(); var thread = Native.GetWindowThreadProcessId(hwnd, out var pid);
        string name;
        try { using var process = Process.GetProcessById((int)pid); name = process.ProcessName + ".exe"; } catch { name = "Unknown"; }
        AutomationElement? element = FocusedElementForWindow(hwnd,pid);
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
        var identity=ElementIdentity(element);
        // Opaque game windows such as Roblox may expose no AutomationElement at all.
        // Capture still succeeds so the Compatibility profile can paste into the
        // foreground window; callers that need UIA (selection editing) still Guard.
        return new InsertionTarget(hwnd, pid, name, element, selection,title,NativeTextControl.Capture(element),identity.Name,identity.Class,identity.Type);
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
            if (target.Element.Current.IsPassword) throw new InvalidOperationException("SonaBar never reads or types into password fields.");
            if(!target.Element.Current.IsEnabled)throw new InvalidOperationException("This text field is disabled.");
        }
        catch(ElementNotAvailableException)
        {
            throw new InvalidOperationException("The original text field is no longer available. Your text is available in SonaBar.");
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
        // Chromium can expose the page-level selection through the contenteditable's
        // TextPattern. That is not a passage the user selected inside the field, and treating
        // it as one would open the selection editor instead of starting dictation.
        if(target.NativeSelection is null)
        {
            var content=InspectableText(target);
            if(!SelectionBelongsToContent(text,content))return null;
        }
        return new(target,text);
    }
    internal static bool SelectionBelongsToContent(string text,string? content)=>content is not null&&content.Contains(text,StringComparison.Ordinal);
    public static void ValidateSelection(SelectedPassage passage)
    {
        if(!IsEditable(passage.Target))throw new InvalidOperationException("The original selection is no longer editable.");
        if(passage.Target.NativeSelection is {} native)NativeTextControl.Validate(native);
        else if(passage.Target.Selection?.GetText(14001)!=passage.Text)throw new InvalidOperationException("The selected passage changed. Select it again to edit safely.");
    }
    public async Task ReplaceSelectionAsync(SelectedPassage passage,string text,ClipboardMode mode,CancellationToken token)
    {
        ValidateSelection(passage);
        var focused=await FocusAsync(passage.Target);
        if(focused.Element is null||passage.Target.Element is null||!Automation.Compare(focused.Element,passage.Target.Element))
            throw new InvalidOperationException("The selected passage changed. Select it again to edit safely.");
        ValidateSelection(passage);token.ThrowIfCancellationRequested();
        await InsertAsync(focused,text,new(focused.Executable,InsertionStrategy.Automatic,false,""),mode,token);
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
        await EnsureInputFocusAsync(target);
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
        LastDeliveryAssumed=false;
        LastDeliveryStatus="Text inserted";
        var restoreCaret=restoreCaretOnInsert;
        restoreCaretOnInsert=false;
        token.ThrowIfCancellationRequested();
        diagnostic?.Invoke("delivery=start;"+DescribeTarget(target));
        if (!Native.IsWindow(target.Window))
        {
            clipboard.SetText(text);
            diagnostic?.Invoke("delivery=failed;reason=target_window_missing;fallback=copy_only");
            throw new InvalidOperationException("The original application closed before text could be delivered. Your text is ready to copy.");
        }
        // Compatibility is deliberately clipboard-only: game overlays may expose a
        // generic AutomationElement that cannot be focused or read back reliably.
        if (profile.Strategy == InsertionStrategy.Compatibility)
            return await PasteToOpaqueTargetAsync(target,text,profile,mode,token,phase,diagnostic);
        if (target.Element is null)
        {
            clipboard.SetText(text);
            diagnostic?.Invoke("delivery=failed;reason=target_not_inspectable;fallback=copy_only");
            throw new InvalidOperationException("Windows could not inspect this field safely. Focus an editable field and try again.");
        }
        Guard(target);
        try
        {
            var initialSelection=target.Selection?.GetText(1)??"";
            diagnostic?.Invoke($"selection=initial;length={initialSelection.Length}");
        }
        catch (InvalidOperationException) { diagnostic?.Invoke("selection=initial;unavailable"); }
        catch (ElementNotAvailableException) { diagnostic?.Invoke("selection=initial;unavailable"); }
        target=await FocusAsync(target,diagnostic,restoreCaret); phase?.Invoke("T9"); token.ThrowIfCancellationRequested();
        var current = FocusedElementForWindow(target.Window,target.ProcessId);
        if (current is null || current.Current.IsPassword)
        {
            diagnostic?.Invoke("delivery=failed;reason=focus_not_safe");
            throw new InvalidOperationException("Insertion stopped: the current field cannot be safely inspected.");
        }
        target=RefineTarget(target);
        Guard(target);
        var beforeText=InspectableText(target);
        // Rich composers (Discord/Slate, Electron editors, browser contenteditables) can
        // drop or reorder characters when a long SendInput batch arrives. A clipboard
        // paste is atomic there and avoids leaving a half-inserted sentence behind.
        var preferClipboard=profile.Strategy==InsertionStrategy.Automatic&&PreferClipboardForTarget(target);
        var partialDetected=false;
        // ValuePattern replaces the whole value, so only use it for an empty field.
        bool inserted=false,wholeEmptyField=false,assumed=false;
        var replaced="";
        try { replaced=target.Selection?.GetText(-1)??""; }
        catch (InvalidOperationException) { diagnostic?.Invoke("selection=current;unavailable"); }
        catch (ElementNotAvailableException) { diagnostic?.Invoke("selection=current;unavailable"); }
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
        if (!inserted&&!preferClipboard&&profile.Strategy == InsertionStrategy.Automatic && target.Element!.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && value is ValuePattern vp && !vp.Current.IsReadOnly && vp.Current.Value.Length == 0)
        {
            diagnostic?.Invoke("backend=uia_value;attempted=true");
            try
            {
                vp.SetValue(text);
                // Chromium updates ValuePattern asynchronously. Reading it immediately
                // reports the old value and a later SendInput fallback would insert the
                // text twice. Wait for the provider, then accept a visible text change.
                var confirmed=false;
                for(var attempt=0;attempt<12;attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    if(vp.Current.Value==text){confirmed=true;break;}
                    var after=InspectableText(target);
                    if(after is not null&&ConfirmedChange(after,text,beforeText)){confirmed=true;break;}
                    await Task.Delay(25,token);
                }
                if(!confirmed)
                {
                    var after=InspectableText(target);
                    if(after is null||beforeText is null||NormalizeText(after)==NormalizeText(beforeText))
                        throw new InvalidOperationException("Windows did not confirm that the text was inserted.");
                    diagnostic?.Invoke("backend=uia_value;result=changed_unverified");
                }
                else diagnostic?.Invoke("backend=uia_value;result=verified_exact");
                inserted=true;wholeEmptyField=true;
            }
            catch(Exception error) when (error is not OperationCanceledException)
            {
                diagnostic?.Invoke($"backend=uia_value;result=not_confirmed;exception={error.GetType().Name};fallback=continue");
            }
        }
        if (!inserted && !preferClipboard && (profile.Strategy == InsertionStrategy.Unicode||profile.Strategy==InsertionStrategy.Automatic&&text.Length<=2000&&IsEditable(target)&&HasInspectableTextPattern(target)))
        {
            diagnostic?.Invoke("backend=sendinput_unicode;attempted=true");
            await EnsureInputFocusAsync(target,diagnostic,restoreCaret);
            await Native.UnicodeAsync(text,token);
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
                    var coverage=after is null?0:InsertedCoverage(after,text,beforeText);
                    if(coverage>0)
                    {
                        partialDetected=true;
                        diagnostic?.Invoke($"backend=sendinput_unicode;partial=detected;coverage={coverage};length={text.Length};action={(string.IsNullOrEmpty(beforeText)?"select_all_paste":"copy_only")}");
                    }
                }
            }
        }
        if (partialDetected&&!string.IsNullOrEmpty(beforeText))
        {
            // We can replace a partial insertion only when the field was empty. In a field
            // that already had user text, pasting again would duplicate the visible prefix,
            // so stop and keep the transcript available for a manual copy.
            diagnostic?.Invoke("delivery=failed;reason=partial_insertion;fallback=copy_only");
            throw new InvalidOperationException("SonaBar inserted part of the text but could not verify the complete result. Your text is available to copy.");
        }
        if (!inserted)
        {
            var reason=preferClipboard?";reason=composer_target":partialDetected?";reason=partial_sendinput":"";
            diagnostic?.Invoke("backend=clipboard_paste;attempted=true;preserve="+(mode==ClipboardMode.Preserve)+reason);
            var previous = mode == ClipboardMode.Preserve ? clipboard.Capture() : null;
            var sequence = clipboard.SetText(text);
            try
            {
                await EnsureInputFocusAsync(target,diagnostic,restoreCaret);
                if(partialDetected)
                {
                    // SendInput left a prefix in the field. Select it and let the paste
                    // replace the partial text instead of appending a second copy.
                    diagnostic?.Invoke("backend=clipboard_paste;recovery=select_all");
                    Native.Chord(0x41);
                    await Task.Delay(60,token);
                }
                Native.Chord(0x56);
                var confirmation=await ConfirmDeliveryAsync(target,text,beforeText,token);
                if(confirmation==DeliveryConfirmation.Confirmed)
                {
                    inserted=true;
                    diagnostic?.Invoke("backend=clipboard_paste;result=verified");
                }
                else if(confirmation==DeliveryConfirmation.ChangedWithoutText)
                {
                    var after=InspectableText(target);
                    var coverage=after is null?0:InsertedCoverage(after,text,beforeText);
                    if(CanAssumeChangedClipboardDelivery(IsEditable(target),coverage,text))
                    {
                        // Rich editors often normalize whitespace, punctuation or line
                        // breaks while pasting. A long visible prefix means the paste
                        // landed; reporting a failure here would only invite a duplicate.
                        inserted=true;assumed=true;
                        diagnostic?.Invoke($"backend=clipboard_paste;result=changed_assumed;coverage={coverage};length={text.Length}");
                    }
                    else
                    {
                        diagnostic?.Invoke($"backend=clipboard_paste;result=changed_unverified;coverage={coverage};reason=not_confirmed");
                        throw new InvalidOperationException("SonaBar could not confirm that the destination accepted the text. Your text is ready to copy.");
                    }
                }
                else if(confirmation==DeliveryConfirmation.Opaque)
                {
                    // Some editors and game overlays expose no readable text at all. The
                    // paste shortcut was delivered to the focused window; failing here would
                    // only report a false error after a successful paste. Compatibility is
                    // opt-in for opaque game windows, while editable fields keep the
                    // existing automatic behavior.
                    var editable=IsEditable(target);
                    if(!CanAssumeOpaqueDelivery(profile.Strategy,editable))
                    {
                        diagnostic?.Invoke("backend=clipboard_paste;result=opaque;reason=not_confirmed");
                        throw new InvalidOperationException("SonaBar could not confirm that the destination accepted the text. Your text is ready to copy.");
                    }
                    inserted=true;
                    assumed=!editable;
                    diagnostic?.Invoke(assumed
                        ? "backend=clipboard_paste;result=assumed;reason=opaque_game_target"
                        : "backend=clipboard_paste;result=assumed;reason=opaque_editable_target");
                }
                else
                {
                    diagnostic?.Invoke($"backend=clipboard_paste;result={confirmation.ToString().ToLowerInvariant()};reason=not_confirmed");
                    throw new InvalidOperationException("SonaBar could not confirm that the destination accepted the text. Your text is ready to copy.");
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
        LastDeliveryAssumed=assumed;
        LastDeliveryStatus=assumed?"Paste sent — check the app/target":"Text inserted";
        diagnostic?.Invoke(assumed?"delivery=assumed":"delivery=confirmed");
        return LastDeliveryStatus;
    }
    private async Task<string> PasteToOpaqueTargetAsync(InsertionTarget target,string text,ApplicationProfile profile,ClipboardMode mode,CancellationToken token,Action<string>? phase,Action<string>? diagnostic)
    {
        diagnostic?.Invoke("backend=clipboard_paste;attempted=true;preserve="+(mode==ClipboardMode.Preserve)+";reason=no_uia_target");
        await FocusWindowAsync(target,diagnostic); phase?.Invoke("T9"); token.ThrowIfCancellationRequested();
        var previous=mode==ClipboardMode.Preserve?clipboard.Capture():null;
        var sequence=clipboard.SetText(text);
        try
        {
            await EnsureInputFocusAsync(target,diagnostic);
            Native.Chord(0x56);
            // The target reads the clipboard asynchronously. Keep the text there long
            // enough to be consumed before restoring the user's previous clipboard.
            await Task.Delay(300,token);
        }
        finally { if(previous is not null) clipboard.RestoreIfUnchanged(previous,sequence); }
        if(profile.PressEnter){token.ThrowIfCancellationRequested();Native.SendKey(13);Native.SendKey(13,true);}
        LastDeliveryAssumed=true;
        LastDeliveryStatus="Paste sent — check the app/target";
        diagnostic?.Invoke("backend=clipboard_paste;result=assumed;reason=opaque_target_without_uia");
        return LastDeliveryStatus;
    }
    /// <summary>
    /// A paste can be assumed only for an editable field or when the user explicitly chose
    /// the compatibility profile for an opaque game window.
    /// </summary>
    internal static bool CanAssumeOpaqueDelivery(InsertionStrategy strategy,bool editable) =>
        editable||strategy==InsertionStrategy.Compatibility;
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
    private static async Task FocusWindowAsync(InsertionTarget target,Action<string>? diagnostic=null)
    {
        Native.GetWindowThreadProcessId(target.Window, out var pid);
        diagnostic?.Invoke($"focus=before;foreground=0x{Native.GetForegroundWindow().ToInt64():X};target_valid={Native.IsWindow(target.Window)};target_pid_matches={pid==target.ProcessId}");
        if (pid != target.ProcessId) throw new InvalidOperationException("The original application is no longer available.");
        var activated=false;
        for(var attempt=0;attempt<3;attempt++)
        {
            activated=Native.ForceForegroundWindow(target.Window);
            for(var wait=0;wait<6&&Native.GetForegroundWindow()!=target.Window;wait++)await Task.Delay(20);
            if(Native.GetForegroundWindow()==target.Window)break;
            await Task.Delay(40);
        }
        diagnostic?.Invoke($"focus=after_restore;foreground=0x{Native.GetForegroundWindow().ToInt64():X};set_foreground={activated}");
        if (Native.GetForegroundWindow() != target.Window) throw new InvalidOperationException("Windows prevented restoring focus. Your text is available in SonaBar.");
        // Avoid shortcuts still held by the user modifying a paste or inserted text.
        for (int i = 0; i < 8 && new[] { 16,17,18,91,92 }.Any(k => Native.GetAsyncKeyState(k) < 0); i++) await Task.Delay(15);
        if (new[] { 16,17,18,91,92 }.Any(k => Native.GetAsyncKeyState(k) < 0)) throw new InvalidOperationException("Release modifier keys before inserting text.");
    }
    /// <summary>
    /// Chromium keeps accessibility focus on a contenteditable after its top-level window was
    /// backgrounded, but the DOM focus can remain on the page body. SetFocus alone does not
    /// repair that. A click at the stored caret (or the middle of an empty field) restores the
    /// DOM focus without moving the insertion point.
    /// </summary>
    private static async Task RestoreCaretFocusAsync(InsertionTarget target,Action<string>? diagnostic=null)
    {
        if(target.Element is null||target.NativeSelection is not null)return;
        try
        {
            var content=InspectableText(target);
            double x=0,y=0;
            var hasPoint=false;
            var range=target.Selection;
            if(range is not null)
            {
                var selected=range.GetText(1);
                if(selected.Length>0)
                {
                    diagnostic?.Invoke("focus=caret_click_skipped;reason=selection");
                    return;
                }
                var rects=range.GetBoundingRectangles();
                if(rects.Length>0)
                {
                    var caret=rects[^1];
                    if(double.IsFinite(caret.X)&&double.IsFinite(caret.Y)&&caret.Width>=0&&caret.Height>0)
                    {
                        x=caret.X+Math.Max(1,caret.Width)/2;
                        y=caret.Y+caret.Height/2;
                        hasPoint=true;
                    }
                }
            }
            if(!hasPoint&&string.IsNullOrWhiteSpace(content))
            {
                var bounds=target.Element.Current.BoundingRectangle;
                if(bounds.Width>1&&bounds.Height>1)
                {
                    x=bounds.Left+bounds.Width/2;
                    y=bounds.Top+bounds.Height/2;
                    hasPoint=true;
                }
            }
            if(!hasPoint)
            {
                diagnostic?.Invoke("focus=caret_click_skipped;reason=no_geometry");
                return;
            }
            var pointX=(int)Math.Round(x);
            var pointY=(int)Math.Round(y);
            var under=Native.RootWindow(Native.WindowAt(pointX,pointY));
            if(under!=target.Window)
            {
                diagnostic?.Invoke($"focus=caret_click_skipped;reason=covered;under=0x{under.ToInt64():X}");
                return;
            }
            Native.ClickAt(pointX,pointY);
            await Task.Delay(45);
            diagnostic?.Invoke($"focus=caret_click;x={pointX};y={pointY}");
        }
        catch(ElementNotAvailableException error)
        {
            diagnostic?.Invoke("focus=caret_click_skipped;exception="+error.GetType().Name);
        }
        catch(InvalidOperationException error)
        {
            diagnostic?.Invoke("focus=caret_click_skipped;exception="+error.GetType().Name);
        }
    }
    private static async Task<InsertionTarget> FocusAsync(InsertionTarget target,Action<string>? diagnostic=null,bool restoreCaret=false)
    {
        await FocusWindowAsync(target,diagnostic);
        if(target.Element is null)throw new InvalidOperationException("The original text field could not be restored safely. Your text is available in SonaBar.");
        var resolved=target;
        try { resolved.Element!.SetFocus(); }
        catch (InvalidOperationException error) { diagnostic?.Invoke("focus=set_focus_failed;exception="+error.GetType().Name); }
        catch (ElementNotAvailableException error) { diagnostic?.Invoke("focus=set_focus_failed;exception="+error.GetType().Name); }
        var selectionLost=false;
        try { resolved.Selection?.Select(); }
        catch (InvalidOperationException error) { selectionLost=true; diagnostic?.Invoke("focus=selection_restore_failed;exception="+error.GetType().Name); }
        catch (ElementNotAvailableException error) { selectionLost=true; diagnostic?.Invoke("focus=selection_restore_failed;exception="+error.GetType().Name); }
        if(resolved.NativeSelection is {} native)
        {
            try { NativeTextControl.Select(native); }
            catch (Exception error) when (error is InvalidOperationException or ElementNotAvailableException)
            {
                selectionLost=true;
                diagnostic?.Invoke("focus=native_selection_restore_failed;exception="+error.GetType().Name);
            }
        }
        if(selectionLost)
        {
            // A selection captured before a long transcription can expire while the target
            // window is still healthy. Reacquire the same field and continue at its current
            // caret instead of failing the whole dictation. Replacement paths validate the
            // passage separately and will still refuse an unsafe edit.
            var fresh=FindTargetElement(resolved);
            if(fresh is not null)
            {
                resolved=WithElement(resolved,fresh);
                try { resolved.Element!.SetFocus(); }
                catch (InvalidOperationException error) { diagnostic?.Invoke("focus=selection_reacquire_set_focus_failed;exception="+error.GetType().Name); }
                catch (ElementNotAvailableException error) { diagnostic?.Invoke("focus=selection_reacquire_set_focus_failed;exception="+error.GetType().Name); }
                diagnostic?.Invoke("focus=selection_reacquired;name="+ElementIdentity(fresh).Name);
            }
            else diagnostic?.Invoke("focus=selection_reacquire_missing");
        }
        if(restoreCaret)await RestoreCaretFocusAsync(resolved,diagnostic);
        AutomationElement? focused=null;
        var matches=false;
        // Chromium needs a moment to move accessibility focus after activation and
        // SetFocus. Reading once immediately can return the page Document and reject a
        // perfectly valid composer. It can also recreate the accessibility node while
        // dictation is being transcribed, so try the same named field again before
        // giving up. Exact UIA identity is too strict for browser contenteditables.
        for(var attempt=0;attempt<6;attempt++)
        {
            focused=FocusedElementForWindow(resolved.Window,resolved.ProcessId);
            matches=MatchesTarget(resolved,focused);
            if(matches)break;
            if(attempt is 1 or 3)
            {
                var fresh=FindTargetElement(resolved);
                if(fresh is not null&&!Automation.Compare(fresh,resolved.Element))
                {
                    diagnostic?.Invoke("focus=reacquired;name="+ElementIdentity(fresh).Name);
                    resolved=WithElement(resolved,fresh);
                    try { resolved.Element!.SetFocus(); }
                    catch (InvalidOperationException error) { diagnostic?.Invoke("focus=reacquire_set_focus_failed;exception="+error.GetType().Name); }
                    catch (ElementNotAvailableException error) { diagnostic?.Invoke("focus=reacquire_set_focus_failed;exception="+error.GetType().Name); }
                    try { resolved.Selection?.Select(); }
                    catch (InvalidOperationException) { }
                    catch (ElementNotAvailableException) { }
                    if(resolved.NativeSelection is {} freshNative)NativeTextControl.Select(freshNative);
                }
            }
            if(attempt<5)await Task.Delay(35);
        }
        // Some providers replace the element without preserving a usable name or class.
        // Accept a verified editable in the same process as a last resort, but never the
        // browser address bar or a password field.
        if(!matches&&focused is not null)
        {
            try
            {
                if(focused.Current.ProcessId==resolved.ProcessId&&!focused.Current.IsPassword&&!IsBrowserChrome(focused)&&IsEditableElement(focused))
                {
                    resolved=WithElement(resolved,focused);
                    matches=true;
                    diagnostic?.Invoke("focus=accepted_editable");
                }
            }
            catch(ElementNotAvailableException) { }
            catch(InvalidOperationException) { }
        }
        var targetIdentity=ElementIdentity(resolved.Element);
        var focusedIdentity=ElementIdentity(focused);
        var password=true;
        try { password=focused?.Current.IsPassword??true; }
        catch(ElementNotAvailableException) { }
        catch(InvalidOperationException) { }
        diagnostic?.Invoke($"focus=element;matches_target={matches};password={password};target={targetIdentity.Type};name={targetIdentity.Name};focused={focusedIdentity.Type};name={focusedIdentity.Name};class={focusedIdentity.Class}");
        if(focused is null||password||!matches)throw new InvalidOperationException("The original text field could not be restored safely. Your text is available in SonaBar.");
        return resolved;
    }
    /// <summary>
    /// Verifies foreground and keyboard focus immediately before synthetic input. A visible
    /// SonaBar window can otherwise become active again after the initial restore and receive
    /// the dictated text in one of its own fields.
    /// </summary>
    private static async Task EnsureInputFocusAsync(InsertionTarget target,Action<string>? diagnostic=null,bool restoreCaret=false)
    {
        static (nint Foreground,nint Focus,nint Root) State()
        {
            var foreground=Native.GetForegroundWindow();
            var focus=Native.FocusedWindow(foreground);
            return (foreground,focus,focus==0?0:Native.RootWindow(focus));
        }
        var state=State();
        var ok=state.Foreground==target.Window&&(state.Focus==0||state.Root==target.Window);
        diagnostic?.Invoke($"focus=final;foreground=0x{state.Foreground.ToInt64():X};focus=0x{state.Focus.ToInt64():X};root=0x{state.Root.ToInt64():X};target=0x{target.Window.ToInt64():X};ok={ok}");
        if(ok)return;
        for(var attempt=0;attempt<3;attempt++)
        {
            await FocusWindowAsync(target,diagnostic);
            if(target.Element is not null)
            {
                try { target.Element.SetFocus(); }
                catch(InvalidOperationException error) { diagnostic?.Invoke("focus=final_set_focus_failed;exception="+error.GetType().Name); }
                catch(ElementNotAvailableException error) { diagnostic?.Invoke("focus=final_set_focus_failed;exception="+error.GetType().Name); }
                // A slow transcription can leave the provider behind the old element.
                // Reacquire the editable node before trying the final focus check again.
                var fresh=FindTargetElement(target);
                if(fresh is not null)
                {
                    target=WithElement(target,fresh);
                    try { target.Element!.SetFocus(); }
                    catch(InvalidOperationException error) { diagnostic?.Invoke("focus=final_reacquire_set_focus_failed;exception="+error.GetType().Name); }
                    catch(ElementNotAvailableException error) { diagnostic?.Invoke("focus=final_reacquire_set_focus_failed;exception="+error.GetType().Name); }
                    diagnostic?.Invoke("focus=final_reacquired;name="+ElementIdentity(fresh).Name);
                }
            }
            if(restoreCaret&&attempt==0)await RestoreCaretFocusAsync(target,diagnostic);
            await Task.Delay(35);
            state=State();
            ok=state.Foreground==target.Window&&(state.Focus==0||state.Root==target.Window);
            diagnostic?.Invoke($"focus=final_after_restore;attempt={attempt};foreground=0x{state.Foreground.ToInt64():X};focus=0x{state.Focus.ToInt64():X};root=0x{state.Root.ToInt64():X};target=0x{target.Window.ToInt64():X};ok={ok}");
            if(ok)return;
            await Task.Delay(35);
        }
        throw new InvalidOperationException("Windows prevented restoring focus. Your text is available in SonaBar.");
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
    internal static bool ContainsPartialInsertion(string after,string inserted,string? before)=>
        InsertedCoverage(after,inserted,before)>=MinimumCoverage(inserted);
    internal static int MinimumCoverage(string inserted)
    {
        var length=NormalizeText(inserted).Length;
        return Math.Min(12,Math.Max(4,length/3));
    }
    /// <summary>
    /// Returns the longest prefix of the dictated text that is newly visible in the
    /// target. Rich editors can normalize whitespace or punctuation, so the full text
    /// may not compare equal even though most of it arrived.
    /// </summary>
    internal static int InsertedCoverage(string after,string inserted,string? before)
    {
        var normalizedAfter=NormalizeText(after);
        var normalizedBefore=before is null?"":NormalizeText(before);
        var normalizedInserted=NormalizeText(inserted);
        if(normalizedInserted.Length==0)return 0;
        var best=Coverage(normalizedAfter,normalizedBefore,normalizedInserted);
        var compactAfter=CollapseWhitespace(normalizedAfter);
        var compactBefore=CollapseWhitespace(normalizedBefore);
        var compactInserted=CollapseWhitespace(normalizedInserted);
        if(compactInserted.Length>0)best=Math.Max(best,Coverage(compactAfter,compactBefore,compactInserted));
        return best;
        static int Coverage(string after,string before,string inserted)
        {
            var best=0;
            for(var length=1;length<=inserted.Length;length++)
            {
                var prefix=inserted[..length];
                if(Occurrences(after,prefix)>Occurrences(before,prefix))best=length;
            }
            return best;
        }
    }
    internal static bool CanAssumeChangedClipboardDelivery(bool editable,int coverage,string inserted)=>
        editable&&coverage>=MinimumCoverage(inserted);
    internal static bool PreferClipboardForTarget(InsertionTarget target)
    {
        var className=target.ElementClass;
        if(string.IsNullOrEmpty(className))
        {
            try { className=target.Element?.Current.ClassName??""; }
            catch(ElementNotAvailableException) { }
        }
        return PreferClipboardForTarget(target.Executable,className);
    }
    /// <summary>
    /// Electron, chat and rich-text composers handle a single clipboard paste more
    /// reliably than a long SendInput Unicode batch. Native edit controls keep the
    /// faster Unicode path.
    /// </summary>
    internal static bool PreferClipboardForTarget(string executable,string className)
    {
        var name=System.IO.Path.GetFileName(executable);
        if(ComposerExecutables.Contains(name))return true;
        var value=(className??"").ToLowerInvariant();
        return ComposerClassMarkers.Any(marker=>value.Contains(marker,StringComparison.Ordinal));
    }
    private static readonly HashSet<string> ComposerExecutables=new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.exe","discordcanary.exe","discordptb.exe","slack.exe","teams.exe","ms-teams.exe",
        "code.exe","code-insiders.exe","cursor.exe","opencode.exe","chatgpt.exe","notion.exe",
        "obsidian.exe","element.exe","signal.exe","telegram.exe","whatsapp.exe"
    };
    private static readonly string[] ComposerClassMarkers=
    [
        "slate","monaco","cm-editor","cm-content","prosemirror","ql-editor","tox-",
        "drafteditor","contenteditable","ace_editor"
    ];
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
            var focused=FocusedElementForWindow(target.Window,target.ProcessId);
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
