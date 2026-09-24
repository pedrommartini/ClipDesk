using System.Windows;
using System.Windows.Controls;

namespace ClipDesk.PluginSdk.Windows;

/// <summary>Progress-aware button used by file-processing plugins built with the Windows Dev Kit.</summary>
public sealed class PluginProgressButton : Button
{
    private readonly TextBlock _label = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 3,
        VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
    private string _defaultLabel;
    private bool _completed;

    public PluginProgressButton(WindowsPluginViewContext context, string label, bool primary)
    {
        _defaultLabel = label;
        var sample = PluginButtons.Create(context, label, primary);
        Tag = sample.Tag;
        Cursor = sample.Cursor;
        Background = sample.Background;
        Foreground = sample.Foreground;
        BorderThickness = sample.BorderThickness;
        Padding = sample.Padding;
        MinHeight = sample.MinHeight;
        FontSize = sample.FontSize;
        FontWeight = sample.FontWeight;
        HorizontalContentAlignment = sample.HorizontalContentAlignment;
        Template = sample.Template;
        var content = new Grid();
        content.Children.Add(_label);
        content.Children.Add(_progress);
        Content = content;
        _label.Text = label;
        context.LayoutChanged += () =>
        {
            FontSize = Math.Clamp(13 * context.Scale, 11, 32);
            MinHeight = Math.Clamp(34 * context.Scale, 30, 90);
            Padding = new Thickness(Math.Clamp(12 * context.Scale, 9, 36), Math.Clamp(6 * context.Scale, 5, 20),
                Math.Clamp(12 * context.Scale, 9, 36), Math.Clamp(6 * context.Scale, 5, 20));
        };
    }

    public string DefaultLabel
    {
        get => _defaultLabel;
        set
        {
            _defaultLabel = value ?? string.Empty;
            if (IsEnabled && _progress.Visibility != Visibility.Visible) _label.Text = _defaultLabel;
        }
    }

    public void SetExecuting(bool executing, string label)
    {
        if (executing) _completed = false;
        IsEnabled = !executing;
        _label.Text = executing ? label : DefaultLabel;
        if (!executing) _progress.Visibility = Visibility.Collapsed;
    }

    public void SetProgress(double progress, string? label)
    {
        if (_completed && progress > 1) return;
        _completed = false;
        IsEnabled = false;
        if (!string.IsNullOrWhiteSpace(label)) _label.Text = label;
        _progress.Value = Math.Clamp(progress <= 1 ? progress * 100 : progress, 0, 100);
        _progress.Visibility = Visibility.Visible;
    }

    public void SetSuccess(string label)
    {
        _completed = true;
        IsEnabled = true;
        _label.Text = label;
        _progress.Visibility = Visibility.Collapsed;
    }

    public void SetError(string label)
    {
        _completed = true;
        IsEnabled = true;
        _label.Text = label;
        _progress.Visibility = Visibility.Collapsed;
    }

    public void Reset(string label)
    {
        _completed = false;
        IsEnabled = true;
        _label.Text = string.IsNullOrWhiteSpace(label) ? DefaultLabel : label;
        _progress.Visibility = Visibility.Collapsed;
    }
}
