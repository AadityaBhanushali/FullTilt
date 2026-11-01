using System;
using System.Globalization;
using System.Windows.Controls;

namespace FullTilt;

public sealed record Choice<T>(T Value, string Label);

public sealed class NumericRangeRule : ValidationRule
{
    public bool AllowFraction { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; }
    public override ValidationResult Validate(object value, CultureInfo cultureInfo) =>
        double.TryParse(value?.ToString(), NumberStyles.Float, cultureInfo, out var number) &&
        double.IsFinite(number) && number >= Minimum && number <= Maximum && (AllowFraction || number == Math.Truncate(number))
            ? ValidationResult.ValidResult
            : new ValidationResult(false, AllowFraction ? $"Enter a number from {Minimum:0} to {Maximum:0}." : $"Enter a whole number from {Minimum:0} to {Maximum:0}.");
}
