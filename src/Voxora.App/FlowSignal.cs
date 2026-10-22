using System.Windows.Automation;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;

namespace Voxora.App;

// A quiet visual signature for the workspace, drawn by WPF so it scales cleanly
// with Windows display settings and follows the active theme.
internal sealed class FlowSignal : Control
{
    public FlowSignal()
    {
        AutomationProperties.SetIsOffscreenBehavior(this, IsOffscreenBehavior.FromClip);
        Focusable = false;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if(e.Property==ForegroundProperty)InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var width=ActualWidth;
        var height=ActualHeight;
        if(width<=0||height<=0)return;

        var center=new Point(width*.5,height*.5);
        var halo=new Pen(Foreground,1);
        halo.Freeze();
        drawing.PushOpacity(.18);
        drawing.DrawEllipse(null,halo,center,width*.43,width*.43);
        drawing.DrawEllipse(null,halo,center,width*.30,width*.30);
        drawing.DrawEllipse(null,halo,center,width*.17,width*.17);
        drawing.Pop();

        for(var track=0;track<5;track++)
        {
            var geometry=new StreamGeometry();
            using(var context=geometry.Open())
            {
                for(var step=0;step<=72;step++)
                {
                    var x=width*.09+step*width*.82/72;
                    var envelope=Math.Pow(Math.Sin(Math.PI*step/72),1.4);
                    var phase=step*.25+track*.58;
                    var y=center.Y+(track-2)*7+Math.Sin(phase)*envelope*(21-track*1.7);
                    if(step==0)context.BeginFigure(new Point(x,y),false,false);
                    else context.LineTo(new Point(x,y),true,false);
                }
            }
            geometry.Freeze();
            drawing.PushOpacity(track==2?.95:.38);
            drawing.DrawGeometry(null,new Pen(Foreground,track==2?2.6:1.2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},geometry);
            drawing.Pop();
        }
        drawing.DrawEllipse(Foreground,null,new Point(width*.91,center.Y),3.5,3.5);
    }
}
