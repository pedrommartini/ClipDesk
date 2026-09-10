using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ClipDesk.Services;

public sealed class ImageRecognitionService : IDisposable
{
    private const int ImageSize = 224;
    private const int MaxPredictions = 6;
    private readonly InferenceSession? _session;
    private readonly string[] _labels;

    public ImageRecognitionService()
    {
        var modelPath = ResolveModelPath("squeezenet1.1-7.onnx");
        var labelPath = ResolveModelPath("synset.txt");

        if (File.Exists(modelPath) && File.Exists(labelPath))
        {
            _session = new InferenceSession(modelPath);
            _labels = File.ReadAllLines(labelPath);
        }
        else
        {
            _labels = [];
        }
    }

    public string? Describe(BitmapSource source)
    {
        try
        {
            var interfaceTitle = DescribeInterface(source);
            if (interfaceTitle is not null)
            {
                return interfaceTitle;
            }

            if (_session is null || _labels.Length == 0)
            {
                return null;
            }

            var inputName = _session.InputMetadata.Keys.First();
            var input = NamedOnnxValue.CreateFromTensor(inputName, CreateTensor(source));
            using var results = _session.Run([input]);
            var scores = results.First().AsTensor<float>().ToArray();
            var predictions = TopPredictions(scores).ToList();
            if (predictions.Count == 0 || predictions[0].Score < 0.08f)
            {
                return null;
            }

            return ToSimplePortugueseTitle(predictions);
        }
        catch
        {
            return null;
        }
    }

    public string? Describe(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = ImageSize;
            image.EndInit();
            image.Freeze();

            return Describe(image);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
    }

