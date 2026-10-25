using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SonaBar.WindowsIntegration;
namespace SonaBar.App;

public sealed class RecordingOverlay : ThemedWindow
{
    private readonly AppController controller;
    private readonly TextBlock caption=new(){FontSize=14,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap};
    private readonly TextBlock detail=new(){FontSize=11,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap};
    private readonly Border recordCircle=new(){Width=44,Height=44,CornerRadius=new CornerRadius(22)};
    private readonly Waveform waveform=new(){Width=100,Height=28};
    private readonly Border pill;
    private readonly TranslateTransform slide=new(0,8);
    private readonly Stopwatch clock=new();
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer dismiss;
    private readonly Button actions;
    private readonly Button finish;
    private bool notice;
    private OperationState state;
    public RecordingOverlay(AppController controller)
    {
        this.controller=controller;Width=430;Height=76;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=false;Topmost=true;ShowActivated=false;ShowInTaskbar=false;
        SetResourceReference(BackgroundProperty,"OverlaySurface");
        caption.SetResourceReference(TextBlock.ForegroundProperty,"OverlayInk");
        detail.SetResourceReference(TextBlock.ForegroundProperty,"TextSecondary");
        recordCircle.SetResourceReference(Border.BackgroundProperty,"Accent");
        recordCircle.HorizontalAlignment=HorizontalAlignment.Center;
        recordCircle.VerticalAlignment=VerticalAlignment.Center;
        recordCircle.Child=Ui.EarIcon(20);
        waveform.HorizontalAlignment=HorizontalAlignment.Center;
        waveform.VerticalAlignment=VerticalAlignment.Center;
        waveform.Visibility=Visibility.Collapsed;
        var row=new Grid{VerticalAlignment=VerticalAlignment.Center};
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(54)});
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(108)});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(60)});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});
        row.Children.Add(recordCircle);
        var textStack=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};
        textStack.Children.Add(caption);textStack.Children.Add(detail);Grid.SetColumn(textStack,1);row.Children.Add(textStack);
        Grid.SetColumn(waveform,2);row.Children.Add(waveform);
        finish=Ui.Button("Stop",controller.ToggleDictation,true);finish.Style=(Style)FindResource("BlueButton");finish.ToolTip="Finish dictation";finish.MinWidth=54;finish.Width=54;finish.MinHeight=30;finish.Height=30;finish.Padding=new Thickness(0);finish.Margin=new Thickness(0);finish.FontSize=12;finish.HorizontalAlignment=HorizontalAlignment.Center;finish.Visibility=Visibility.Collapsed;Grid.SetColumn(finish,3);row.Children.Add(finish);
        actions=Ui.Button("×",()=>{if(controller.Busy)controller.Cancel();else OpenActions();});actions.Style=(Style)FindResource("IconButton");actions.ToolTip="Cancel while recording · text actions when done";actions.Width=28;actions.Height=28;actions.Padding=new Thickness(0);actions.Margin=new Thickness(0);actions.HorizontalAlignment=HorizontalAlignment.Center;actions.SetResourceReference(ForegroundProperty,"TextSecondary");Grid.SetColumn(actions,4);row.Children.Add(actions);
        pill=new Border{CornerRadius=new CornerRadius(24),Padding=new Thickness(10),Child=row,BorderThickness=new Thickness(1),RenderTransform=slide,Opacity=0};
        pill.SetResourceReference(Border.BackgroundProperty,"OverlaySurface");
        pill.SetResourceReference(Border.BorderBrushProperty,"Border");
        Content=pill;
        SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(this).Handle;WindowIntegration.MakeNonActivating(handle);ApplyShape();};
        SizeChanged+=(_,_)=>ApplyShape();
        DpiChanged+=(_,_)=>ApplyShape();
        MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource==pill||e.OriginalSource==caption||e.OriginalSource==detail){DragMove();var position=WindowIntegration.WindowPosition(new WindowInteropHelper(this).Handle);controller.Settings.OverlayPixelLeft=position.Left;controller.Settings.OverlayPixelTop=position.Top;controller.SettingsStore.Save(controller.Settings);}};
        timer=new DispatcherTimer(TimeSpan.FromMilliseconds(100),DispatcherPriority.Render,(_,_)=>{if(state==OperationState.Recording)detail.Text=$"{clock.Elapsed:mm\\:ss}  ·  On this PC";},Dispatcher);timer.Stop();
        dismiss=new DispatcherTimer{Interval=TimeSpan.FromSeconds(7)};dismiss.Tick+=(_,_)=>{dismiss.Stop();notice=false;if(!controller.Busy)Hide();};
        Closed+=(_,_)=>{timer.Stop();dismiss.Stop();waveform.Stop();};
    }
    /// <summary>Cuts the window into a rounded shape so the corners belong to the desktop.</summary>
    private void ApplyShape()
    {
        var handle=new WindowInteropHelper(this).Handle;
        if(handle==nint.Zero)return;
        var dpi=VisualTreeHelper.GetDpi(this);
        var width=(int)Math.Round(ActualWidth*dpi.DpiScaleX);
        var height=(int)Math.Round(ActualHeight*dpi.DpiScaleY);
        var radius=(int)Math.Round(24*dpi.DpiScaleX);
        WindowIntegration.ApplyRoundedRegion(handle,width,height,radius);
    }
    public void AddLevel(float value){if(state==OperationState.Recording)waveform.Add(value);}
    public void ShowPreparing(){dismiss.Stop();notice=false;Reveal();finish.Visibility=Visibility.Collapsed;waveform.Visibility=Visibility.Collapsed;actions.Content="×";SetCircle("Accent",Ui.EarIcon(20));caption.Text="Getting ready…";detail.Text="On this PC";}
    public void Dismiss(){dismiss.Stop();notice=false;Hide();}
    private void Reveal()
    {
        if(IsVisible)return;
        Show();WindowIntegration.PositionOverlay(new WindowInteropHelper(this).Handle,430,76,controller.Settings.OverlayPixelLeft,controller.Settings.OverlayPixelTop);
        pill.Opacity=0;slide.Y=8;
        pill.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)));
        slide.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(8,0,TimeSpan.FromMilliseconds(200)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
    }
    public void SetState(OperationState value)
    {
        state=value;
        if(value==OperationState.Idle){timer.Stop();if(!notice)Hide();return;}
        dismiss.Stop();notice=false;Reveal();actions.Content="×";finish.Visibility=value==OperationState.Recording?Visibility.Visible:Visibility.Collapsed;
        waveform.Visibility=value is OperationState.Recording or OperationState.Transcribing or OperationState.Enhancing or OperationState.Inserting?Visibility.Visible:Visibility.Collapsed;
        if(value==OperationState.Recording)
        {
            clock.Restart();waveform.BeginListening();timer.Start();SetCircle("Accent",Ui.EarIcon(20));caption.Text="Listening";detail.Text="00:00  ·  On this PC";
        }
        else if(value is OperationState.Transcribing or OperationState.Enhancing or OperationState.Inserting)
        {
            timer.Stop();waveform.BeginProcessing();SetCircle("Accent",Ui.EarIcon(20));
            caption.Text=value switch{OperationState.Enhancing=>"Polishing…",OperationState.Inserting=>"Inserting…",_=>"Transcribing…"};detail.Text="Working locally";
        }
        else
        {
            timer.Stop();waveform.Stop();SetCircle("Accent",Ui.EarIcon(20));
            caption.Text=value==OperationState.Cancelled?"Cancelled":"Please try again";detail.Text="On this PC";
        }
    }
    public void ShowNotice(string text,bool error)
    {
        notice=true;Reveal();timer.Stop();waveform.Stop();waveform.Visibility=Visibility.Collapsed;finish.Visibility=Visibility.Collapsed;caption.Text=text;caption.ToolTip=text;detail.Text=error?"Needs attention":"Inserted  ·  Local & private";
        SetCircle(error?"OverlayError":"Success",new TextBlock{Text=error?"!":"✓",FontSize=22,FontWeight=FontWeights.Bold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
        actions.Content="···";
        dismiss.Stop();dismiss.Interval=TimeSpan.FromSeconds(error?10:controller.Settings.ShowQuickActions?8:1.5);dismiss.Start();
    }
    private void SetCircle(string resource,UIElement content)
    {
        recordCircle.SetResourceReference(Border.BackgroundProperty,resource);
        recordCircle.Child=content;
    }
    private void OpenActions()
    {
        dismiss.Stop();var menu=QuickActions.Create(controller);menu.Closed+=(_,_)=>dismiss.Start();actions.ContextMenu=menu;menu.PlacementTarget=actions;menu.IsOpen=true;
    }
}
public sealed class Waveform : FrameworkElement
{
    private const int BarCount=18;
    private const float NoiseFloor=.012f;
    private const float FullScale=.65f;
    private readonly float[] values=new float[BarCount];
    private readonly float[] history=new float[BarCount];
    private float target;
    private readonly EventHandler rendering;
    private bool active;
    private bool processing;
    private long lastFrame;
    private double processingPhase;
    public Waveform(){Width=94;Height=30;rendering=(_,_)=>Tick();Unloaded+=(_,_)=>Stop();}
    public void BeginListening()
    {
        processing=false;target=0;Array.Fill(values,0);Array.Fill(history,0);lastFrame=Stopwatch.GetTimestamp();SetActive(true);InvalidateVisual();
    }
    public void BeginProcessing(){processing=true;target=0;processingPhase=0;lastFrame=Stopwatch.GetTimestamp();SetActive(true);}
    public void Stop(){processing=false;target=0;Array.Fill(values,0);Array.Fill(history,0);SetActive(false);InvalidateVisual();}
    private void SetActive(bool value)
    {
        if(active==value)return; active=value;
        if(value)CompositionTarget.Rendering+=rendering;else CompositionTarget.Rendering-=rendering;
    }
    public void Add(float sample)
    {
        if(processing)return;
        var mapped=MapAudio(sample);
        target=mapped;
        Array.Copy(history,1,history,0,BarCount-1);
        history[^1]=mapped;
    }
    private static float MapAudio(float sample)
    {
        var normalized=Math.Clamp((sample-NoiseFloor)/(FullScale-NoiseFloor),0f,1f);
        // RMS is already stable; this gentle power curve makes ordinary speech visible while
        // reserving the final headroom for genuinely loud speech.
        return MathF.Pow(normalized,.68f);
    }
    private void Tick()
    {
        var now=Stopwatch.GetTimestamp();var elapsed=lastFrame==0?1d/60d:Math.Clamp((now-lastFrame)/(double)Stopwatch.Frequency,1d/240d,.05d);lastFrame=now;
        processingPhase+=elapsed;
        for(var i=0;i<BarCount;i++)
        {
            var position=(i-(BarCount-1)/2f)/((BarCount-1)/2f);
            var envelope=.64f+.36f*(1f-position*position);
            // A small, deterministic trace of recent genuine samples restores the organic
            // contour of the original waveform without allowing idle bars to animate on their own.
            var texture=.88f+.12f*history[i];
            var desired=processing
                ? .12f*envelope*(.82f+.18f*(float)Math.Sin(processingPhase*5+i*.55))
                : target*envelope*texture;
            var timeConstant=desired>values[i]?.042f:.085f;
            var rate=1f-(float)Math.Exp(-elapsed/timeConstant);
            values[i]+=(desired-values[i])*rate;
        }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext context)
    {
        var brush=TryFindResource("OverlayAccent") as System.Windows.Media.Brush ?? Brushes.MediumSeaGreen;
        const double minimumHeight=2.5,maximumHeight=24,barWidth=2.6;
        var width=RenderSize.Width>0?RenderSize.Width:Width;
        var heightLimit=RenderSize.Height>0?RenderSize.Height:Height;
        var gap=(width-barWidth*BarCount)/(BarCount-1);
        for(var i=0;i<BarCount;i++)
        {
            var height=minimumHeight+values[i]*(maximumHeight-minimumHeight);
            context.DrawRoundedRectangle(brush,null,new Rect(i*(barWidth+gap),(heightLimit-height)/2,barWidth,height),barWidth/2,barWidth/2);
        }
    }
}
