using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ClipDesk.Plugin.AudioRecorder;

public sealed class AudioCaptureService : IDisposable
{
    private WasapiCapture? _micCapture;
    private WasapiLoopbackCapture? _loopbackCapture;
    private WaveFileWriter? _micWriter;
    private WaveFileWriter? _loopbackWriter;

    private string? _tempMicPath;
    private string? _tempLoopbackPath;
    private string? _finalPreviewPath;

    private WaveOutEvent? _playbackPlayer;
    private AudioFileReader? _playbackReader;

    private bool _isRecording;
    private bool _isPaused;

    // Analisador Espectral Real (FFT 512 pontos com balística profissional)
    private const int FftSize = 512;
    private readonly object _fftLock = new();
    private readonly float[] _ringBuffer = new float[FftSize];
    private int _ringPos;
    private readonly float[] _barLevels = new float[16];
    private readonly float[] _fftReal = new float[FftSize];
    private readonly float[] _fftImag = new float[FftSize];

    public bool IsRecording => _isRecording;
    public bool IsPaused => _isPaused;
    public string? CurrentAudioPath => _finalPreviewPath;

    /// <summary>Loads a private preview copy without opening capture devices or starting playback.</summary>
    public void LoadSharedPreview(string path)
    {
        if (_isRecording) throw new InvalidOperationException("Uma gravação local está em andamento.");
        if (!File.Exists(path)) throw new FileNotFoundException("Áudio compartilhado não disponível.");
        CleanupTempFiles();
        var copy = Path.Combine(Path.GetTempPath(), "clipdesk_preview_" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
        File.Copy(path, copy);
        _finalPreviewPath = copy;
    }

    public bool StartRecording(bool recordMic, bool recordSystem)
    {
        if (_isRecording) return false;
        if (!recordMic && !recordSystem) return false;

        CleanupTempFiles();

        lock (_fftLock)
        {
            _ringPos = 0;
            Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
            Array.Clear(_barLevels, 0, _barLevels.Length);
        }

        string tempDir = Path.GetTempPath();
        string guid = Guid.NewGuid().ToString("N");

        try
        {
            if (recordMic)
            {
                try
                {
                    _micCapture = new WasapiCapture();
                    _tempMicPath = Path.Combine(tempDir, $"clipdesk_mic_{guid}.wav");
                    _micWriter = new WaveFileWriter(_tempMicPath, _micCapture.WaveFormat);

                    _micCapture.DataAvailable += (_, e) =>
                    {
                        if (!_isPaused && _micWriter != null && e.BytesRecorded > 0)
                        {
                            _micWriter.Write(e.Buffer, 0, e.BytesRecorded);
                            FeedPcmBytes(e.Buffer, e.BytesRecorded, _micCapture.WaveFormat);
                        }
                    };

                    _micCapture.StartRecording();
                }
                catch
                {
                    _micCapture?.Dispose();
                    _micCapture = null;
                    _micWriter?.Dispose();
                    _micWriter = null;
                }
            }

            if (recordSystem)
            {
                try
                {
                    _loopbackCapture = new WasapiLoopbackCapture();
                    _tempLoopbackPath = Path.Combine(tempDir, $"clipdesk_sys_{guid}.wav");
                    _loopbackWriter = new WaveFileWriter(_tempLoopbackPath, _loopbackCapture.WaveFormat);

                    _loopbackCapture.DataAvailable += (_, e) =>
                    {
                        if (!_isPaused && _loopbackWriter != null && e.BytesRecorded > 0)
                        {
                            _loopbackWriter.Write(e.Buffer, 0, e.BytesRecorded);
                            FeedPcmBytes(e.Buffer, e.BytesRecorded, _loopbackCapture.WaveFormat);
                        }
                    };

                    _loopbackCapture.StartRecording();
                }
                catch
                {
                    _loopbackCapture?.Dispose();
                    _loopbackCapture = null;
                    _loopbackWriter?.Dispose();
                    _loopbackWriter = null;
                }
            }

            if (_micCapture == null && _loopbackCapture == null)
            {
                CleanupTempFiles();
                return false;
            }

            _isRecording = true;
            _isPaused = false;
            return true;
        }
        catch
        {
            StopRecording(1.0f, 1.0f);
            return false;
        }
    }

    public void PauseRecording()
    {
        if (_isRecording)
            _isPaused = true;
    }

    public void ResumeRecording()
    {
        if (_isRecording)
            _isPaused = false;
    }

    public string? StopRecording(float micVolume, float systemVolume, bool micIsStereo = false, bool sysIsStereo = true)
    {
        if (!_isRecording && _finalPreviewPath != null)
            return _finalPreviewPath;

        _isRecording = false;
        _isPaused = false;

        // Parar captura e fechar gravadores
        StopAndDisposeCaptures();

        // Criar o arquivo de preview final mixando as faixas com seus respectivos volumes e canais
        string tempDir = Path.GetTempPath();
        _finalPreviewPath = Path.Combine(tempDir, $"clipdesk_preview_{Guid.NewGuid():N}.wav");

        bool hasMic = _tempMicPath != null && File.Exists(_tempMicPath) && new FileInfo(_tempMicPath).Length > 100;
        bool hasSys = _tempLoopbackPath != null && File.Exists(_tempLoopbackPath) && new FileInfo(_tempLoopbackPath).Length > 100;

        bool masterIsStereo = (hasMic && micIsStereo) || (hasSys && sysIsStereo);
        if (!hasMic && !hasSys) masterIsStereo = micIsStereo || sysIsStereo;
        int targetMasterChannels = masterIsStereo ? 2 : 1;

        try
        {
            if (hasMic && hasSys)
            {
                using var micReader = new AudioFileReader(_tempMicPath!);
                using var sysReader = new AudioFileReader(_tempLoopbackPath!);

                micReader.Volume = Math.Clamp(micVolume, 0f, 1f);
                sysReader.Volume = Math.Clamp(systemVolume, 0f, 1f);

                var micProvider = PrepareChannelSource(micReader, micIsStereo, targetMasterChannels);
                var sysProvider = PrepareChannelSource(sysReader, sysIsStereo, targetMasterChannels);

                var mixer = new MixingSampleProvider(new[] { micProvider, sysProvider });
                WaveFileWriter.CreateWaveFile16(_finalPreviewPath, mixer);
            }
            else if (hasMic)
            {
                using var micReader = new AudioFileReader(_tempMicPath!);
                micReader.Volume = Math.Clamp(micVolume, 0f, 1f);
                var micProvider = PrepareChannelSource(micReader, micIsStereo, micIsStereo ? 2 : 1);
                WaveFileWriter.CreateWaveFile16(_finalPreviewPath, micProvider);
            }
            else if (hasSys)
            {
                using var sysReader = new AudioFileReader(_tempLoopbackPath!);
                sysReader.Volume = Math.Clamp(systemVolume, 0f, 1f);
                var sysProvider = PrepareChannelSource(sysReader, sysIsStereo, sysIsStereo ? 2 : 1);
                WaveFileWriter.CreateWaveFile16(_finalPreviewPath, sysProvider);
            }
            else
            {
                return null;
            }

            return _finalPreviewPath;
        }
        catch
        {
            return hasMic ? _tempMicPath : _tempLoopbackPath;
        }
    }

    private static ISampleProvider PrepareChannelSource(AudioFileReader reader, bool sourceIsStereo, int masterChannels)
    {
        ISampleProvider provider = reader;
        if (provider.WaveFormat.SampleRate != 44100)
        {
            var outFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, provider.WaveFormat.Channels);
            provider = new MediaFoundationResampler(reader, outFormat).ToSampleProvider();
        }

        if (!sourceIsStereo && provider.WaveFormat.Channels >= 2)
        {
            provider = new StereoToMonoSampleProvider(provider);
        }

        if (masterChannels == 2 && provider.WaveFormat.Channels == 1)
        {
            provider = new MonoToStereoSampleProvider(provider);
        }
        else if (masterChannels == 1 && provider.WaveFormat.Channels >= 2)
        {
            provider = new StereoToMonoSampleProvider(provider);
        }

        return provider;
    }

