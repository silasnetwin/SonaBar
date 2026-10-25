using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace SonaBar.Setup;
internal static class Program
{
    private static readonly string InstallRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","SonaBar");
    private const string UninstallKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SilasNet.SonaBar";
    [STAThread] private static void Main(string[] args)
    {
        var app=new Application();
        if(args.Contains("--uninstall")){Uninstall();return;}
        if(args.Contains("--update")){ApplyUpdate(args);return;}
        var canvas=new SolidColorBrush(Color.FromRgb(236,236,236));var card=new SolidColorBrush(Colors.White);var ink=new SolidColorBrush(Color.FromRgb(29,29,31));var accent=new SolidColorBrush(Color.FromRgb(0,102,204));var action=new SolidColorBrush(Color.FromRgb(47,168,79));
        var window=new Window{Title="Install SonaBar",Width=540,Height=460,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=canvas,Foreground=ink,FontFamily=new FontFamily("Segoe UI Variable Display, Segoe UI")};
        var stack=new StackPanel{Margin=new Thickness(32)};
        var eyebrow=new TextBlock{Text="SONABAR  /  WINDOWS",FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=accent,Margin=new Thickness(0,0,0,8)};stack.Children.Add(eyebrow);
        stack.Children.Add(new TextBlock{Text="SonaBar",FontSize=34,FontWeight=FontWeights.SemiBold});
        stack.Children.Add(new TextBlock{Text="Speech to Text. Private. Offline.\nby SilasNet",FontSize=15,Foreground=new SolidColorBrush(Color.FromRgb(110,110,115)),Margin=new Thickness(0,8,0,20)});
        var copy=new TextBlock{Text="Install for your Windows account. No administrator access is needed. Speech models are prepared privately inside SonaBar after installation.",TextWrapping=TextWrapping.Wrap};
        stack.Children.Add(new Border{Background=card,CornerRadius=new CornerRadius(14),BorderBrush=new SolidColorBrush(Color.FromRgb(217,217,217)),BorderThickness=new Thickness(1),Padding=new Thickness(18,15,18,15),Child=copy,Margin=new Thickness(0,0,0,16)});
        var desktop=new CheckBox{Content="Create a Desktop shortcut",IsChecked=false,Margin=new Thickness(0,0,0,12)};stack.Children.Add(desktop);
        var status=new TextBlock{Text=InstallRoot,FontSize=11,TextWrapping=TextWrapping.Wrap};stack.Children.Add(status);
        var progress=new ProgressBar{Height=6,Margin=new Thickness(0,12,0,12),Maximum=100};stack.Children.Add(progress);
        var install=new Button{Content="Install SonaBar",Padding=new Thickness(16,11,16,11),Background=action,Foreground=Brushes.White,BorderThickness=new Thickness(0),FontWeight=FontWeights.SemiBold};stack.Children.Add(install);
        bool running=false;
        window.Closing+=(_,e)=>{if(running)e.Cancel=true;};
        install.Click+=async(_,_)=>
        {
            running=true;install.IsEnabled=false;
            try
            {
                if(Process.GetProcessesByName("SonaBar.App").Length>0)throw new InvalidOperationException("Quit SonaBar from the system tray before installing an update.");
                bool shortcut=desktop.IsChecked==true;
                await Task.Run(()=>Install(new Progress<double>(p=>window.Dispatcher.Invoke(()=>progress.Value=p))));
                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"SonaBar.lnk"));
                if(shortcut)Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"SonaBar.lnk"));
                using var key=Registry.CurrentUser.CreateSubKey(UninstallKey);key.SetValue("DisplayName","SonaBar");key.SetValue("Publisher","SilasNet");key.SetValue("DisplayVersion",Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)??"0.1.3");key.SetValue("InstallLocation",InstallRoot);key.SetValue("DisplayIcon",Path.Combine(InstallRoot,"SonaBar.App.exe"));key.SetValue("UninstallString",$"\"{Path.Combine(InstallRoot,"SonaBar-Uninstall.exe")}\" --uninstall");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
                status.Text="Installed. Your voice is ready for a new home.";install.Content="Launch SonaBar";install.IsEnabled=true;running=false;
                Process.Start(new ProcessStartInfo(Path.Combine(InstallRoot,"SonaBar.App.exe")){UseShellExecute=true});window.Close();
            }
            catch(Exception error){status.Text=error.Message;install.IsEnabled=true;running=false;}
        };
        window.Content=stack;app.Run(window);
    }
    private static void ApplyUpdate(string[] args)
    {
        try
        {
            var waitIndex=Array.IndexOf(args,"--wait-pid");
            if(waitIndex>=0&&waitIndex+1<args.Length&&int.TryParse(args[waitIndex+1],out var pid))
            {
                try { using var process=Process.GetProcessById(pid); process.WaitForExit(30000); } catch(ArgumentException) { }
            }
            Install(new Progress<double>(_=>{}));
            Process.Start(new ProcessStartInfo(Path.Combine(InstallRoot,"SonaBar.App.exe")){UseShellExecute=true});
        }
        catch(Exception error)
        {
            MessageBox.Show("SonaBar could not install the downloaded update. Your current installation was kept.\n\n"+error.Message,"SonaBar update",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
    }
    private static void Install(IProgress<double> progress)
    {
        using var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("SonaBar.Payload.zip")??throw new InvalidDataException("This installer has no application payload. Build the release package first.");
        using var zip=new ZipArchive(payload,ZipArchiveMode.Read);
        var parent=Path.GetDirectoryName(InstallRoot)!;Directory.CreateDirectory(parent);
        var staging=Path.Combine(parent,"SonaBar-stage-"+Guid.NewGuid());var backup=Path.Combine(parent,"SonaBar-backup-"+Guid.NewGuid());Directory.CreateDirectory(staging);
        try
        {
            long total=zip.Entries.Sum(x=>x.Length),written=0;
            foreach(var entry in zip.Entries)
            {
                var destination=Path.GetFullPath(Path.Combine(staging,entry.FullName));
                if(!destination.StartsWith(staging+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid package path.");
                if(entry.Name.Length==0){Directory.CreateDirectory(destination);continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);entry.ExtractToFile(destination,true);written+=entry.Length;progress.Report(written*100d/Math.Max(1,total));
            }
            File.Copy(Environment.ProcessPath!,Path.Combine(staging,"SonaBar-Uninstall.exe"),true);
            if(Directory.Exists(InstallRoot))Directory.Move(InstallRoot,backup);
            try{Directory.Move(staging,InstallRoot);}catch{if(Directory.Exists(backup))Directory.Move(backup,InstallRoot);throw;}
            if(Directory.Exists(backup))Directory.Delete(backup,true);
        }
        finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    private static void Shortcut(string path)
    {
        var type=Type.GetTypeFromProgID("WScript.Shell")!;dynamic shell=Activator.CreateInstance(type)!;dynamic link=shell.CreateShortcut(path);link.TargetPath=Path.Combine(InstallRoot,"SonaBar.App.exe");link.WorkingDirectory=InstallRoot;link.Description="SonaBar · Speech to Text. Private. Offline.";link.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
    }
    private static void Uninstall()
    {
        if(MessageBox.Show("Uninstall SonaBar? Downloaded models, settings and history will remain in your local app data.","Uninstall SonaBar",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        if(Process.GetProcessesByName("SonaBar.App").Length>0){MessageBox.Show("Quit SonaBar from the system tray, then run uninstall again.");return;}
        // The bootstrapper first relocates itself so every installed file can be removed by this process.
        if(Path.GetFullPath(Environment.ProcessPath!).StartsWith(InstallRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
        {
            var temporary=Path.Combine(Path.GetTempPath(),"SonaBar-Uninstall-"+Guid.NewGuid()+".exe");File.Copy(Environment.ProcessPath!,temporary);Process.Start(new ProcessStartInfo(temporary,"--uninstall"){UseShellExecute=true});return;
        }
        try
        {
            if(Directory.Exists(InstallRoot))Directory.Delete(InstallRoot,true);
            File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"SonaBar.lnk"));File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"SonaBar.lnk"));
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey,false);using var startup=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true);startup?.DeleteValue("SonaBar",false);MessageBox.Show("SonaBar has been uninstalled. Your local data has been preserved.");
        }
        catch(Exception e){MessageBox.Show("Could not finish uninstalling: "+e.Message);}
    }
}
