using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;

namespace FullTilt;

internal sealed class ThemedDialog : Window
{
    internal static bool RejectDiagnosticPrompts { get; set; }
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;
    public TextBox? NameInput { get; }
    public ThemedDialog(string title, string message, MessageBoxButton choices, string? initialName = null, Func<string, string?>? validate = null, string yesLabel = "Yes", string noLabel = "No")
    {
        Title = title; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        var body = new StackPanel { Margin = new Thickness(24, 12, 24, 24) };
        body.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 18), Foreground = (Brush)FindResource("Muted") });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ThemePalette.Get("#8A3C16"), Margin = new Thickness(0, 6, 0, 10) };
        if (initialName is not null)
        {
            NameInput = new TextBox { Text = initialName, MaxLength = 80, Padding = new Thickness(12, 9, 12, 9) };
            AutomationProperties.SetName(NameInput, "Profile name"); body.Children.Add(NameInput); body.Children.Add(error);
            Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button Add(string label, MessageBoxResult result, bool primary = false)
        {
            var button = new Button { Content = label, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), IsDefault = primary, IsCancel = result == MessageBoxResult.Cancel };
            if (primary) button.Background = ThemePalette.Get("#EAF9CA");
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => { Result = result; if (IsVisible) Close(); }; buttons.Children.Add(button); return button;
        }
        if (NameInput is not null)
        {
            var save = Add(title.StartsWith("New", StringComparison.Ordinal) ? "Create" : "Rename", MessageBoxResult.OK, true);
            void Validate() { error.Text = validate?.Invoke(NameInput.Text) ?? ""; save.IsEnabled = !string.IsNullOrWhiteSpace(NameInput.Text) && error.Text.Length == 0; }
            NameInput.TextChanged += (_, _) => Validate(); Validate(); Add("Cancel", MessageBoxResult.Cancel);
        }
        else
        {
            if (choices is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel) { Add(yesLabel, MessageBoxResult.Yes, true); Add(noLabel, MessageBoxResult.No); }
            else Add("OK", MessageBoxResult.OK, true);
            if (choices is MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel) Add("Cancel", MessageBoxResult.Cancel);
        }
        body.Children.Add(buttons); Content = body; ThemedChrome.Apply(this, false); Loaded += (_, _) => UiMotion.Reveal(body);
    }
    public static MessageBoxResult Ask(Window? owner, string message, string title, MessageBoxButton choices, string yesLabel = "Yes", string noLabel = "No")
    {
        if (RejectDiagnosticPrompts) throw new InvalidOperationException("Unexpected diagnostic prompt: " + title + " · " + message);
        var dialog = new ThemedDialog(title, message, choices, yesLabel: yesLabel, noLabel: noLabel);
        if (owner?.IsVisible == true) dialog.Owner = owner;
        dialog.ShowDialog(); return dialog.Result;
    }
}