    #region Real-Time Spectrum & Waveform Analysis (FFT)

    private void FeedPcmBytes(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (bytesRecorded <= 0 || buffer.Length == 0) return;

        lock (_fftLock)
        {
            try
            {
                if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    int channels = Math.Max(1, format.Channels);
                    int step = 4 * channels;
                    for (int i = 0; i <= bytesRecorded - step; i += step)
                    {
                        float sample = BitConverter.ToSingle(buffer, i);
                        if (channels >= 2)
                        {
                            sample = (sample + BitConverter.ToSingle(buffer, i + 4)) * 0.5f;
                        }
                        _ringBuffer[_ringPos] = sample;
                        _ringPos = (_ringPos + 1) % FftSize;
                    }
                }
                else if (format.BitsPerSample == 16)
                {
                    int channels = Math.Max(1, format.Channels);
                    int step = 2 * channels;
                    for (int i = 0; i <= bytesRecorded - step; i += step)
                    {
                        short left = BitConverter.ToInt16(buffer, i);
                        float sample = left / 32768f;
                        if (channels >= 2)
                        {
                            short right = BitConverter.ToInt16(buffer, i + 2);
                            sample = (sample + (right / 32768f)) * 0.5f;
                        }
                        _ringBuffer[_ringPos] = sample;
                        _ringPos = (_ringPos + 1) % FftSize;
                    }
                }
            }
            catch { }
        }
    }

    public float[] GetLiveWaveform(int barCount = 16)
    {
        var output = new float[barCount];

        lock (_fftLock)
        {
            bool isActive = (_isRecording && !_isPaused) || IsPlaying;

            if (!isActive)
            {
                // Em repouso ou pausado: barras decaem suavemente até zero
                for (int i = 0; i < barCount; i++)
                {
                    _barLevels[i] = Math.Max(0f, _barLevels[i] * 0.72f);
                    output[i] = _barLevels[i];
                }
                return output;
            }

            // 1. Copiar amostras recentes com janela de Hann
            float sumSquares = 0f;
            for (int i = 0; i < FftSize; i++)
            {
                int idx = (_ringPos + i) % FftSize;
                float val = _ringBuffer[idx];
                sumSquares += val * val;

                float window = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));
                _fftReal[i] = val * window;
                _fftImag[i] = 0f;
            }

            float rms = (float)Math.Sqrt(sumSquares / FftSize);
            if (rms < 0.0003f)
            {
                // Silêncio real: decai rapidamente para zero
                for (int i = 0; i < barCount; i++)
                {
                    _barLevels[i] = Math.Max(0f, _barLevels[i] * 0.70f);
                    output[i] = _barLevels[i];
                }
                return output;
            }

            // 2. Executar FFT real (512 amostras)
            ComputeFft(_fftReal, _fftImag);

            // 3. Faixas logarítmicas de frequência (50Hz - 20kHz)
            int[][] bandBins = new int[][]
            {
                new[] { 1, 1 },      // ~86 Hz (Sub-grave)
                new[] { 2, 2 },      // ~172 Hz (Grave)
                new[] { 3, 4 },      // ~258 - 344 Hz (Grave superior)
                new[] { 5, 6 },      // ~430 - 516 Hz (Corpo da voz)
                new[] { 7, 9 },      // ~602 - 774 Hz (Médio-baixo)
                new[] { 10, 13 },    // ~860 - 1119 Hz (Voz fundamental)
                new[] { 14, 18 },    // ~1.2 - 1.55 kHz (Formantes)
                new[] { 19, 25 },    // ~1.6 - 2.15 kHz (Presença vocal)
                new[] { 26, 35 },    // ~2.2 - 3.0 kHz (Clareza)
                new[] { 36, 48 },    // ~3.1 - 4.1 kHz (Ataque)
                new[] { 49, 66 },    // ~4.2 - 5.7 kHz (Brilho / Sibilantes)
                new[] { 67, 90 },    // ~5.8 - 7.7 kHz (Agudos)
                new[] { 91, 122 },   // ~7.8 - 10.5 kHz (Agudos altos)
                new[] { 123, 160 },  // ~10.6 - 13.8 kHz (Ar)
                new[] { 161, 205 },  // ~13.9 - 17.6 kHz (Ar superior)
                new[] { 206, 254 }   // ~17.7 - 21.8 kHz (Ultra-agudos)
            };

            for (int b = 0; b < barCount && b < bandBins.Length; b++)
            {
                int startBin = bandBins[b][0];
                int endBin = bandBins[b][1];
                float maxMag = 0f;

                for (int k = startBin; k <= endBin && k < 256; k++)
                {
                    float r = _fftReal[k];
                    float im = _fftImag[k];
                    float mag = (float)Math.Sqrt(r * r + im * im);
                    if (mag > maxMag) maxMag = mag;
                }

                // Compensação perceptual: altas frequências na fala têm menor amplitude
                float freqBoost = 1.0f + (b * 0.20f);
                float normalized = (float)Math.Pow(maxMag * freqBoost * 7.0f, 0.78);
                normalized = Math.Clamp(normalized, 0f, 1f);

                // Balística: Ataque instantâneo, decaimento suave
                if (normalized > _barLevels[b])
                {
                    _barLevels[b] = normalized;
                }
                else
                {
                    _barLevels[b] = Math.Max(0f, _barLevels[b] - 0.09f);
                }

                output[b] = Math.Clamp(_barLevels[b], 0f, 1f);
            }
        }

        return output;
    }

    private static void ComputeFft(float[] real, float[] imag)
    {
        int n = real.Length;
        int j = 0;
        for (int i = 0; i < n - 1; i++)
        {
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
            int k = n >> 1;
            while (k <= j)
            {
                j -= k;
                k >>= 1;
            }
            j += k;
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            float wlenRe = (float)Math.Cos(angle);
            float wlenIm = (float)Math.Sin(angle);

            for (int i = 0; i < n; i += len)
            {
                float wRe = 1.0f;
                float wIm = 0.0f;
                int halfLen = len >> 1;
                for (int k = 0; k < halfLen; k++)
                {
                    float uRe = real[i + k];
                    float uIm = imag[i + k];
                    float vRe = real[i + k + halfLen] * wRe - imag[i + k + halfLen] * wIm;
                    float vIm = real[i + k + halfLen] * wIm + imag[i + k + halfLen] * wRe;

                    real[i + k] = uRe + vRe;
                    imag[i + k] = uIm + vIm;
                    real[i + k + halfLen] = uRe - vRe;
                    imag[i + k + halfLen] = uIm - vIm;

                    float nextWRe = wRe * wlenRe - wIm * wlenIm;
                    wIm = wRe * wlenIm + wIm * wlenRe;
                    wRe = nextWRe;
                }
            }
        }
    }

    #endregion

    #region Playback (Preview)

    public bool IsPlaying => _playbackPlayer?.PlaybackState == PlaybackState.Playing;

    public void PlayPreview(Action? onPlaybackStopped = null)
    {
        if (_finalPreviewPath == null || !File.Exists(_finalPreviewPath)) return;

        StopPreview();

        try
        {
            _playbackReader = new AudioFileReader(_finalPreviewPath);

            // Intercepta amostras de áudio durante a reprodução real para alimentar a visualização
            var visualizer = new VisualizingSampleProvider(_playbackReader, (samples, count, channels) =>
            {
                lock (_fftLock)
                {
                    int step = Math.Max(1, channels);
                    for (int i = 0; i <= count - step; i += step)
                    {
                        float sample = samples[i];
                        if (channels >= 2 && i + 1 < count)
                        {
                            sample = (sample + samples[i + 1]) * 0.5f;
                        }
                        _ringBuffer[_ringPos] = sample;
                        _ringPos = (_ringPos + 1) % FftSize;
                    }
                }
            });

            _playbackPlayer = new WaveOutEvent();
            _playbackPlayer.Init(visualizer);
            _playbackPlayer.PlaybackStopped += (_, _) =>
            {
                onPlaybackStopped?.Invoke();
            };
            _playbackPlayer.Play();
        }
        catch
        {
            StopPreview();
        }
    }

    public void PausePreview()
    {
        _playbackPlayer?.Pause();
    }

    public void StopPreview()
    {
        if (_playbackPlayer != null)
        {
            try { _playbackPlayer.Stop(); } catch { }
            _playbackPlayer.Dispose();
            _playbackPlayer = null;
        }
        if (_playbackReader != null)
        {
            try { _playbackReader.Dispose(); } catch { }
            _playbackReader = null;
        }
    }

    #endregion

    public string CommitToDocumentsFolder()
    {
        if (_finalPreviewPath == null || !File.Exists(_finalPreviewPath))
            throw new FileNotFoundException("Nenhum áudio gravado para salvar.");

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string audioDir = Path.Combine(documents, "Clipdesk", "audio recordings");
        Directory.CreateDirectory(audioDir);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string finalFileName = $"Gravacao_{timestamp}_{Guid.NewGuid():N}.wav";
        string finalPath = Path.Combine(audioDir, finalFileName);

        File.Copy(_finalPreviewPath, finalPath);

        CleanupTempFiles();
        return finalPath;
    }

    public void Discard()
    {
        _isRecording = false;
        _isPaused = false;
        StopAndDisposeCaptures();
        StopPreview();
        CleanupTempFiles();
    }

    private void StopAndDisposeCaptures()
    {
        if (_micCapture != null)
        {
            try { _micCapture.StopRecording(); } catch { }
            try { _micCapture.Dispose(); } catch { }
            _micCapture = null;
        }
        if (_micWriter != null)
        {
            try { _micWriter.Flush(); } catch { }
            try { _micWriter.Dispose(); } catch { }
            _micWriter = null;
        }
        if (_loopbackCapture != null)
        {
            try { _loopbackCapture.StopRecording(); } catch { }
            try { _loopbackCapture.Dispose(); } catch { }
            _loopbackCapture = null;
        }
        if (_loopbackWriter != null)
        {
            try { _loopbackWriter.Flush(); } catch { }
            try { _loopbackWriter.Dispose(); } catch { }
            _loopbackWriter = null;
        }
    }

    private void CleanupTempFiles()
    {
        StopPreview();

        if (_tempMicPath != null && File.Exists(_tempMicPath))
        {
            try { File.Delete(_tempMicPath); } catch { }
            _tempMicPath = null;
        }
        if (_tempLoopbackPath != null && File.Exists(_tempLoopbackPath))
        {
            try { File.Delete(_tempLoopbackPath); } catch { }
            _tempLoopbackPath = null;
        }
        if (_finalPreviewPath != null && File.Exists(_finalPreviewPath))
        {
            try { File.Delete(_finalPreviewPath); } catch { }
            _finalPreviewPath = null;
        }
    }

    public void Dispose()
    {
        _isRecording = false;
        _isPaused = false;
        StopAndDisposeCaptures();
        StopPreview();
        CleanupTempFiles();
    }
}

