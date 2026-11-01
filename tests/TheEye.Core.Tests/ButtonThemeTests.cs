using System.Xml.Linq;
using TheEye.Presentation.Theming;

namespace TheEye.Core.Tests;

public sealed class ButtonThemeTests
{
    [Fact]
    public void WpfDefaultTextStyle_DoesNotOverrideInheritedButtonForeground()
    {
        using var stream = typeof(ButtonThemeTests).Assembly.GetManifestResourceStream("WpfControls.xaml");
        Assert.NotNull(stream);
        var document = XDocument.Load(stream);
        var ns = document.Root!.Name.Namespace;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var textStyle = document.Root.Elements(ns + "Style").Single(style =>
            (string?)style.Attribute("TargetType") == "{x:Type TextBlock}" && style.Attribute(x + "Key") is null);

        Assert.DoesNotContain(textStyle.Elements(ns + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Foreground");
    }

    [Theory]
    [InlineData("WpfControls.xaml", "Style", "{x:Type Button}", "TextElement.Foreground")]
    [InlineData("WpfControls.xaml", "Style", "{x:Type ToggleButton}", "TextElement.Foreground")]
    [InlineData("AvaloniaControls.axaml", "ControlTheme", "Button", "Foreground")]
    [InlineData("AvaloniaControls.axaml", "ControlTheme", "ToggleButton", "Foreground")]
    public void ButtonTemplates_PassTheControlForegroundToTheirContent(
        string resource, string styleElement, string targetType, string foregroundProperty)
    {
        using var stream = typeof(ButtonThemeTests).Assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);
        var document = XDocument.Load(stream);
        var ns = document.Root!.Name.Namespace;
        var presenters = document.Root.Elements(ns + styleElement)
            .Where(style => (string?)style.Attribute("TargetType") == targetType)
            .SelectMany(style => style.Descendants(ns + "ContentPresenter"))
            .ToArray();

        Assert.NotEmpty(presenters);
        Assert.All(presenters, presenter =>
            Assert.Equal("{TemplateBinding Foreground}", (string?)presenter.Attribute(foregroundProperty)));
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("MidnightBlue")]
    [InlineData("Forest")]
    [InlineData("Sunset")]
    public void ButtonText_HasAtLeastWcagAaContrastInEveryTheme(string theme)
    {
        var palette = ThemeCatalog.Find(theme);
        AssertContrast(palette.AccentText, palette.Accent);
        AssertContrast(palette.AccentText, palette.AccentHover);
        AssertContrast(palette.MutedText, palette.PanelAltBackground);
        AssertContrast(palette.MutedText, palette.PanelBackground);
        AssertContrast(palette.Text, palette.Selection);
    }

    private static void AssertContrast(string foreground, string background)
    {
        var first = Luminance(foreground);
        var second = Luminance(background);
        var ratio = (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
        Assert.True(ratio >= 4.5, $"{foreground} on {background}: {ratio:F2}:1, expected at least 4.5:1");
    }

    private static double Luminance(string hex)
    {
        var (red, green, blue) = ThemeCatalog.ParseRgb(hex);
        return 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);
    }

    private static double Linear(int channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
