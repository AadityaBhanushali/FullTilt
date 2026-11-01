using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace FullTilt;

// Native vector outlines from the supplied full-color logo kit, shared with icon generation.
public sealed class BrandLogo : FrameworkElement
{
    private sealed record LogoAsset(double Width, double Height, string Lime, string Forest, string OffWhite, string Lettermark, string Wordmark);
    private static readonly LogoAsset Asset = ReadAsset();
    internal static readonly Geometry LettermarkGeometry = ReadGeometry(Asset.Lettermark);
    internal static readonly Geometry WordmarkGeometry = ReadGeometry(Asset.Wordmark);
    internal static string Lime => Asset.Lime;
    internal static string Forest => Asset.Forest;
    internal static string OffWhite => Asset.OffWhite;
    public static readonly DependencyProperty LettermarkOnlyProperty = DependencyProperty.Register(
        nameof(LettermarkOnly), typeof(bool), typeof(BrandLogo), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool LettermarkOnly { get => (bool)GetValue(LettermarkOnlyProperty); set => SetValue(LettermarkOnlyProperty, value); }
    private static LogoAsset ReadAsset()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/FullTilt-logo.json")).Stream;
        return JsonSerializer.Deserialize<LogoAsset>(stream) ?? throw new InvalidOperationException("FullTilt logo data is missing.");
    }
    private static Geometry ReadGeometry(string data) { var geometry = Geometry.Parse(data); geometry.Freeze(); return geometry; }
    protected override void OnRender(DrawingContext drawing)
    {
        var bounds = LettermarkOnly ? LettermarkGeometry.Bounds : new Rect(0, 0, Asset.Width, Asset.Height);
        var scale = Math.Min(ActualWidth / bounds.Width, ActualHeight / bounds.Height);
        if (!double.IsFinite(scale) || scale <= 0) return;
        drawing.PushTransform(new TranslateTransform((ActualWidth - bounds.Width * scale) / 2, (ActualHeight - bounds.Height * scale) / 2));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        drawing.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
        drawing.DrawGeometry(ThemePalette.Get(Asset.Lime), null, LettermarkGeometry);
        if (!LettermarkOnly) drawing.DrawGeometry(ThemePalette.Get(Asset.Forest), null, WordmarkGeometry);
        drawing.Pop(); drawing.Pop(); drawing.Pop();
    }
}
