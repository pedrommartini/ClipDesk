using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClipDesk.PluginSdk.Windows;

/// <summary>Shared responsive button language for v2 Windows plugins.</summary>
public static class PluginButtons
{
    public static Button Create(WindowsPluginViewContext context, string label, bool primary = false)
    {
        var accent = TryBrush(context.AccentColor, "#7C5CFC");
        var color = (accent as SolidColorBrush)?.Color ?? Colors.MediumPurple;
        var luminance = (.2126 * color.R + .7152 * color.G + .0722 * color.B) / 255;
        var foreground = primary ? TryBrush(luminance > .62 ? "#172033" : "#FFFFFF", "#FFFFFF")
            : TryBrush(context.IsDarkMode ? "#EEF4FF" : "#233149", "#FFFFFF");
        var button = new Button
        {
            Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            Tag = "plugin-interactive",
            Cursor = Cursors.Hand,
            Background = primary ? accent : TryBrush(context.IsDarkMode ? "#29384D" : "#E9EFF6", "#29384D"),
            Foreground = foreground,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(Math.Clamp(12 * context.Scale, 9, 36), Math.Clamp(6 * context.Scale, 5, 20),
                Math.Clamp(12 * context.Scale, 9, 36), Math.Clamp(6 * context.Scale, 5, 20)),
            MinHeight = Math.Clamp(34 * context.Scale, 30, 90),
            FontSize = Math.Clamp(13 * context.Scale, 11, 32),
            FontWeight = FontWeights.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var template = new ControlTemplate(typeof(Button));
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.Name = "Surface";
        surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        surface.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(Math.Clamp(11 * context.Scale, 8, 24)));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        surface.AppendChild(presenter);
        template.VisualTree = surface;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, .86));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, .72));
        template.Triggers.Add(pressed);
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, TryBrush(context.IsDarkMode ? "#FFFFFF" : "#243247", "#FFFFFF"), "Surface"));
        focused.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Surface"));
        template.Triggers.Add(focused);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4));
        template.Triggers.Add(disabled);
        button.Template = template;
        context.LayoutChanged += () =>
        {
            button.FontSize = Math.Clamp(13 * context.Scale, 11, 32);
            button.MinHeight = Math.Clamp(34 * context.Scale, 30, 90);
            button.Padding = new Thickness(Math.Clamp(12 * context.Scale, 9, 36), Math.Clamp(6 * context.Scale, 5, 20),
                Math.Clamp(12 * context.Scale, 9, 36), Math.Clamp(6 * context.Scale, 5, 20));
        };
        return button;
    }

    private static Brush TryBrush(string value, string fallback)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        catch (FormatException) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
        catch (NotSupportedException) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
    }
}
