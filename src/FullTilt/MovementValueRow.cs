using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;

namespace FullTilt;

// One accepted value drives the text field, step buttons and slider. Range
// refreshes never write a coerced slider value back into the settings.
internal sealed class MovementValueRow : Grid
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private readonly Func<(double Min, double Max, double Step, double SliderMax)> _range;
    private readonly StackPanel _label = new();
    private readonly Grid _number = new();
    internal void SetCompact(bool compact)
    {
        Margin = new Thickness(0, compact ? 3 : 9, 0, compact ? 3 : 9);
        if (_label.Children.Count > 1) ((UIElement)_label.Children[1]).Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }
    private readonly TextBlock _unit;
    private bool _refreshing;
    public TextBox Number { get; } = new();
    public Slider Slider { get; } = new();
    public string Title { get; }
    public MovementValueRow(SettingsViewModel model, string title, string detail, string unit, string property,
        Func<double> get, Action<double> set, Func<(double, double, double, double)> range)
    {
        Title = title; _get = get; _set = set; _range = range;
        Margin = new Thickness(0, 4, 0, 4); VerticalAlignment = VerticalAlignment.Center;
        for (var i = 0; i < 2; i++) RowDefinitions.Add(new() { Height = GridLength.Auto });
        foreach (var width in new[] { new GridLength(190), new GridLength(1, GridUnitType.Star), new GridLength(104), new GridLength(44) }) ColumnDefinitions.Add(new() { Width = width });
        _label.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var help = new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 8, 0) }; help.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); _label.Children.Add(help);
        Children.Add(_label);
        Slider.Style = CreateSliderStyle();
        Slider.SetResourceReference(Control.FocusVisualStyleProperty, "FocusRing");
        Slider.Margin = new Thickness(10, 0, 18, 0); Slider.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(Slider, 1); Children.Add(Slider);
        _number.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _number.ColumnDefinitions.Add(new() { Width = new GridLength(20) });
        Number.Padding = new Thickness(9, 8, 22, 8); Number.MinHeight = 38; Number.VerticalContentAlignment = VerticalAlignment.Center;
        SetColumnSpan(Number, 2); _number.Children.Add(Number);
        var arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 3, 1) }; SetColumn(arrows, 1); _number.Children.Add(arrows);
        foreach (var direction in new[] { 1, -1 })
        {
            var button = new Button { Content = direction == 1 ? "⌃" : "⌄", FontSize = 10, Height = 16, MinHeight = 0, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent };
            System.Windows.Automation.AutomationProperties.SetName(button, (direction == 1 ? "Increase " : "Decrease ") + title);
            button.Click += (_, _) => Step(direction); arrows.Children.Add(button);
        }
        SetColumn(_number, 2); Children.Add(_number);
        _unit = new TextBlock { Text = unit, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; _unit.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); SetColumn(_unit, 3); Children.Add(_unit);
        var binding = new Binding(property) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, Converter = WholeNumbers.Converter, StringFormat = "{0:0}" };
        binding.ValidationRules.Add(new RangeRule(this)); Number.SetBinding(TextBox.TextProperty, binding);
        Number.LostFocus += (_, _) => Number.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        Number.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down) { Step(e.Key == Key.Up ? 1 : -1); e.Handled = true; }
            else if (e.Key == Key.Enter) { Number.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); e.Handled = true; }
        };
        Slider.IsSnapToTickEnabled = true; Slider.TickFrequency = 1;
        Slider.ValueChanged += (_, _) =>
        {
            if (_refreshing) return;
            _set(WholeNumbers.Round(Slider.Value)); Refresh();
            Number.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        };
        SizeChanged += (_, _) => LayoutRow(); Refresh();
        System.Windows.Automation.AutomationProperties.SetName(Number, title + " numeric value"); System.Windows.Automation.AutomationProperties.SetName(Slider, title + " slider");
        ToolTip = detail;
    }
    private void Step(int direction)
    {
        var range = WholeRange(); _set(Math.Clamp(WholeNumbers.Round(_get()) + direction * range.Step, range.Min, range.Max));
        Number.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }
    public void Refresh()
    {
        _refreshing = true;
        try { var range = WholeRange(); Slider.Minimum = range.Min; Slider.Maximum = range.SliderMax; Slider.SmallChange = range.Step; Slider.LargeChange = range.Step * 5; Slider.Value = Math.Clamp(WholeNumbers.Round(_get()), range.Min, range.SliderMax); }
        finally { _refreshing = false; }
    }
    private (double Min, double Max, double Step, double SliderMax) WholeRange()
    {
        var range = _range();
        return (WholeNumbers.Round(range.Min), WholeNumbers.Round(range.Max), Math.Max(1, WholeNumbers.Round(range.Step)), WholeNumbers.Round(range.SliderMax));
    }
    private void LayoutRow()
    {
        var compact = ActualWidth < 450;
        ColumnDefinitions[0].Width = compact ? new GridLength(0) : new GridLength(Math.Clamp(ActualWidth * .32, 170, 250));
        SetColumnSpan(_label, compact ? 4 : 1); _label.Margin = compact ? new Thickness(0, 0, 0, 5) : new Thickness(0);
        SetColumn(Slider, compact ? 0 : 1); SetColumnSpan(Slider, compact ? 2 : 1);
        foreach (var element in new UIElement[] { Slider, _number, _unit }) SetRow(element, compact ? 1 : 0);
    }
    private sealed class RangeRule(MovementValueRow owner) : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var range = owner.WholeRange();
            return double.TryParse(value?.ToString(), NumberStyles.Float, cultureInfo, out var number) && double.IsFinite(number) && WholeNumbers.Round(number) >= range.Min && WholeNumbers.Round(number) <= range.Max
                ? ValidationResult.ValidResult : new(false, $"Enter a whole number from {range.Min:0} to {range.Max:0}.");
        }
    }
    internal static Style CreateSliderStyle(bool draftAware = false) => (Style)XamlReader.Parse(draftAware ? SliderStyle.Replace("{DynamicResource ThemeBAEF6C}", "{Binding Foreground, RelativeSource={RelativeSource AncestorType=Slider}}", StringComparison.Ordinal) : SliderStyle);
    private const string SliderStyle = """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
        <Setter Property="MinHeight" Value="32"/><Setter Property="IsMoveToPointEnabled" Value="True"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider"><Grid><Track x:Name="PART_Track" VerticalAlignment="Center">
        <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="5" CornerRadius="2.5" Background="{DynamicResource ThemeBAEF6C}"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="5" CornerRadius="2.5" Background="{DynamicResource ThemeE0E5DC}"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb Width="22" Height="32"><Thumb.Template><ControlTemplate TargetType="Thumb"><Grid><Ellipse Width="18" Height="18" Fill="{DynamicResource Ink}"/><Ellipse Width="16" Height="16" Fill="#FAFBF7"/></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        </Track></Grid><ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        """;
}
