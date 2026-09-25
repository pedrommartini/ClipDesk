using System.IO;
using System.Net.WebSockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace ClipDesk.Plugin.Tts.Speech;

/// <summary>
/// Client for Microsoft Edge ReadAloud online neural text-to-speech service via WebSocket.
/// Produces natural 48kbps MP3 audio streams without requiring paid Azure Speech keys.
/// </summary>
public sealed class EdgeTtsClient
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string SecMsGecVersion = "1-132.0.2917.0";
    private const string WssEndpoint = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1";

    /// <summary>
    /// Computes the dynamic Sec-MS-GEC token based on Windows FileTime rounded to 5-minute ticks.
    /// </summary>
    public static string GenerateSecMsGec()
    {
        long fileTime = DateTimeOffset.UtcNow.ToFileTime();
        // 5-minute window = 300 seconds * 10,000,000 ticks = 3,000,000,000L
        long ticks = fileTime - (fileTime % 3_000_000_000L);
        string payload = $"{ticks}{TrustedClientToken}";

        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Builds the full WebSocket connection URI with required parameters.
    /// </summary>
    public static Uri BuildWebSocketUri()
    {
        var secMsGec = GenerateSecMsGec();
        var connectionId = Guid.NewGuid().ToString("N");
        return new Uri($"{WssEndpoint}?TrustedClientToken={TrustedClientToken}&Sec-MS-GEC={secMsGec}&Sec-MS-GEC-Version={SecMsGecVersion}&ConnectionId={connectionId}");
    }

    /// <summary>
    /// Generates SSML XML markup for synthesis.
    /// </summary>
    public static string BuildSsml(string text, string voiceName, string language, int rate, int volume)
    {
        var escapedText = SecurityElement.Escape(text) ?? "";
        var ratePercent = rate >= 0 ? $"+{rate * 10}%" : $"{rate * 10}%";
        var volumePercent = volume == 100 ? "+0%" : $"{volume - 100}%";

        var langCode = language;
        if (string.IsNullOrWhiteSpace(langCode))
            langCode = "pt-BR";
        else if (langCode.Length == 2)
        {
            langCode = langCode.ToLowerInvariant() switch
            {
                "pt" => "pt-BR",
                "en" => "en-US",
                "es" => "es-ES",
                _ => "pt-BR"
            };
        }

        return $"""
        <speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{langCode}'>
          <voice name='{voiceName}'>
            <prosody rate='{ratePercent}' volume='{volumePercent}'>
              {escapedText}
            </prosody>
          </voice>
        </speak>
        """;
    }

    /// <summary>
    /// Synthesizes text to MP3 bytes asynchronously using the Edge TTS WebSocket endpoint.
    /// </summary>
    public async Task<byte[]> SynthesizeToMp3Async(
        string text,
        string voiceName,
        string language,
        int rate,
        int volume,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var ws = new ClientWebSocket();

        // Required headers for Edge ReadAloud authentication
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36 Edg/132.0.0.0");
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibfdhpknjhbpfcgidapo");
        ws.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br");
        ws.Options.SetRequestHeader("Accept-Language", "pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7,es;q=0.6");

        var uri = BuildWebSocketUri();
        await ws.ConnectAsync(uri, cancellationToken);

        // 1. Send speech.config message
        const string configMessage = "Content-Type: application/json; charset=utf-8\r\nPath: speech.config\r\n\r\n{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";
        var configBytes = Encoding.UTF8.GetBytes(configMessage);
        await ws.SendAsync(new ArraySegment<byte>(configBytes), WebSocketMessageType.Text, true, cancellationToken);

        // 2. Send ssml message
        var ssml = BuildSsml(text, voiceName, language, rate, volume);
        var requestId = Guid.NewGuid().ToString("N");
        var timestamp = DateTime.UtcNow.ToString("o");
        var ssmlMessage = $"X-RequestId:{requestId}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{timestamp}\r\nPath:ssml\r\n\r\n{ssml}";
        var ssmlBytes = Encoding.UTF8.GetBytes(ssmlMessage);
        await ws.SendAsync(new ArraySegment<byte>(ssmlBytes), WebSocketMessageType.Text, true, cancellationToken);

        // 3. Receive audio stream
        using var audioOutput = new MemoryStream();
        var receiveBuffer = new byte[8192];
        using var messageBuffer = new MemoryStream();

        while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            messageBuffer.SetLength(0);
            WebSocketReceiveResult result;

            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cancellationToken);
                messageBuffer.Write(receiveBuffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                var data = messageBuffer.ToArray();
                if (data.Length >= 2)
                {
                    int headerLength = (data[0] << 8) | data[1];
                    int audioStart = 2 + headerLength;
                    if (data.Length > audioStart)
                    {
                        audioOutput.Write(data, audioStart, data.Length - audioStart);
                    }
                }
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                var textChunk = Encoding.UTF8.GetString(messageBuffer.ToArray());
                if (textChunk.Contains("Path:turn.end", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Completed", CancellationToken.None);
            }
        }
        catch
        {
            // Socket closing errors can be safely ignored
        }

        var audioBytes = audioOutput.ToArray();
        if (audioBytes.Length == 0)
        {
            throw new InvalidOperationException("Nenhum dado de áudio foi retornado pelo serviço neural.");
        }

        return audioBytes;
    }
}
