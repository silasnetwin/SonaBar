using System.Windows.Automation;
namespace Voxora.App;
internal static class Ui
{
    internal static StackPanel Stack(params UIElement[] items) { var panel = new StackPanel(); foreach(var item in items) panel.Children.Add(item); return panel; }
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
        button.Click += async (_,_) => { button.IsEnabled = false; try { await action(); } catch(OperationCanceledException) {} catch(Exception e) { MessageBox.Show(e.Message,"SineFlow",MessageBoxButton.OK,MessageBoxImage.Warning); } finally { button.IsEnabled = true; } }; return button;
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
}
