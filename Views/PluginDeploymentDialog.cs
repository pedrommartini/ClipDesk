using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClipDesk.Views;

/// <summary>Small focused confirmation dialog that keeps the deploy password out of persisted state.</summary>
public sealed class PluginDeploymentDialog : Window
{
    private readonly PasswordBox _password = new();
    public string DeployPassword => _password.Password;

    public PluginDeploymentDialog(string packageName, string username)
    {
        Title = "Fazer deploy do plugin";
        Width = 440; MinHeight = 300; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(22, 31, 47));
        Foreground = Brushes.White;

        var root = new Border { Padding = new Thickness(26), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(65, 82, 110)), Background = Background };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Publicar na loja", FontSize = 21, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(Copy($"{packageName}\nSerá publicado por @{username}.", 12, new Thickness(0, 8, 0, 18)));
        stack.Children.Add(Copy("Senha de publicação", 12, new Thickness(0, 0, 0, 6)));
        _password.Height = 38; _password.Padding = new Thickness(10, 7, 10, 7);
        _password.Background = new SolidColorBrush(Color.FromRgb(31, 44, 65));
        _password.Foreground = Brushes.White; _password.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 101, 136));
        _password.KeyDown += (_, e) => { if (e.Key == Key.Enter) Confirm(); };
        stack.Children.Add(_password);
        stack.Children.Add(Copy("A senha só autoriza este deploy e não é salva no ClipDesk.", 11, new Thickness(0, 8, 0, 20)));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Button("Cancelar", null); cancel.Click += (_, _) => DialogResult = false;
        var publish = Button("Publicar", new SolidColorBrush(Color.FromRgb(112, 86, 225))); publish.Margin = new Thickness(8, 0, 0, 0);
        publish.Click += (_, _) => Confirm();
        actions.Children.Add(cancel); actions.Children.Add(publish); stack.Children.Add(actions);
        root.Child = stack; Content = root;
        Loaded += (_, _) => _password.Focus();
    }

    private static TextBlock Copy(string text, double size, Thickness margin) => new()
    {
        Text = text, FontSize = size, Margin = margin, TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(172, 187, 214))
    };

    private static Button Button(string text, Brush? background) => new()
    {
        Content = text, Height = 36, Padding = new Thickness(16, 0, 16, 0), Cursor = Cursors.Hand,
        Background = background ?? new SolidColorBrush(Color.FromRgb(39, 52, 75)), Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(80, 101, 136))
    };

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(_password.Password)) return;
        DialogResult = true;
    }
}
