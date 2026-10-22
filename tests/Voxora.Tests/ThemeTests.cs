using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Voxora.App;
using Voxora.Audio;
using Voxora.Core;
using Voxora.Enhancement;
using Voxora.Models;
using Voxora.Storage;
using Voxora.Transcription;
using Voxora.WindowsIntegration;
using Xunit;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;

namespace Voxora.Tests;

[Collection("Desktop clipboard")]
public class ThemeTests
{
    [Fact]
    public void EveryPageUsesReadableThemeResourcesAtSupportedScales()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "Voxora-theme-test-" + Guid.NewGuid());
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SineFlow.App;component/Themes/Controls.xaml", UriKind.Relative) });
                var settings = new AppSettings(); using var models = new ModelManager(Path.Combine(root, "models")); using var audio = new AudioCaptureService(); using var speech = new WhisperEngine(); using var writing = new LocalEnhancementEngine(); using var hotkeys = new GlobalHotkeyService(); using var ducker = new AudioDucker(); var clipboard = new ClipboardService();
                using var controller = new AppController(settings, new SettingsStore(root), new HistoryRepository(root), models, audio, speech, writing, clipboard, new TextInsertionService(clipboard), hotkeys, ducker);
                var window = new MainWindow(controller); app.MainWindow = window; controller.SetResult("Morgen treffen wir uns um 11 Uhr. Bring bitte die Folien mit.");
                // The main window keeps the native Windows frame: the system draws minimise,
                // maximise and close, and the title bar only follows the theme colour.
                Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
                Assert.True(window.ResizeMode is ResizeMode.CanResize);
                Assert.Null(System.Windows.Shell.WindowChrome.GetWindowChrome(window));
                // The sidebar pill and the footer both follow the readiness state, so a missing
                // model cannot be labelled "Ready to listen" anywhere in the window.
                Assert.Equal("●  " + controller.Readiness.Label, controller.PresenceLabel);
                Assert.StartsWith(controller.Readiness.Label, controller.StageLabel);
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    Theme.Apply(theme);
                    var ink = ((SolidColorBrush)app.Resources["Ink"]).Color; var surface = ((SolidColorBrush)app.Resources["Surface"]).Color; var card = ((SolidColorBrush)app.Resources["Card"]).Color; var muted = ((SolidColorBrush)app.Resources["Muted"]).Color;
                    Assert.True(Contrast(ink, surface) >= 7); Assert.True(Contrast(ink, card) >= 7); Assert.True(Contrast(muted, card) >= 4.5); Assert.True(Contrast(((SolidColorBrush)app.Resources["Accent"]).Color, ((SolidColorBrush)app.Resources["AccentInk"]).Color) >= 4.5);
                    window.BeginInit(); window.EndInit(); Assert.Equal(surface, ((SolidColorBrush)window.Background).Color);
                    foreach (var page in new[] { "Welcome", "Overview", "Dictation", "General", "Audio", "Shortcuts", "Custom Words", "Snippets", "Applications", "History", "Statistics", "Models", "Languages", "Privacy", "Advanced", "Debug", "About", "Files", "Meetings" })
                    {
                        window.Navigate(page); var content = (FrameworkElement)window.Content;
                        content.Measure(new System.Windows.Size(1060, 780)); content.Arrange(new Rect(0, 0, 1060, 780)); content.UpdateLayout();
                        Assert.True(content.ActualWidth > 0);
                        Assert.Equal(surface, ((SolidColorBrush)((System.Windows.Controls.Panel)content).Background).Color);
                        var artifactDirectory = Environment.GetEnvironmentVariable("VOXORA_UI_ARTIFACTS");
                        if (!string.IsNullOrWhiteSpace(artifactDirectory) && page is not ("Overview" or "Dictation" or "General"))
                        {
                            Directory.CreateDirectory(artifactDirectory);
                            var pageBitmap = new RenderTargetBitmap(1060, 780, 96, 96, PixelFormats.Pbgra32); pageBitmap.Render(content);
                            var pageEncoder = new PngBitmapEncoder(); pageEncoder.Frames.Add(BitmapFrame.Create(pageBitmap));
                            using var pageFile = File.Create(Path.Combine(artifactDirectory, $"{theme}-{page}-1.png")); pageEncoder.Save(pageFile);
                        }
                        if (!string.IsNullOrWhiteSpace(artifactDirectory) && page is ("Overview" or "Dictation" or "Files"))
                        {
                            content.Measure(new System.Windows.Size(780, 580)); content.Arrange(new Rect(0, 0, 780, 580)); content.UpdateLayout();
                            var compactBitmap = new RenderTargetBitmap(780, 580, 96, 96, PixelFormats.Pbgra32); compactBitmap.Render(content);
                            var compactEncoder = new PngBitmapEncoder(); compactEncoder.Frames.Add(BitmapFrame.Create(compactBitmap));
                            using var compactFile = File.Create(Path.Combine(artifactDirectory, $"{theme}-{page}-compact.png")); compactEncoder.Save(compactFile);
                            content.Measure(new System.Windows.Size(1060, 780)); content.Arrange(new Rect(0, 0, 1060, 780)); content.UpdateLayout();
                        }
                        if (page is "Overview" or "Dictation" or "General")
                        {
                            foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
                            {
                                var bitmap = new RenderTargetBitmap((int)(1060 * scale), (int)(780 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(content);
                                var directory = Environment.GetEnvironmentVariable("VOXORA_UI_ARTIFACTS");
                                if (!string.IsNullOrWhiteSpace(directory)) { Directory.CreateDirectory(directory); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(directory, $"{theme}-{page}-{scale:0.##}.png")); encoder.Save(file); }
                            }
                        }
                    }
                    var rewrite = new RewriteWindow(controller, "Test text"); Assert.Equal(surface, ((SolidColorBrush)rewrite.Background).Color); rewrite.Close();
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
                    ((FrameworkElement)overlay.Content).Measure(new System.Windows.Size(346, 62));
                    Assert.True(((FrameworkElement)overlay.Content).DesiredSize.Width > 0);
                    Assert.Equal(new CornerRadius(20), ((Border)overlay.Content).CornerRadius);
                    overlay.Close();
                }
                app.Shutdown();
            }
            catch (Exception error) { failure = error; }
            finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(90))); if (failure is not null) throw new InvalidOperationException("Theme verification failed", failure);
    }
    private static double Contrast(Color a, Color b) { double Lum(Color c) { double F(byte x) { var v = x / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); } return .2126 * F(c.R) + .7152 * F(c.G) + .0722 * F(c.B); } var x = Lum(a); var y = Lum(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
}
