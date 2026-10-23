using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading;
using Voxora.Audio;
using Voxora.Enhancement;
using Voxora.Models;
using Voxora.Storage;
using Voxora.Transcription;
using Voxora.Updater;
using Voxora.WindowsIntegration;

namespace Voxora.App;

public partial class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? activationWait;
    private ServiceProvider? services;
    private System.Windows.Forms.NotifyIcon? tray;
    private MainWindow? window;
    private AppController? controller;
    private bool quitting;
    private bool smoke;
    private string? downloadedUpdate;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startupClock = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string stage) => SafeLog.WriteDiagnostic("Startup", ("stage", stage), ("elapsed_ms", $"{startupClock.Elapsed.TotalMilliseconds:F0}"));
        Mark("process");
        smoke=e.Args.Contains("--smoke-test");
        var instance=smoke?"Local\\SNineStudios.SineFlow.Smoke."+Environment.ProcessId:"Local\\SNineStudios.SineFlow";
        if(smoke&&Environment.GetEnvironmentVariable("SINEFLOW_DATA_ROOT") is null)Environment.SetEnvironmentVariable("SINEFLOW_DATA_ROOT",Path.Combine(Path.GetTempPath(),"SineFlow-smoke-"+Environment.ProcessId));
        mutex = new Mutex(true, instance, out var first);
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, instance+".Activate");
        if (!first)
        {
            var currentPath=Environment.ProcessPath ?? AppContext.BaseDirectory;
            var existingPath=ExistingSineFlowPath(currentPath);
            SafeLog.WriteDiagnostic("SecondInstance",("requested_executable",currentPath),("existing_executable",existingPath ?? "unknown"));
            activation.Set();
            if(!smoke)MessageBox.Show("SineFlow is already running"+(existingPath is null ? "." : " from:\n"+existingPath)+"\n\nQuit the existing instance before starting a different build.","SineFlow",MessageBoxButton.OK,MessageBoxImage.Information);
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) => { SafeLog.Write("UI", args.Exception); controller?.Cancel(); if(!smoke)MessageBox.Show("SineFlow could not complete the operation. " + args.Exception.Message, "SineFlow", MessageBoxButton.OK, MessageBoxImage.Warning); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, args) => { SafeLog.Write("Background", args.Exception); args.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception error) SafeLog.Write("Fatal", error); };
        Brand.MigrateLegacyData(); Directory.CreateDirectory(Brand.DataRoot);
        var temp = Path.Combine(Brand.DataRoot, "Temp"); Directory.CreateDirectory(temp);
        foreach (var file in Directory.GetFiles(temp)) { try { File.Delete(file); } catch (IOException) { } }
        Mark("data_ready");
        var registry = new ServiceCollection();
        registry.AddSingleton(new SettingsStore()); registry.AddSingleton(p => p.GetRequiredService<SettingsStore>().Load());
        registry.AddSingleton(new HistoryRepository()); registry.AddSingleton(new ModelManager());
        registry.AddSingleton<AudioCaptureService>(); registry.AddSingleton<AudioDucker>(); registry.AddSingleton<ClipboardService>(); registry.AddSingleton<TextInsertionService>(); registry.AddSingleton<GlobalHotkeyService>();
        registry.AddSingleton<WhisperEngine>(); registry.AddSingleton<ParakeetEngine>(); registry.AddSingleton<QwenEngine>(); registry.AddSingleton<ITranscriptionEngine, TranscriptionEngineRouter>(); registry.AddSingleton<IEnhancementEngine, LocalEnhancementEngine>(); registry.AddSingleton<AppController>();
        services = registry.BuildServiceProvider(); controller = services.GetRequiredService<AppController>();
        // Fields inside SineFlow are inspected on the interface thread; other applications are
        // inspected on the automation thread so a busy target can never freeze the window.
        TextInsertionService.InterfaceThread = work => Dispatcher.Invoke(work);
        SafeLog.WriteDiagnostic("Startup",
            ("version",Brand.Version),
            ("executable",Environment.ProcessPath ?? "unknown"),
            ("base_directory",AppContext.BaseDirectory),
            ("pid",Environment.ProcessId),
            ("build_id",typeof(App).Assembly.ManifestModule.ModuleVersionId.ToString("N")),
            ("timing_diagnostics",controller.Settings.DebugLogging));
        Theme.Apply(controller.Settings.Theme);
        WhisperEngine.ConfigureBackend(controller.Settings.Backend);
        LocalEnhancementEngine.ConfigureBackend(controller.Settings.Backend);
        try { services.GetRequiredService<AudioDucker>().Recover(); } catch (Exception error) { SafeLog.Write("VolumeRecovery", error); }
        Mark("services_ready");
        window = new MainWindow(controller); MainWindow = window;
        var overlay = new RecordingOverlay(controller); controller.AttachOverlay(overlay);
        tray = new System.Windows.Forms.NotifyIcon { Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "SineFlow.ico")), Text = "SineFlow · Speech to Text", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        void Item(string title, Action action) => menu.Items.Add(title, null, (_, _) => Dispatcher.Invoke(action));
        Item("SineFlow", ShowWindow); menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        Item("Start Dictation", controller.ToggleDictation); Item("Edit Selected Text", controller.EditSelection);
        Item("Transcribe File", () => { ShowWindow(); window.Navigate("Files"); }); Item("Meeting Mode", () => { ShowWindow(); window.Navigate("Meetings"); });
        Item("History", () => { ShowWindow(); window.Navigate("History"); }); Item("Settings", ShowWindow);
        var pause = new System.Windows.Forms.ToolStripMenuItem("Pause SineFlow") { CheckOnClick = true }; pause.CheckedChanged += (_, _) => controller.Paused = pause.Checked; menu.Items.Add(pause);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator()); Item("Quit", Quit);
        tray.ContextMenuStrip = menu; tray.MouseClick += (_, args) => { if (args.Button == System.Windows.Forms.MouseButtons.Left) Dispatcher.Invoke(ShowWindow); };
        controller.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(AppController.Status)) { tray.Text = ("SineFlow · " + controller.Status)[..Math.Min(63, 11 + controller.Status.Length)]; } };
        activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => Dispatcher.BeginInvoke(ShowWindow), null, Timeout.Infinite, false);
        if(!smoke)controller.Initialize();
        if(!smoke&&controller.Settings.AutomaticUpdates)_=CheckForUpdatesAsync();
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        if (!smoke&&(!controller.Settings.Onboarded || !e.Args.Contains("--tray"))) ShowWindow();
        if (!controller.Settings.Onboarded) window.Navigate("Welcome");
        Mark("window_shown");
        // The models and the speech engine are prepared after the window exists. Nothing is
        // downloaded here, so the first start stays fast and a missing model is reported as
        // a state the user can act on.
        if(!smoke)await controller.InitializeAsync();
        Mark("ready");
        if (e.Args.Contains("--smoke-test"))
        {
            var content=(FrameworkElement)window.Content;content.Measure(new System.Windows.Size(1060,780));content.Arrange(new Rect(0,0,1060,780));content.UpdateLayout();
            // Every page is built and laid out, so a broken page is found by the smoke test
            // instead of when a user opens it for the first time.
            var rendered=new List<string>();var failed=new List<string>();
            foreach(var page in new[]{"Overview","General","Dictation","Writing","Models","Languages","Audio","Shortcuts","Custom Words","Snippets","Applications","History","Statistics","Privacy","Advanced","Debug","About","Files","Meetings"})
            {
                try{window.Navigate(page);content.UpdateLayout();rendered.Add(page);}
                catch(Exception error){failed.Add(page+":"+error.GetType().Name);}
            }
            window.Navigate("Overview");content.UpdateLayout();
            // An optional page name writes a picture of that page, so a layout change can be
            // reviewed without a screen recording.
            var shotIndex=Array.IndexOf(e.Args,"--smoke-shot");
            if(shotIndex>=0&&shotIndex+1<e.Args.Length)
            {
                window.Navigate(e.Args[shotIndex+1]);content.UpdateLayout();
                // Let bindings and styles settle, so the picture shows the finished page.
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                content.UpdateLayout();
                var width=(int)Math.Max(1,content.ActualWidth);var height=(int)Math.Max(1,content.ActualHeight);
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using(var stream=File.Create(Path.Combine(Brand.DataRoot,"smoke-shot.png")))encoder.Save(stream);
                window.Navigate("Overview");
            }
            await controller.SmokeTestAsync(tray.Visible, content.ActualWidth>0, rendered, failed);
            Quit();
        }
    }
    private static string? ExistingSineFlowPath(string currentPath)
    {
        try
        {
            var processName=Path.GetFileNameWithoutExtension(currentPath);
            foreach(var process in Process.GetProcessesByName(processName))
            {
                using(process)
                {
                    if(process.Id==Environment.ProcessId)continue;
                    try { return process.MainModule?.FileName; } catch (InvalidOperationException) { }
                }
            }
        }
        catch (InvalidOperationException) { }
        return null;
    }
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) { if (controller?.Settings.Theme == "System") Dispatcher.BeginInvoke(() => Theme.Apply("System")); }
    private void ShowWindow() { window?.Show(); if (window is not null) window.WindowState = WindowState.Normal; window?.Activate(); }
    public async void Quit()
    {
        if (quitting) return;
        if (controller?.Busy == true && MessageBox.Show("An operation is still running. Cancel it and quit?", "Quit SineFlow", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        quitting = true;
        try
        {
            if(controller is not null)await controller.StopSafelyAsync();
            tray?.Dispose(); activationWait?.Unregister(null); SystemEvents.UserPreferenceChanged -= PreferenceChanged;
            services?.Dispose(); activation?.Dispose(); mutex?.Dispose();
        }
        catch(Exception error)
        {
            SafeLog.Write("Quit", error);
            if(smoke)Console.Error.WriteLine(error);
            Environment.ExitCode=1;
        }
        finally { Shutdown(); }
        var update=downloadedUpdate;
        if(update is not null&&File.Exists(update))
            Process.Start(new ProcessStartInfo(update,$"--update --wait-pid {Environment.ProcessId}"){UseShellExecute=true});
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var configuration=UpdateFeedConfiguration.Load(AppContext.BaseDirectory);
            if(!Uri.TryCreate(configuration.Feed,UriKind.Absolute,out var feed)||feed.Scheme!="https")return;
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};
            var service=new UpdateService(http,feed);
            if(!Version.TryParse(Brand.Version,out var installed))return;
            var release=await service.CheckAsync(installed,configuration.Channel,CancellationToken.None);
            if(release is null)return;
            var updates=Path.Combine(Brand.DataRoot,"Updates");Directory.CreateDirectory(updates);
            var destination=Path.Combine(updates,$"SineFlow-Setup-{release.Version}.exe");
            if(!File.Exists(destination))await service.DownloadAsync(release,destination,CancellationToken.None);
            downloadedUpdate=destination;
            _ = Dispatcher.BeginInvoke(() =>
            {
                controller?.Inform($"SineFlow {release.Version} is ready and will install when you quit.");
                tray?.ShowBalloonTip(4500,"SineFlow update ready",$"Version {release.Version} will install automatically when you quit.",System.Windows.Forms.ToolTipIcon.Info);
            });
        }
        catch(Exception error)
        {
            SafeLog.Write("UpdateCheck",error);
        }
    }
}
public static class Theme
{
    /// <summary>
    /// Applies the colour system and keeps the native title bar in step with it. Every colour
    /// comes from <see cref="Palette"/>, so there is exactly one place that defines them.
    /// </summary>
    public static void Apply(string choice)
    {
        var palette = PaletteService.Apply(choice);
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "AppBackground", "WindowBackground", "Surface", "SurfaceElevated", "Background", "BackgroundElevated", "Sidebar", "Card", "Hover", "SurfaceHover", "SurfacePressed", "SurfaceSelected", "OverlaySurface" })
                Application.Current.Resources[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "TextPrimary", "TextSecondary", "TextTertiary", "Ink", "Muted", "OverlayInk", "Error", "OverlayError", "Danger", "Warning", "Success" })
                Application.Current.Resources[key] = SystemColors.WindowTextBrush;
            Application.Current.Resources["Accent"] = SystemColors.HighlightBrush;
            Application.Current.Resources["AccentSoft"] = SystemColors.ControlBrush;
            Application.Current.Resources["AccentInk"] = SystemColors.HighlightTextBrush;
            Application.Current.Resources["Line"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["Action"] = SystemColors.HighlightBrush;
            Application.Current.Resources["ActionInk"] = SystemColors.HighlightTextBrush;
        }
        foreach (Window window in Application.Current.Windows)
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            WindowIntegration.ApplyTitleBar(handle, palette.IsDark, Palette.ToWindowColour(palette.AppBackground));
        }
    }
}
