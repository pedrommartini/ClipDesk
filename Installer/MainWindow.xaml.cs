using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ClipDesk.Installer;

public partial class MainWindow : Window
{
    private readonly string? _uninstallPath;
    private string _installPath;
    private string _folderBrowserPath = string.Empty;
    private bool _working;
    private InstallResult? _result;

    public MainWindow(string? uninstallPath)
    {
        InitializeComponent();
        BrandTitle.Text = InstallerBrand.AppName;
        ExistingTitle.Text = $"{InstallerBrand.AppName} já está instalado";
        _uninstallPath = uninstallPath;
        var existingPath = InstallerEngine.FindExistingInstallPath();
        _installPath = !InstallerBrand.IsDevelopment && string.Equals(existingPath, InstallerEngine.LegacyInstallPath, StringComparison.OrdinalIgnoreCase)
            ? InstallerEngine.DefaultInstallPath
            : existingPath ?? InstallerEngine.DefaultInstallPath;
        UpdateInstallLocation();
        if (InstallerBrand.IsDevelopment && uninstallPath is null)
        {
            Title = "Instalar ClipDesk DEV";
            ActionTitle.Text = "Pronto para testar?";
            ActionBody.Text = "Instale o ClipDesk DEV com dados isolados para testar colaboração entre dispositivos.";
            InstallButtonText.Text = "Instalar ClipDesk DEV";
            InstallLocationText.Text = $"Ambiente de testes  •  {_installPath}  •  Não altera o ClipDesk de uso diário.";
        }
        if (uninstallPath is not null)
        {
            Title = $"Desinstalar {InstallerBrand.AppName}";
            ActionTitle.Text = $"Remover o {InstallerBrand.AppName}?";
            ActionBody.Text = "O aplicativo será removido deste computador.";
            InstallButtonText.Text = $"Remover {InstallerBrand.AppName}";
            InstallLocationText.Text = "Escolha abaixo se deseja preservar ou apagar os dados locais desta edição.";
            ChooseFolderButton.Visibility = Visibility.Collapsed;
            UninstallDataOptions.Visibility = Visibility.Visible;
        }
        Closed += (_, _) => Application.Current.Shutdown();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_uninstallPath is null && InstallationWillReplaceFiles())
        {
            ExistingLocationText.Text = _installPath;
            ShowState(ExistingState);
            return;
        }
        await RunOperationAsync();
    }

    private async void OverwriteButton_Click(object sender, RoutedEventArgs e) => await RunOperationAsync();
    private void BackFromOverwriteButton_Click(object sender, RoutedEventArgs e) => ShowState(IntroState);
    private async void RetryButton_Click(object sender, RoutedEventArgs e) => await RunOperationAsync();

    private async Task RunOperationAsync()
    {
        if (_working) return;
        _working = true;
        ShowState(ProgressState);
        ProgressTitle.Text = _uninstallPath is null ? $"Instalando o {InstallerBrand.AppName}" : $"Removendo o {InstallerBrand.AppName}";
        var progress = new Progress<InstallProgress>(UpdateProgress);
        try
        {
            if (_uninstallPath is null)
            {
                var preserveData = PreserveDataCheck.IsChecked != false;
                _result = await Task.Run(() => InstallerEngine.InstallAsync(_installPath, testMode: false, progress, preserveData));
                FinishTitle.Text = $"{InstallerBrand.AppName} está pronto";
                FinishBody.Text = "A instalação foi concluída com sucesso.";
                LaunchButton.Content = $"Abrir {InstallerBrand.AppName}";
                LaunchButton.Visibility = Visibility.Visible;
            }
            else
            {
                var deleteData = DeleteDataCheck.IsChecked == true;
                await Task.Run(() => InstallerEngine.UninstallAsync(_uninstallPath, progress, deleteData));
                FinishTitle.Text = $"{InstallerBrand.AppName} removido";
                FinishBody.Text = deleteData ? "O aplicativo e todos os seus dados privados locais foram removidos." : "O aplicativo foi removido. Suas mesas e preferências foram preservadas.";
                LaunchButton.Visibility = Visibility.Collapsed;
            }
            ShowState(FinishState);
        }
        catch (Exception ex)
        {
            ErrorMessage.Text = ex.Message;
            ShowState(ErrorState);
        }
        finally { _working = false; }
    }

    private void UpdateProgress(InstallProgress value)
    {
        ProgressMessage.Text = value.Message;
        ProgressPercent.Text = $"{value.Value * 100:0}%";
        var target = Math.Max(0, value.Value * 392);
        ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void ShowState(UIElement target)
    {
        foreach (var state in new[] { IntroState, ExistingState, ProgressState, FinishState, ErrorState })
        {
            state.BeginAnimation(OpacityProperty, null);
            state.Visibility = state == target ? Visibility.Visible : Visibility.Collapsed;
            state.Opacity = state == target ? 0 : 1;
        }
        target.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_result is not null)
        {
#if CLIPDESK_DEV
            var launch = new ProcessStartInfo(_result.ExecutablePath) { UseShellExecute = true, WorkingDirectory = _result.InstallPath };
#else
            var launch = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            launch.ArgumentList.Add(_result.ExecutablePath);
#endif
            Process.Start(launch);
        }
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_working) Close();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_installPath)?.FullName
            ?? Environment.GetFolderPath(InstallerBrand.IsDevelopment
                ? Environment.SpecialFolder.LocalApplicationData
                : Environment.SpecialFolder.ProgramFiles);
        NavigateFolder(parent);
        FolderPickerOverlay.Visibility = Visibility.Visible;
        FolderPickerOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void CloseFolderPickerButton_Click(object sender, RoutedEventArgs e) => CloseFolderPicker();

    private void CloseFolderPicker()
    {
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) => FolderPickerOverlay.Visibility = Visibility.Collapsed;
        FolderPickerOverlay.BeginAnimation(OpacityProperty, animation);
    }

    private void UpFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_folderBrowserPath);
        if (parent is not null) NavigateFolder(parent.FullName);
        else ShowDriveRoots();
    }

    private void OpenTypedFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var typedPath = Environment.ExpandEnvironmentVariables(FolderPathTextBox.Text.Trim().Trim('"'));
        if (Directory.Exists(typedPath)) NavigateFolder(typedPath);
        else FlashInvalidPath();
    }

    private void FolderList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FolderList.SelectedItem is FolderEntry entry) NavigateFolder(entry.Path);
    }

    private void UseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var chosenParent = Environment.ExpandEnvironmentVariables(FolderPathTextBox.Text.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(chosenParent)) throw new InvalidOperationException();
            _installPath = InstallerEngine.CreateInstallPath(chosenParent);
            UpdateInstallLocation();
            CloseFolderPicker();
        }
        catch
        {
            FlashInvalidPath();
        }
    }

    private void NavigateFolder(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException();
            _folderBrowserPath = fullPath;
            FolderPathTextBox.Text = fullPath;
            FolderPathTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(82, 106, 139));
            var folders = Directory.EnumerateDirectories(fullPath)
                .Select(folder => new FolderEntry("▰", Path.GetFileName(folder), folder))
                .OrderBy(folder => folder.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            FolderList.ItemsSource = folders;
        }
        catch
        {
            FlashInvalidPath();
        }
    }

    private void ShowDriveRoots()
    {
        _folderBrowserPath = string.Empty;
        FolderPathTextBox.Text = string.Empty;
        FolderList.ItemsSource = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Select(drive => new FolderEntry("◆", string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel, drive.RootDirectory.FullName))
            .ToList();
    }

    private void FlashInvalidPath()
    {
        FolderPathTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(237, 105, 137));
        FolderPathTextBox.BeginAnimation(OpacityProperty, new DoubleAnimation(.58, 1, TimeSpan.FromMilliseconds(260)) { AutoReverse = true });
    }

    private bool InstallationWillReplaceFiles()
    {
        if (InstallerEngine.FindExistingInstallPath() is not null) return true;
        if (Directory.Exists(InstallationData.Root)) return true;
        try { return Directory.Exists(_installPath) && Directory.EnumerateFileSystemEntries(_installPath).Any(); }
        catch { return Directory.Exists(_installPath); }
    }

    private void UpdateInstallLocation() => InstallLocationText.Text = $"Destino  •  {_installPath}";

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !IsInteractiveElement(e.OriginalSource as DependencyObject))
            DragMove();
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or TextBox or ListBoxItem or ScrollBar) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private sealed record FolderEntry(string Icon, string DisplayName, string Path);
}