#region Custom Sample Providers (Mono / Stereo / Visualizer)

public sealed class VisualizingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action<float[], int, int> _onSamplesRead;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public VisualizingSampleProvider(ISampleProvider source, Action<float[], int, int> onSamplesRead)
    {
        _source = source;
        _onSamplesRead = onSamplesRead;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read > 0)
        {
            _onSamplesRead(buffer, read, WaveFormat.Channels);
        }
        return read;
    }
}

public sealed class MonoToStereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[]? _sourceBuffer;

    public WaveFormat WaveFormat { get; }

    public MonoToStereoSampleProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int sourceSamplesRequired = count / 2;
        if (_sourceBuffer == null || _sourceBuffer.Length < sourceSamplesRequired)
            _sourceBuffer = new float[sourceSamplesRequired];

        int read = _source.Read(_sourceBuffer, 0, sourceSamplesRequired);
        for (int i = 0; i < read; i++)
        {
            buffer[offset + i * 2] = _sourceBuffer[i];
            buffer[offset + i * 2 + 1] = _sourceBuffer[i];
        }
        return read * 2;
    }
}

public sealed class StereoToMonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[]? _sourceBuffer;

    public WaveFormat WaveFormat { get; }

    public StereoToMonoSampleProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int sourceSamplesRequired = count * 2;
        if (_sourceBuffer == null || _sourceBuffer.Length < sourceSamplesRequired)
            _sourceBuffer = new float[sourceSamplesRequired];

        int read = _source.Read(_sourceBuffer, 0, sourceSamplesRequired);
        int outputSamples = read / 2;
        for (int i = 0; i < outputSamples; i++)
        {
            buffer[offset + i] = (_sourceBuffer[i * 2] + _sourceBuffer[i * 2 + 1]) * 0.5f;
        }
        return outputSamples;
    }
}

#endregion
