using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SonaBar.App;
using SonaBar.Audio;
using SonaBar.Core;
using SonaBar.Enhancement;
using SonaBar.Models;
using SonaBar.Storage;
using SonaBar.Transcription;
using SonaBar.WindowsIntegration;
using Xunit;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;

namespace SonaBar.Tests;

[Collection("Desktop clipboard")]
public class ThemeTests
{
    [Fact]
    public void EveryPageUsesReadableThemeResourcesAtSupportedScales()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "SonaBar-theme-test-" + Guid.NewGuid());
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SonaBar.App;component/Themes/Controls.xaml", UriKind.Relative) });
                var settings = new AppSettings(); using var models = new ModelManager(Path.Combine(root, "models")); using var audio = new AudioCaptureService(); using var speech = new WhisperEngine(); using var writing = new LocalEnhancementEngine(); using var hotkeys = new GlobalHotkeyService(); using var ducker = new AudioDucker(); var clipboard = new ClipboardService();
                using var controller = new AppController(settings, new SettingsStore(root), new HistoryRepository(root), models, audio, speech, writing, clipboard, new TextInsertionService(clipboard), hotkeys, ducker);
                var window = new MainWindow(controller); app.MainWindow = window; controller.SetResult("Morgen treffen wir uns um 11 Uhr. Bring bitte die Folien mit.");
                // The compact shell is the only product window: a custom rounded title bar,
                // no resize handle, and a fixed everyday width instead of the old workspace.
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
                Assert.NotNull(chrome);
                Assert.Equal(0, chrome.CaptionHeight);
                Assert.Equal(new Thickness(0), chrome.ResizeBorderThickness);
                Assert.False(chrome.UseAeroCaptionButtons);
                Assert.True(window.ShowInTaskbar);
                Assert.InRange(window.Width, 380, 430);
                Assert.InRange(window.Height, 520, 660);
                Assert.NotNull(FindButton(window, "Settings"));
                Assert.NotNull(FindButton(window, "Minimise"));
                Assert.NotNull(FindButton(window, "Close"));
                var pin = FindButton(window, "Keep SonaBar on top");
                Assert.NotNull(pin);
                pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(window.Topmost);
                pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(window.Topmost);
                // The footer follows the readiness state, so a missing model cannot be labelled
                // "Ready to listen" anywhere in the window.
                Assert.Equal(controller.Readiness.Label, controller.PresenceLabel);
                Assert.StartsWith(controller.Readiness.Label, controller.StageLabel);
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    Theme.Apply(theme);
                    var ink = ((SolidColorBrush)app.Resources["Ink"]).Color; var surface = ((SolidColorBrush)app.Resources["Surface"]).Color; var card = ((SolidColorBrush)app.Resources["Card"]).Color; var muted = ((SolidColorBrush)app.Resources["Muted"]).Color; var windowBackground = ((SolidColorBrush)app.Resources["WindowBackground"]).Color;
                    Assert.True(Contrast(ink, surface) >= 7); Assert.True(Contrast(ink, card) >= 7); Assert.True(Contrast(muted, card) >= 4.5); Assert.True(Contrast(((SolidColorBrush)app.Resources["Accent"]).Color, ((SolidColorBrush)app.Resources["AccentInk"]).Color) >= 4.5);
                    window.BeginInit(); window.EndInit(); Assert.Equal(windowBackground, ((SolidColorBrush)window.Background).Color);
                    foreach (var page in new[] { "Welcome", "Overview", "Dictation", "General", "Audio", "Shortcuts", "Custom Words", "Snippets", "Applications", "History", "Statistics", "Models", "Languages", "Privacy", "Advanced", "Debug", "About", "Help", "Files", "Meetings" })
                    {
                        window.Navigate(page); var content = (FrameworkElement)window.Content;
                        content.Measure(new System.Windows.Size(430, 660)); content.Arrange(new Rect(0, 0, 430, 660)); content.UpdateLayout();
                        Assert.True(content.ActualWidth > 0, $"{theme}/{page}: width={content.ActualWidth}, height={content.ActualHeight}, type={content.GetType().Name}");
                        Assert.InRange(content.ActualWidth, 380, 430);
                        Assert.Equal(surface, ((SolidColorBrush)((System.Windows.Controls.Panel)content).Background).Color);
                        var artifactDirectory = Environment.GetEnvironmentVariable("SONABAR_UI_ARTIFACTS");
                        if (!string.IsNullOrWhiteSpace(artifactDirectory))
                        {
                            Directory.CreateDirectory(artifactDirectory);
                            var pageBitmap = new RenderTargetBitmap(430, 660, 96, 96, PixelFormats.Pbgra32); pageBitmap.Render(content);
                            var pageEncoder = new PngBitmapEncoder(); pageEncoder.Frames.Add(BitmapFrame.Create(pageBitmap));
                            using var pageFile = File.Create(Path.Combine(artifactDirectory, $"{theme}-{page}-compact.png")); pageEncoder.Save(pageFile);
                        }
                        if (page is "Overview" or "Dictation" or "General")
                        {
                            foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
                            {
                                var bitmap = new RenderTargetBitmap((int)(430 * scale), (int)(660 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(content);
                                var directory = Environment.GetEnvironmentVariable("SONABAR_UI_ARTIFACTS");
                                if (!string.IsNullOrWhiteSpace(directory)) { Directory.CreateDirectory(directory); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(directory, $"{theme}-{page}-{scale:0.##}.png")); encoder.Save(file); }
                            }
                        }
                    }
                    var rewrite = new RewriteWindow(controller, "Test text"); Assert.Equal(windowBackground, ((SolidColorBrush)rewrite.Background).Color); rewrite.Close();
                    var selection = new SelectionActionsWindow(controller, new SelectedPassage(null!, "Test text"));
                    ((FrameworkElement)selection.Content).Measure(new System.Windows.Size(540, 530));
                    Assert.True(((FrameworkElement)selection.Content).DesiredSize.Height > 0);
                    selection.Close();
                    var overlay = new RecordingOverlay(controller);
                    // A layered window paints its corners black on some drivers, so the overlay
                    // is a plain window that Windows clips into a rounded shape instead.
                    Assert.False(overlay.AllowsTransparency);
                    Assert.Equal(WindowStyle.None, overlay.WindowStyle);
                    Assert.True(overlay.Topmost);
                    ((FrameworkElement)overlay.Content).Measure(new System.Windows.Size(430, 76));
                    Assert.True(((FrameworkElement)overlay.Content).DesiredSize.Width > 0);
                    Assert.Equal(new CornerRadius(24), ((Border)overlay.Content).CornerRadius);
                    overlay.Close();
                }
                app.Shutdown();
            }
            catch (Exception error) { failure = error; }
            finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(90))); if (failure is not null) throw new InvalidOperationException("Theme verification failed", failure);
    }
    private static Button? FindButton(DependencyObject root, string name) =>
        Descendants(root).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetName(button) == name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static double Contrast(Color a, Color b) { double Lum(Color c) { double F(byte x) { var v = x / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); } return .2126 * F(c.R) + .7152 * F(c.G) + .0722 * F(c.B); } var x = Lum(a); var y = Lum(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
}
