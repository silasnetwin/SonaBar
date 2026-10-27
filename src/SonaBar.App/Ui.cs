using System.Windows.Automation;
using System.Windows.Media.Imaging;
namespace SonaBar.App;
internal static class Ui
{
    internal const string SettingsIcon = "M19.43,12.98c0.04,-0.32 0.07,-0.64 0.07,-0.98s-0.03,-0.66 -0.07,-0.98l2.11,-1.65c0.19,-0.15 0.24,-0.42 0.12,-0.64l-2,-3.46c-0.12,-0.22 -0.39,-0.3 -0.61,-0.22l-2.49,1c-0.52,-0.4 -1.08,-0.73 -1.69,-0.98l-0.38,-2.65C14.46,2.18 14.25,2 14,2h-4c-0.25,0 -0.46,0.18 -0.49,0.42l-0.38,2.65c-0.61,0.25 -1.17,0.59 -1.69,0.98l-2.49,-1c-0.23,-0.09 -0.49,0 -0.61,0.22l-2,3.46c-0.13,0.22 -0.07,0.49 0.12,0.64l2.11,1.65c-0.04,0.32 -0.07,0.65 -0.07,0.98s0.03,0.66 0.07,0.98l-2.11,1.65c-0.19,0.15 -0.24,0.42 -0.12,0.64l2,3.46c0.12,0.22 0.39,0.3 0.61,0.22l2.49,-1c0.52,0.4 1.08,0.73 1.69,0.98l0.38,2.65c0.03,0.24 0.24,0.42 0.49,0.42h4c0.25,0 0.46,-0.18 0.49,-0.42l0.38,-2.65c0.61,-0.25 1.17,-0.59 1.69,-0.98l2.49,1c0.23,0.09 0.49,0 0.61,-0.22l2,-3.46c0.12,-0.22 0.07,-0.49 -0.12,-0.64l-2.11,-1.65zM12,15.5A3.5,3.5 0 1 1 12,8.5a3.5,3.5 0 0 1 0,7z";
    internal const string PinIcon = "M16,9V4h1c0.41,0 0.75,-0.34 0.75,-0.75S17.41,2.5 17,2.5H7C6.59,2.5 6.25,2.84 6.25,3.25S6.59,4 7,4h1v5c0,1.66 -1.34,3 -3,3v2h5.97v7l1,1l1,-1v-7H19v-2C17.34,12 16,10.66 16,9z";
    internal const string MinimizeIcon = "M5,12H19";
    internal const string CloseIcon = "M6,6L18,18M18,6L6,18";
    internal static StackPanel Stack(params UIElement[] items) { var panel = new StackPanel(); foreach(var item in items) panel.Children.Add(item); return panel; }
    /// <summary>A vector icon that follows the foreground of the containing button.</summary>
    internal static UIElement Icon(string data, double size = 16, double thickness = 1.7, bool filled = false)
    {
        var geometry = Geometry.Parse(data);
        if (geometry.IsFrozen) geometry = geometry.Clone();
        if (geometry is PathGeometry pathGeometry) pathGeometry.FillRule = FillRule.EvenOdd;
        else if (geometry is StreamGeometry streamGeometry) streamGeometry.FillRule = FillRule.EvenOdd;
        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = filled ? null : Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true
        };
        var foreground = new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1)
        };
        if (filled) path.SetBinding(System.Windows.Shapes.Shape.FillProperty, foreground);
        else path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, foreground);
        return path;
    }
    /// <summary>The product mark, clipped to a circle for the custom title bar.</summary>
    internal static Border Logo(double size = 32)
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri("pack://application:,,,/SonaBar.App;component/Assets/SonaBar-Source.png", UriKind.Absolute);
        source.EndInit();
        source.Freeze();
        var image = new System.Windows.Controls.Image
        {
            Source = source,
            Stretch = Stretch.UniformToFill,
            Clip = new EllipseGeometry(new System.Windows.Point(size / 2, size / 2), size / 2, size / 2)
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            ClipToBounds = true,
            Child = image
        };
    }
    internal static TextBlock Text(string text, double size = 14, bool muted = false) { var block = new TextBlock { Text = text, FontSize = size, FontWeight=size>=16?FontWeights.SemiBold:FontWeights.Normal, Margin = new Thickness(0,0,0,8), LineHeight = size * 1.35 }; if(muted) block.SetResourceReference(TextBlock.ForegroundProperty,"TextSecondary"); return block; }
    internal static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment=HorizontalAlignment.Center }; AutomationProperties.SetName(button,text); if(primary) button.SetResourceReference(Control.StyleProperty,"PrimaryButton");
        button.Click += (_,_) => action(); return button;
    }
    internal static Button AsyncButton(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content=text,HorizontalAlignment=HorizontalAlignment.Left,HorizontalContentAlignment=HorizontalAlignment.Center };
        AutomationProperties.SetName(button,text); if(primary)button.SetResourceReference(Control.StyleProperty,"PrimaryButton");
        button.Click += async (_,_) => { button.IsEnabled = false; try { await action(); } catch(OperationCanceledException) {} catch(Exception e) { MessageBox.Show(e.Message,"SonaBar",MessageBoxButton.OK,MessageBoxImage.Warning); } finally { button.IsEnabled = true; } }; return button;
    }
    internal static WrapPanel Row(params UIElement[] elements) { var panel = new WrapPanel { Margin=new Thickness(0,2,0,10) }; foreach(var element in elements) panel.Children.Add(element); return panel; }
    internal static Border Card(params UIElement[] elements) => Panel(elements);
    internal static Border Panel(params UIElement[] elements) { var panel = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(24,22,24,18), Margin = new Thickness(0,0,0,16), Child = Stack(elements) }; panel.SetResourceReference(Border.BackgroundProperty,"BackgroundElevated"); panel.SetResourceReference(Border.BorderBrushProperty,"Border"); panel.CornerRadius=new CornerRadius(14); return panel; }
    internal static Border Hero(params UIElement[] elements) { var panel=Panel(elements); panel.Padding=new Thickness(28,27,28,22); panel.SetResourceReference(Border.BackgroundProperty,"AccentSoft"); panel.BorderThickness=new Thickness(0); panel.CornerRadius=new CornerRadius(18); return panel; }
    internal static Grid SettingRow(string title, string? description, UIElement control)
    {
        var row = new Grid { Margin = new Thickness(0,0,0,11), MinHeight = 52 }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy=Stack(Text(title,14), Text(description??"",12,true)); copy.Margin=new Thickness(0,0,18,0); if(description is null) copy.Children.RemoveAt(1); row.Children.Add(copy); Grid.SetColumn(control,1); if(control is FrameworkElement element){element.VerticalAlignment=VerticalAlignment.Center;element.Margin=new Thickness(0);element.MaxWidth=210;} row.Children.Add(control); return row;
    }
    internal static CheckBox Check(string title, bool value, Action<bool> set) { var check = new CheckBox { Content = title, IsChecked = value }; check.Checked += (_,_) => set(true); check.Unchecked += (_,_) => set(false); return check; }
    internal static ComboBox Choice<T>(IEnumerable<T> values, T selected, Action<T> set)
    {
        var combo = new ComboBox { ItemsSource = values, SelectedItem = selected }; combo.SelectionChanged += (_,_) => { if(combo.SelectedItem is T item) set(item); }; return combo;
    }
    internal static TextBox Input(string value = "", bool multiline = false) => new() { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 110 : 0, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    internal static Border StatusPill(string text)
    {
        var label=Text(text,11); label.Margin=new Thickness(0); label.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
        var pill=new Border { Child=label, Style=(Style)Application.Current.FindResource("StatusPill") };
        return pill;
    }
    /// <summary>The white ear mark used by the record control in the footer and the overlay.</summary>
    internal static UIElement EarIcon(double size) => new System.Windows.Shapes.Path
    {
        Data=Geometry.Parse("M6,8.5 A6.5,6.5 0 1 1 19,8.5 C19,14.5 13,14.5 13,18.5 A3.5,3.5 0 1 1 6,18.5 M15,8.5 A2.5,2.5 0 0 0 10,8.5 L10,9.5 A2,2 0 1 1 10,13.5"),
        Stroke=Brushes.White,
        StrokeThickness=2.1,
        StrokeStartLineCap=PenLineCap.Round,
        StrokeEndLineCap=PenLineCap.Round,
        Fill=Brushes.Transparent,
        Stretch=Stretch.Uniform,
        Width=size,
        Height=size,
        HorizontalAlignment=HorizontalAlignment.Center,
        VerticalAlignment=VerticalAlignment.Center
    };
}