    private static string ResolveModelPath(string fileName)
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Models", fileName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources", "Models", fileName);
    }

    private static DenseTensor<float> CreateTensor(BitmapSource source)
    {
        var bitmap = RenderResized(source);
        var stride = ImageSize * 4;
        var pixels = new byte[stride * ImageSize];
        bitmap.CopyPixels(pixels, stride, 0);

        var tensor = new DenseTensor<float>([1, 3, ImageSize, ImageSize]);
        for (var y = 0; y < ImageSize; y++)
        {
            for (var x = 0; x < ImageSize; x++)
            {
                var pixelIndex = (y * stride) + (x * 4);
                var blue = pixels[pixelIndex] / 255f;
                var green = pixels[pixelIndex + 1] / 255f;
                var red = pixels[pixelIndex + 2] / 255f;

                tensor[0, 0, y, x] = (red - 0.485f) / 0.229f;
                tensor[0, 1, y, x] = (green - 0.456f) / 0.224f;
                tensor[0, 2, y, x] = (blue - 0.406f) / 0.225f;
            }
        }

        return tensor;
    }

    private static BitmapSource RenderResized(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var sourceWidth = Math.Max(1, source.PixelWidth);
            var sourceHeight = Math.Max(1, source.PixelHeight);
            var scale = Math.Max(ImageSize / (double)sourceWidth, ImageSize / (double)sourceHeight);
            var width = sourceWidth * scale;
            var height = sourceHeight * scale;
            var x = (ImageSize - width) / 2;
            var y = (ImageSize - height) / 2;

            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, ImageSize, ImageSize));
            context.DrawImage(source, new Rect(x, y, width, height));
        }

        var target = new RenderTargetBitmap(ImageSize, ImageSize, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();

        return new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
    }

    private static string? DescribeInterface(BitmapSource source)
    {
        var pixels = PixelSnapshot.From(source);
        if (pixels.Total == 0)
        {
            return null;
        }

        var darkRatio = pixels.Ratio(IsDark);
        var lightRatio = pixels.Ratio(IsLight);
        var whiteRatio = pixels.Ratio(IsWhite);
        var grayRatio = pixels.Ratio(IsGray);
        var spotifyGreenRatio = pixels.Ratio(IsSpotifyGreen);
        var whatsappGreenRatio = pixels.Ratio(IsWhatsappGreen);
        var youtubeRedRatio = pixels.Ratio(IsYoutubeRed);
        var discordBlurpleRatio = pixels.Ratio(IsDiscordBlurple);
        var instagramColorRatio = pixels.Ratio(IsInstagramColor);

        var topLightRatio = pixels.RegionRatio(0, 0, pixels.Width, Math.Max(1, pixels.Height / 7), IsLight);
        var topGrayRatio = pixels.RegionRatio(0, 0, pixels.Width, Math.Max(1, pixels.Height / 7), IsGray);
        var bottomSpotifyGreenRatio = pixels.RegionRatio(0, pixels.Height / 2, pixels.Width, pixels.Height - (pixels.Height / 2), IsSpotifyGreen);

        if (LooksLikeSpotify(pixels, darkRatio, whiteRatio, spotifyGreenRatio, bottomSpotifyGreenRatio))
        {
            return "Spotify";
        }

        if (youtubeRedRatio > 0.0025 && (darkRatio > 0.25 || lightRatio > 0.35))
        {
            return "YouTube";
        }

        if (discordBlurpleRatio > 0.01 && darkRatio > 0.35)
        {
            return "Discord";
        }

        if (whatsappGreenRatio > 0.006 && lightRatio > 0.25 && darkRatio < 0.45)
        {
            return "WhatsApp";
        }

        if (instagramColorRatio > 0.012 && lightRatio > 0.15)
        {
            return "Instagram";
        }

        if (LooksLikeCodeEditor(pixels, darkRatio, grayRatio))
        {
            return "Codigo";
        }

        if (LooksLikeDocument(pixels, lightRatio, grayRatio))
        {
            return "Documento";
        }

        if (LooksLikeBrowser(topLightRatio, topGrayRatio, pixels))
        {
            return "Navegador";
        }

        if (LooksLikeAppScreen(pixels, darkRatio, lightRatio, grayRatio))
        {
            return "Tela";
        }

        return null;
    }

    private static bool LooksLikeSpotify(
        PixelSnapshot pixels,
        double darkRatio,
        double whiteRatio,
        double spotifyGreenRatio,
        double bottomSpotifyGreenRatio)
    {
        if (darkRatio > 0.5 && spotifyGreenRatio > 0.0012 && bottomSpotifyGreenRatio > 0.0015 && whiteRatio > 0.01)
        {
            return true;
        }

        foreach (var window in CandidateWindows(pixels))
        {
            var windowDarkRatio = pixels.RegionRatio(window.X, window.Y, window.Size, window.Size, IsUiDark);
            var windowGreenRatio = pixels.RegionRatio(window.X, window.Y, window.Size, window.Size, IsSpotifyGreen);
            var windowWhiteRatio = pixels.RegionRatio(window.X, window.Y, window.Size, window.Size, IsWhite);

            if (windowGreenRatio > 0.006 && windowDarkRatio > 0.14 && windowWhiteRatio > 0.018)
            {
                return true;
            }

            if (windowGreenRatio > 0.002 && windowDarkRatio > 0.42 && windowWhiteRatio > 0.006)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<ScanWindow> CandidateWindows(PixelSnapshot pixels)
    {
        var maxSize = Math.Min(pixels.Width, pixels.Height);
        var sizes = new[] { 48, 60, 76, 96, 124, 160, 220 }
            .Where(size => size <= maxSize)
            .Distinct();

        foreach (var size in sizes)
        {
            var step = Math.Max(8, size / 4);
            for (var y = 0; y <= pixels.Height - size; y += step)
            {
                for (var x = 0; x <= pixels.Width - size; x += step)
                {
                    yield return new ScanWindow(x, y, size);
                }
            }
        }
    }

    private static bool LooksLikeCodeEditor(PixelSnapshot pixels, double darkRatio, double grayRatio)
    {
        if (darkRatio < 0.45 || grayRatio < 0.2)
        {
            return false;
        }

        var coloredTextRatio = pixels.Ratio(pixel =>
            !IsDark(pixel) &&
            !IsGray(pixel) &&
            (pixel.Red > 120 || pixel.Green > 120 || pixel.Blue > 120));

        var leftGutterRatio = pixels.RegionRatio(0, 0, Math.Max(1, pixels.Width / 8), pixels.Height, IsGray);
        return coloredTextRatio > 0.035 && leftGutterRatio > 0.2;
    }

    private static bool LooksLikeDocument(PixelSnapshot pixels, double lightRatio, double grayRatio)
    {
        if (lightRatio < 0.55 || grayRatio < 0.12)
        {
            return false;
        }

        var darkTextRatio = pixels.Ratio(pixel => pixel.Red < 90 && pixel.Green < 90 && pixel.Blue < 90);
        var centerLightRatio = pixels.RegionRatio(pixels.Width / 5, pixels.Height / 8, pixels.Width * 3 / 5, pixels.Height * 3 / 4, IsLight);

        return darkTextRatio > 0.015 && centerLightRatio > 0.55;
    }

    private static bool LooksLikeBrowser(double topLightRatio, double topGrayRatio, PixelSnapshot pixels)
    {
        if (topLightRatio < 0.45 && topGrayRatio < 0.55)
        {
            return false;
        }

        var topDarkTextRatio = pixels.RegionRatio(0, 0, pixels.Width, Math.Max(1, pixels.Height / 7), pixel =>
            pixel.Red < 110 && pixel.Green < 110 && pixel.Blue < 110);

        return topDarkTextRatio > 0.02;
    }

    private static bool LooksLikeAppScreen(PixelSnapshot pixels, double darkRatio, double lightRatio, double grayRatio)
    {
        if (pixels.Width < 240 || pixels.Height < 160)
        {
            return false;
        }

        var strongHorizontalBand = pixels.RegionRatio(0, 0, pixels.Width, Math.Max(1, pixels.Height / 8), IsDark) > 0.65
            || pixels.RegionRatio(0, 0, pixels.Width, Math.Max(1, pixels.Height / 8), IsLight) > 0.55;

        var hasUiContrast = darkRatio > 0.35 && lightRatio > 0.02
            || lightRatio > 0.45 && grayRatio > 0.12;

        return strongHorizontalBand && hasUiContrast;
    }

    private static bool IsDark(Pixel pixel)
    {
        return pixel.Red < 45 && pixel.Green < 45 && pixel.Blue < 45;
    }

    private static bool IsUiDark(Pixel pixel)
    {
        return pixel.Red < 65 && pixel.Green < 65 && pixel.Blue < 65;
    }

    private static bool IsLight(Pixel pixel)
    {
        return pixel.Red > 215 && pixel.Green > 215 && pixel.Blue > 215;
    }

    private static bool IsWhite(Pixel pixel)
    {
        return pixel.Red > 235 && pixel.Green > 235 && pixel.Blue > 235;
    }

    private static bool IsGray(Pixel pixel)
    {
        return Math.Abs(pixel.Red - pixel.Green) < 18
            && Math.Abs(pixel.Green - pixel.Blue) < 18
            && Math.Abs(pixel.Red - pixel.Blue) < 18;
    }

    private static bool IsSpotifyGreen(Pixel pixel)
    {
        return pixel.Green >= 140
            && pixel.Red <= 80
            && pixel.Blue >= 45
            && pixel.Blue <= 155
            && pixel.Green > pixel.Red * 1.6
            && pixel.Green > pixel.Blue;
    }

    private static bool IsWhatsappGreen(Pixel pixel)
    {
        return pixel.Green >= 130
            && pixel.Red <= 80
            && pixel.Blue >= 70
            && pixel.Blue <= 155
            && pixel.Green > pixel.Red * 1.6
            && pixel.Green > pixel.Blue * 1.1;
    }

    private static bool IsYoutubeRed(Pixel pixel)
    {
        return pixel.Red >= 190
            && pixel.Green <= 70
            && pixel.Blue <= 70;
    }

    private static bool IsDiscordBlurple(Pixel pixel)
    {
        return pixel.Blue >= 145
            && pixel.Red >= 70
            && pixel.Red <= 130
            && pixel.Green >= 70
            && pixel.Green <= 130
            && pixel.Blue > pixel.Red
            && pixel.Blue > pixel.Green;
    }

    private static bool IsInstagramColor(Pixel pixel)
    {
        var magenta = pixel.Red > 170 && pixel.Blue > 120 && pixel.Green < 100;
        var orange = pixel.Red > 190 && pixel.Green > 80 && pixel.Green < 170 && pixel.Blue < 90;
        return magenta || orange;
    }

    private IEnumerable<ImagePrediction> TopPredictions(float[] scores)
    {
        return scores
            .Select((score, index) => new { score, index })
            .Where(result => result.index >= 0 && result.index < _labels.Length)
            .OrderByDescending(result => result.score)
            .Take(MaxPredictions)
            .Select(result => new ImagePrediction(RemoveSynsetId(_labels[result.index]).ToLowerInvariant(), result.score));
    }

    private static string ToSimplePortugueseTitle(IReadOnlyList<ImagePrediction> predictions)
    {
        foreach (var prediction in predictions)
        {
            var title = MatchKnownTitle(prediction.Label);
            if (title is not null)
            {
                return title;
            }
        }

        var bestLabel = predictions[0].Label;
        var firstName = bestLabel.Split(',').FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(firstName) ? "Imagem" : ToTitleCaseAscii(firstName);
    }

    private static string? MatchKnownTitle(string label)
    {
        if (Has(label, "laptop", "notebook computer")) return "Notebook";
        if (Has(label, "desktop computer", "computer keyboard", "typewriter keyboard")) return "Computador";
        if (Has(label, "monitor", "screen", "television", "desktop")) return "Tela";
        if (Has(label, "cellular telephone", "mobile phone", "hand-held computer", "remote control")) return "Celular";
        if (Has(label, "keyboard")) return "Teclado";
        if (Has(label, "mouse")) return "Mouse";
        if (Has(label, "camera", "lens", "web site", "projector")) return "Camera";
        if (Has(label, "book", "comic book", "menu", "envelope", "packet", "binder", "notebook", "card", "carton")) return "Documento";
        if (Has(label, "wallet", "purse", "backpack", "mailbag", "handbag")) return "Bolsa";
        if (Has(label, "watch", "clock")) return "Relogio";
        if (Has(label, "car", "cab", "jeep", "limousine", "minivan", "pickup", "racer", "sports car", "ambulance", "truck", "bus", "police van")) return "Carro";
        if (Has(label, "motor scooter", "moped", "motorcycle")) return "Moto";
        if (Has(label, "bicycle", "mountain bike", "tricycle")) return "Bicicleta";
        if (Has(label, "airliner", "airship", "airplane", "warplane")) return "Aviao";
        if (Has(label, "boat", "ship", "canoe", "yawl", "schooner", "speedboat", "gondola")) return "Barco";
        if (Has(label, "train", "locomotive", "streetcar")) return "Trem";
        if (Has(label, "table", "desk", "dining table", "pool table")) return "Mesa";
        if (Has(label, "chair", "sofa", "bench", "seat", "rocking chair")) return "Cadeira";
        if (Has(label, "bed", "pillow", "crib", "quilt")) return "Quarto";
        if (Has(label, "cup", "mug", "bottle", "espresso", "coffee", "teapot", "water bottle", "wine bottle")) return "Bebida";
        if (Has(label, "plate", "pizza", "burger", "hotdog", "sandwich", "banana", "apple", "orange", "strawberry", "lemon", "pineapple", "corn", "broccoli", "ice cream", "cake")) return "Comida";
        if (Has(label, "person", "groom", "bride", "mask", "suit", "jersey")) return "Pessoa";
        if (Has(label, "dog", "retriever", "terrier", "spaniel", "poodle", "shepherd", "husky", "malamute", "hound", "chihuahua", "boxer", "collie", "corgi", "dalmatian")) return "Cachorro";
        if (Has(label, "cat", "tabby", "siamese", "persian", "tiger cat")) return "Gato";
        if (Has(label, "bird", "eagle", "owl", "hen", "cock", "parrot", "macaw", "flamingo", "peacock", "toucan")) return "Passaro";
        if (Has(label, "horse", "zebra")) return "Cavalo";
        if (Has(label, "fish", "goldfish", "shark", "ray")) return "Peixe";
        if (Has(label, "flower", "daisy", "orchid", "rose", "sunflower")) return "Flor";
        if (Has(label, "tree", "forest", "lakeside", "seashore", "valley", "alp", "volcano", "cliff", "mountain", "promontory", "sandbar", "coral reef", "geyser", "waterfall", "beacon")) return "Paisagem";
        if (Has(label, "church", "castle", "palace", "library", "restaurant", "cinema", "theater", "home", "house", "building")) return "Lugar";
        if (Has(label, "soccer ball", "basketball", "baseball", "tennis ball", "golf ball", "racket", "ski", "snowmobile")) return "Esporte";
        if (Has(label, "guitar", "piano", "drum", "violin", "sax", "trumpet", "microphone")) return "Musica";
        if (Has(label, "shirt", "coat", "dress", "shoe", "sandal", "sunglass", "tie", "hat")) return "Roupa";
        if (Has(label, "hammer", "screwdriver", "drill", "saw", "wrench", "lawn mower")) return "Ferramenta";

        return null;
    }

    private static bool Has(string label, params string[] terms)
    {
        return terms.Any(label.Contains);
    }

    private static string RemoveSynsetId(string rawLabel)
    {
        var firstSpace = rawLabel.IndexOf(' ');
        return firstSpace >= 0 ? rawLabel[(firstSpace + 1)..] : rawLabel;
    }

    private static string ToTitleCaseAscii(string value)
    {
        var words = value
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());

        return string.Join(" ", words);
    }

    private sealed record ImagePrediction(string Label, float Score);

    private readonly record struct ScanWindow(int X, int Y, int Size);

    private readonly record struct Pixel(byte Red, byte Green, byte Blue);

    private sealed class PixelSnapshot
    {
        private const int MaxEdge = 360;
        private readonly Pixel[] _pixels;

        private PixelSnapshot(int width, int height, Pixel[] pixels)
        {
            Width = width;
            Height = height;
            _pixels = pixels;
        }

        public int Width { get; }

        public int Height { get; }

        public int Total => _pixels.Length;

        public static PixelSnapshot From(BitmapSource source)
        {
            var scaled = RenderPreview(source);
            var stride = scaled.PixelWidth * 4;
            var bytes = new byte[stride * scaled.PixelHeight];
            scaled.CopyPixels(bytes, stride, 0);

            var pixels = new Pixel[scaled.PixelWidth * scaled.PixelHeight];
            for (var index = 0; index < pixels.Length; index++)
            {
                var byteIndex = index * 4;
                pixels[index] = new Pixel(bytes[byteIndex + 2], bytes[byteIndex + 1], bytes[byteIndex]);
            }

            return new PixelSnapshot(scaled.PixelWidth, scaled.PixelHeight, pixels);
        }

        public double Ratio(Func<Pixel, bool> predicate)
        {
            if (_pixels.Length == 0)
            {
                return 0;
            }

            return _pixels.Count(predicate) / (double)_pixels.Length;
        }

        public double RegionRatio(int x, int y, int width, int height, Func<Pixel, bool> predicate)
        {
            var startX = Math.Clamp(x, 0, Width);
            var startY = Math.Clamp(y, 0, Height);
            var endX = Math.Clamp(x + width, startX, Width);
            var endY = Math.Clamp(y + height, startY, Height);
            var total = 0;
            var matches = 0;

            for (var row = startY; row < endY; row++)
            {
                var rowOffset = row * Width;
                for (var column = startX; column < endX; column++)
                {
                    total++;
                    if (predicate(_pixels[rowOffset + column]))
                    {
                        matches++;
                    }
                }
            }

            return total == 0 ? 0 : matches / (double)total;
        }

        private static BitmapSource RenderPreview(BitmapSource source)
        {
            var sourceWidth = Math.Max(1, source.PixelWidth);
            var sourceHeight = Math.Max(1, source.PixelHeight);
            var scale = Math.Min(1, MaxEdge / (double)Math.Max(sourceWidth, sourceHeight));
            var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                context.DrawImage(source, new Rect(0, 0, width, height));
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();

            return new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        }
    }
}
