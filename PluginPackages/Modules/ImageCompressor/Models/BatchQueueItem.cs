using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ClipDesk.Plugin.ImageCompressor.Models;

public enum BatchItemStatus
{
    Pending,
    Compressing,
    Completed,
    Failed
}

/// <summary>
/// Representa um item na fila de processamento em lote da interface gráfica.
/// </summary>
public sealed class BatchQueueItem : INotifyPropertyChanged
{
    private BatchItemStatus _status = BatchItemStatus.Pending;
    private string? _outputPath;
    private int _outputWidth;
    private int _outputHeight;
    private long _compressedSize;
    private long _savedBytes;
    private double _reductionPercentage;
    private string? _errorMessage;
    private BitmapSource? _thumbnail;

    public BatchQueueItem(string sourcePath, long originalSize, int originalWidth = 0, int originalHeight = 0, string? stableId = null)
    {
        Id = stableId ?? Guid.NewGuid().ToString("N");
        SourcePath = sourcePath;
        FileName = Path.GetFileName(sourcePath);
        OriginalSize = originalSize;
        OriginalWidth = originalWidth;
        OriginalHeight = originalHeight;
    }

    public string Id { get; }
    public string SourcePath { get; }
    public string FileName { get; }
    public long OriginalSize { get; }
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set => SetField(ref _thumbnail, value);
    }

    public BatchItemStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string? OutputPath
    {
        get => _outputPath;
        set => SetField(ref _outputPath, value);
    }

    public int OutputWidth
    {
        get => _outputWidth;
        set => SetField(ref _outputWidth, value);
    }

    public int OutputHeight
    {
        get => _outputHeight;
        set => SetField(ref _outputHeight, value);
    }

    public long CompressedSize
    {
        get => _compressedSize;
        set => SetField(ref _compressedSize, value);
    }

    public long SavedBytes
    {
        get => _savedBytes;
        set => SetField(ref _savedBytes, value);
    }

    public double ReductionPercentage
    {
        get => _reductionPercentage;
        set => SetField(ref _reductionPercentage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public void ApplyResult(CompressionResult result)
    {
        if (result.Success)
        {
            Status = BatchItemStatus.Completed;
            OutputPath = result.OutputPath;
            OutputWidth = result.OutputWidth;
            OutputHeight = result.OutputHeight;
            CompressedSize = result.CompressedBytes;
            SavedBytes = result.SavedBytes;
            ReductionPercentage = result.ReductionPercentage;
            ErrorMessage = null;
        }
        else
        {
            Status = BatchItemStatus.Failed;
            ErrorMessage = result.ErrorMessage;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
