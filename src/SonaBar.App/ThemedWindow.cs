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
    /// Applies a borderless, Windows-native resize and snap frame. The window draws its own
    /// title bar with Windows minimise, maximise/restore and close controls.
    /// </summary>
    protected void UseUtilityChrome()
    {
        WindowStyle = WindowStyle.None;
        var chrome = new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 44,
            ResizeBorderThickness = new Thickness(7),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
    }

    protected static void MakeChromeControl(UIElement element) =>
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(element, true);
}
