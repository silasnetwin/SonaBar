namespace SonaBar.App;
// WPF's implicit style lookup uses the concrete class key. Every app window opts into
// this shared Window style explicitly instead of depending on base-type lookup.
public class ThemedWindow : Window
{
    public ThemedWindow() => SetResourceReference(StyleProperty, "AppWindow");

    /// <summary>
    /// Keeps the standard Windows frame with the minimise, maximise and close buttons that
    /// Windows draws itself. Only the colour of the title bar follows the theme, so the
    /// window behaves exactly like a native Windows program.
    /// </summary>
    protected void UseNativeChrome()
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
    }

    /// <summary>
    /// Applies the fixed compact shell used by the product window. The window draws its own
    /// title bar, keeps the native taskbar entry, and relies on Windows 11's rounded DWM frame.
    /// There is deliberately no resize border and no native caption hit area, so dragging and
    /// caption actions stay under the app's control.
    /// </summary>
    protected void UseCompactChrome()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        var chrome = new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
    }
}
