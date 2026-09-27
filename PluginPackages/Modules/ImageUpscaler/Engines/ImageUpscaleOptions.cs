using System.IO;
using ClipDesk.Plugin.ImageUpscaler.Models;

namespace ClipDesk.Plugin.ImageUpscaler.Engines;

public sealed class ImageUpscaleOptions
{
    public UpscaleFactor Factor { get; set; } = UpscaleFactor.Scale4x;
    public UpscaleModel Model { get; set; } = UpscaleModel.Balanced;
    public UpscaleEngine Engine { get; set; } = UpscaleEngine.LocalNative;
    public int Sharpness { get; set; } = 65;
    public DenoiseLevel Denoise { get; set; } = DenoiseLevel.Low;
    public OutputFormat Format { get; set; } = OutputFormat.Original;
    public bool FaceEnhance { get; set; } = false;
    public string CloudEndpoint { get; set; } = "";
    public string OutputDirectory { get; set; } = GetDefaultOutputDirectory();

    public static string GetDefaultOutputDirectory()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dir = Path.Combine(docs, "Clipdesk", "Image Upscaler");
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { }
        }
        return dir;
    }

    public static ImageUpscaleOptions FromState(ImageUpscalerState state)
    {
        return new ImageUpscaleOptions
        {
            Factor = state.Factor,
            Model = state.ModelType,
            Engine = state.EngineType,
            Sharpness = state.SharpnessLevel,
            Denoise = state.DenoiseSetting,
            Format = state.Format,
            FaceEnhance = state.FaceEnhanceEnabled,
            CloudEndpoint = state.CloudApiEndpoint,
            OutputDirectory = GetDefaultOutputDirectory()
        };
    }
}
