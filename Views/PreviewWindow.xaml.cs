using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipDesk.Models;
using ClipDesk.Services;

namespace ClipDesk.Views;

public partial class PreviewWindow : Window
{
    private readonly ClipboardItem _item;
    private readonly StorageService _storageService;
    private readonly bool _isDarkMode;
    private bool _isClosing;

    public PreviewWindow(ClipboardItem item, StorageService storageService, bool isDarkMode)
    {
        InitializeComponent();
        _item = item;
        _storageService = storageService;
        _isDarkMode = isDarkMode;
        Configure();
        ApplyTheme();
    }

    public bool WasEdited { get; private set; }

    private void Configure()
    {
        TitleText.Text = _item.Type == ClipboardItemType.Text ? "Texto" : _item.DisplayName;
        SaveButton.Visibility = Visibility.Collapsed;

        switch (_item.Type)
        {
            case ClipboardItemType.Text:
                TextEditor.Text = _item.Text ?? string.Empty;
                EditorSurface.Visibility = Visibility.Visible;
                SaveButton.Visibility = Visibility.Visible;
                Loaded += (_, _) => TextEditor.Focus();
                break;
            case ClipboardItemType.Image:
                TitleText.Text = "Imagem";
                PreviewImage.Source = _storageService.LoadBitmap(_item.StoredFilePath);
                ImageSurface.Visibility = Visibility.Visible;
                break;
            case ClipboardItemType.Link:
                TitleText.Text = "Link";
                InfoText.Text = _item.Url ?? string.Empty;
                InfoSurface.Visibility = Visibility.Visible;
                break;
            case ClipboardItemType.File:
            case ClipboardItemType.Folder:
            case ClipboardItemType.AppFolder:
                TitleText.Text = "Origem";
                InfoText.Text = string.Join(Environment.NewLine, _item.FilePaths);
                InfoSurface.Visibility = Visibility.Visible;
                break;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_item.Type != ClipboardItemType.Text)
        {
            return;
        }

        _item.Text = TextEditor.Text;
        WasEdited = true;
        BeginCloseAnimation();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        BeginCloseAnimation();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            BeginCloseAnimation();
            e.Handled = true;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PreviewStack.Opacity = 0;
        PreviewScale.ScaleX = 0.9;
        PreviewScale.ScaleY = 0.9;

        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var pop = new DoubleAnimation(1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 }
        };

        PreviewStack.BeginAnimation(OpacityProperty, fade);
        PreviewScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        PreviewScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void BeginCloseAnimation()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        var duration = TimeSpan.FromMilliseconds(140);
        var fade = new DoubleAnimation(0, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var shrink = new DoubleAnimation(0.94, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fade.Completed += (_, _) => Close();
        PreviewStack.BeginAnimation(OpacityProperty, fade);
        PreviewScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        PreviewScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    private void ApplyTheme()
    {
        var panel = _isDarkMode ? "#F0283040" : "#F0DAE4F4";
        var panelBorder = _isDarkMode ? "#334A5568" : "#99C8D3E2";
        var editor = _isDarkMode ? "#10283040" : "#38FFFFFF";
        var editorBorder = _isDarkMode ? "#22FFFFFF" : "#88B7C4D6";
        var title = _isDarkMode ? "#EFF4FA" : "#2F3847";
        var text = _isDarkMode ? "#F8FAFC" : "#263142";
        var selection = _isDarkMode ? "#806E44E8" : "#706E44E8";
        var actionBackground = _isDarkMode ? "#16FFFFFF" : "#66FFFFFF";
        var actionBorder = _isDarkMode ? "#24FFFFFF" : "#88B7C4D6";

        RootSurface.Background = Brushes.Transparent;
        PreviewPanel.Background = BrushFrom(panel);
        PreviewPanel.BorderBrush = BrushFrom(panelBorder);
        EditorSurface.Background = BrushFrom(editor);
        EditorSurface.BorderBrush = BrushFrom(editorBorder);
        ImageSurface.Background = BrushFrom(editor);
        ImageSurface.BorderBrush = BrushFrom(editorBorder);
        InfoSurface.Background = BrushFrom(editor);
        InfoSurface.BorderBrush = BrushFrom(editorBorder);
        TitleText.Foreground = BrushFrom(title);
        TextEditor.Foreground = BrushFrom(text);
        TextEditor.CaretBrush = BrushFrom(text);
        TextEditor.SelectionBrush = BrushFrom(selection);
        InfoText.Foreground = BrushFrom(text);
        NoteLines.Fill = (Brush)FindResource(_isDarkMode ? "DarkNoteLinesBrush" : "LightNoteLinesBrush");

        foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(PreviewStack))
        {
            button.Background = BrushFrom(actionBackground);
            button.BorderBrush = BrushFrom(actionBorder);
            button.Foreground = BrushFrom(text);
        }
    }

    private static Brush BrushFrom(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    public static bool TryOpenExternally(ClipboardItem item)
    {
        string? target = item.Type switch
        {
            ClipboardItemType.Link => item.Url,
            ClipboardItemType.File or ClipboardItemType.Folder => item.FilePaths.FirstOrDefault(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (item.Type != ClipboardItemType.Link && !File.Exists(target) && !Directory.Exists(target))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = true
        });
        return true;
    }
}
