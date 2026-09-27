using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ClipDesk.Plugin.ImageUpscaler.Models;

public enum ItemUpscaleStatus
{
    Ready,
    Processing,
    Completed,
    Failed
}

public sealed class UpscaleItem : INotifyPropertyChanged
{
    private string _sourcePath = "";
    private string _fileName = "";
    private long _fileSizeBytes;
    private int _width;
    private int _height;
    private BitmapSource? _thumbnail;
    private ItemUpscaleStatus _status = ItemUpscaleStatus.Ready;
    private double _progress;
    private string? _statusMessage;
    private UpscaleResult? _result;

    public string SourcePath
    {
        get => _sourcePath;
        set => SetField(ref _sourcePath, value);
    }

    public string FileName
    {
        get => _fileName;
        set => SetField(ref _fileName, value);
    }

    public long FileSizeBytes
    {
        get => _fileSizeBytes;
        set => SetField(ref _fileSizeBytes, value);
    }

    public int Width
    {
        get => _width;
        set => SetField(ref _width, value);
    }

    public int Height
    {
        get => _height;
        set => SetField(ref _height, value);
    }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set => SetField(ref _thumbnail, value);
    }

    public ItemUpscaleStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public UpscaleResult? Result
    {
        get => _result;
        set => SetField(ref _result, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static UpscaleItem? FromPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

        try
        {
            var info = new FileInfo(filePath);
            var item = new UpscaleItem
            {
                SourcePath = filePath,
                FileName = info.Name,
                FileSizeBytes = info.Length
            };

            // Read image dimensions safely without loading full huge bitmap
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count > 0)
            {
                var frame = decoder.Frames[0];
                item.Width = frame.PixelWidth;
                item.Height = frame.PixelHeight;

                // Create a lightweight frozen thumbnail (128px)
                stream.Seek(0, SeekOrigin.Begin);
                var thumb = new BitmapImage();
                thumb.BeginInit();
                thumb.CacheOption = BitmapCacheOption.OnLoad;
                thumb.DecodePixelWidth = 160;
                thumb.StreamSource = stream;
                thumb.EndInit();
                thumb.Freeze();
                item.Thumbnail = thumb;
            }

            return item;
        }
        catch
        {
            return null;
        }
    }
}
