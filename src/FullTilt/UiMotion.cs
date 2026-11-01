using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FullTilt;

// Presentation only: never changes selection, saving or input timing.
public static class UiMotion
{
    internal static bool? DiagnosticAnimationOverride { get; set; }
    internal static bool Enabled => DiagnosticAnimationOverride ?? SystemParameters.ClientAreaAnimation;
    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.RegisterAttached("Interactive", typeof(bool), typeof(UiMotion), new PropertyMetadata(false, Attach));
    public static void SetInteractive(DependencyObject element, bool value) => element.SetValue(InteractiveProperty, value);
    public static bool GetInteractive(DependencyObject element) => (bool)element.GetValue(InteractiveProperty);
    public static readonly DependencyProperty OffsetXProperty = DependencyProperty.RegisterAttached("OffsetX", typeof(double), typeof(UiMotion), new PropertyMetadata(0d, (owner, args) =>
    {
        if (owner is not FrameworkElement element) return;
        var move = element.RenderTransform as TranslateTransform ?? new TranslateTransform(); element.RenderTransform = move; Animate(move, TranslateTransform.XProperty, (double)args.NewValue);
    }));
    public static void SetOffsetX(DependencyObject element, double value) => element.SetValue(OffsetXProperty, value);
    public static double GetOffsetX(DependencyObject element) => (double)element.GetValue(OffsetXProperty);
    private static readonly DependencyProperty ScaleProperty = DependencyProperty.RegisterAttached("Scale", typeof(ScaleTransform), typeof(UiMotion));
    private static void Attach(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not FrameworkElement element || !(bool)args.NewValue) return;
        // Each control owns its transform; theme brushes and layout remain untouched.
        var scale = new ScaleTransform(1, 1); var group = new TransformGroup();
        if (element.RenderTransform != Transform.Identity) group.Children.Add(element.RenderTransform);
        group.Children.Add(scale); element.RenderTransform = group; element.RenderTransformOrigin = new Point(.5, .5); element.SetValue(ScaleProperty, scale);
        void State() => AnimateScale(scale, element.IsEnabled && element.IsMouseOver ? 1.008 : 1);
        element.MouseEnter += (_, _) => State(); element.MouseLeave += (_, _) => State();
        element.PreviewMouseLeftButtonDown += (_, _) => { if (element.IsEnabled) AnimateScale(scale, .985); };
        element.PreviewMouseLeftButtonUp += (_, _) => State(); element.LostMouseCapture += (_, _) => State();
        element.PreviewKeyDown += (_, e) => { if (element is ButtonBase && element.IsEnabled && e.Key is Key.Space or Key.Return) AnimateScale(scale, .985); };
        element.PreviewKeyUp += (_, e) => { if (element is ButtonBase && e.Key is Key.Space or Key.Return) State(); };
        element.IsEnabledChanged += (_, _) => State();
        element.Unloaded += (_, _) => Reset(scale);
        if (element is Expander expander)
        {
            void Expand(bool opening)
            {
                if ((expander.Template.FindName("Body", expander) ?? expander.Template.FindName("CategoryBody", expander) ?? expander.Template.FindName("ExpandSite", expander)) is not FrameworkElement body) return;
                var height = body.ActualHeight;
                body.BeginAnimation(UIElement.VisibilityProperty, null); body.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
                if (!Enabled) return;
                if (opening) { body.Measure(new Size(Math.Max(1, expander.ActualWidth), double.PositiveInfinity)); height = body.DesiredSize.Height; Reveal(body, 2); }
                else
                {
                    var visibility = new ObjectAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(140), FillBehavior = FillBehavior.Stop };
                    visibility.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    body.BeginAnimation(UIElement.VisibilityProperty, visibility);
                }
                body.BeginAnimation(FrameworkElement.MaxHeightProperty, new DoubleAnimation(opening ? 0 : height, opening ? height : 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
            }
            expander.Expanded += (_, e) => { if (e.OriginalSource == expander) Expand(true); };
            expander.Collapsed += (_, e) => { if (e.OriginalSource == expander) Expand(false); };
        }
        if (element is ComboBox combo) combo.DropDownOpened += (_, _) => { if (combo.Template.FindName("PART_Popup", combo) is Popup { Child: FrameworkElement body }) Reveal(body); };
        if (element is ContextMenu menu) menu.Opened += (_, _) => Reveal(menu);
        if (element is ListBoxItem item) item.Selected += (_, _) => Reveal(item, 2);
        if (element is ToggleButton toggle) { toggle.Checked += (_, _) => Pulse(scale); toggle.Unchecked += (_, _) => Pulse(scale); }
    }
    internal static void AnimateScale(ScaleTransform scale, double target)
    {
        Animate(scale, ScaleTransform.ScaleXProperty, target); Animate(scale, ScaleTransform.ScaleYProperty, target);
    }
    private static void Pulse(ScaleTransform scale) { if (!Enabled) { Reset(scale); return; } scale.ScaleX = scale.ScaleY = 1; Animate(scale, ScaleTransform.ScaleXProperty, 1, .975); Animate(scale, ScaleTransform.ScaleYProperty, 1, .975); }
    private static void Reset(ScaleTransform scale) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleX = scale.ScaleY = 1; }
    internal static void Animate(Animatable target, DependencyProperty property, double value, double? from = null)
    {
        var current = from ?? (double)target.GetValue(property); target.BeginAnimation(property, null); target.SetValue(property, value);
        if (Enabled) target.BeginAnimation(property, new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    internal static void Reveal(FrameworkElement element, double distance = 4)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (element.RenderTransform is TranslateTransform existing) { existing.BeginAnimation(TranslateTransform.YProperty, null); existing.Y = 0; }
        if (!Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.72, 1, TimeSpan.FromMilliseconds(170)) { FillBehavior = FillBehavior.Stop });
        // Do not replace transforms owned by interactive controls.
        if (GetInteractive(element)) return;
        var move = element.RenderTransform as TranslateTransform ?? new TranslateTransform(); element.RenderTransform = move;
        Animate(move, TranslateTransform.YProperty, 0, distance);
    }
}
