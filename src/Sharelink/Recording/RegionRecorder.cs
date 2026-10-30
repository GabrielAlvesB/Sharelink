using System.Diagnostics;
using System.Text;
using Sharelink.Common;

namespace Sharelink.Recording;

/// <summary>Controla um processo do FFmpeg gravando uma área da tela.</summary>
internal sealed class RegionRecorder
{
    private readonly SynchronizationContext _ui;
    private readonly StringBuilder _errors = new();
    private Process? _process;
    private bool _stopping;
    private string _ffmpeg = "";
    private string _capturePath = "";
    private string _outputPath = "";
    private int _gifFps;

    public RegionRecorder() =>
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Precisa ser criado na thread da interface.");

    public bool IsRecording => _process is not null;
    public RecordingFormat Format { get; private set; }
    public Rectangle Region { get; private set; }
    public DateTime StartedAt { get; private set; }

    /// <summary>O FFmpeg terminou sozinho (erro) durante a gravação. Disparado na thread da interface.</summary>
    public event Action<string>? Failed;

    public void Start(string ffmpegPath, Rectangle region, RecordingFormat format, int fps, int gifFps, bool drawMouse, string outputPath)
    {
        if (IsRecording)
            throw new InvalidOperationException("Já existe uma gravação em andamento.");

        _ffmpeg = ffmpegPath;
        _outputPath = outputPath;
        _gifFps = gifFps;
        _capturePath = format == RecordingFormat.Gif
            ? Path.Combine(Path.GetTempPath(), $"sharelink_{Guid.NewGuid():N}.mp4")
            : outputPath;
        _errors.Clear();
        _stopping = false;

        Process process = CreateProcess(ffmpegPath,
            FfmpegArguments.Capture(region, format == RecordingFormat.Gif ? Math.Max(gifFps, 15) : fps, drawMouse, _capturePath,
                intermediate: format == RecordingFormat.Gif));
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => _ui.Post(_ => OnExited(process), null);

        process.Start();
        process.BeginErrorReadLine();

        _process = process;
        Format = format;
        Region = region;
        StartedAt = DateTime.Now;
    }

    private Process CreateProcess(string fileName, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
        };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
                return;
            lock (_errors)
            {
                if (_errors.Length < 4000)
                    _errors.AppendLine(e.Data);
            }
        };
        return process;
    }

    private void OnExited(Process process)
    {
        if (_stopping || process != _process)
            return;

        _process = null;
        process.Dispose();
        TryDelete(_capturePath);
        string message = ErrorText("O FFmpeg parou inesperadamente.");
        Log.Info($"Gravação falhou: {message}");
        Failed?.Invoke(message);
    }

    /// <summary>Finaliza a gravação e devolve o caminho do arquivo pronto.</summary>
    public async Task<string> StopAsync()
    {
        Process process = _process ?? throw new InvalidOperationException("Nenhuma gravação em andamento.");
        _stopping = true;

        try
        {
            // "q" pede ao FFmpeg para encerrar e fechar o arquivo corretamente.
            try
            {
                await process.StandardInput.WriteAsync('q');
                await process.StandardInput.FlushAsync();
            }
            catch (IOException)
            {
                // Processo já terminou.
            }

            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    throw new InvalidOperationException("O FFmpeg não respondeu e foi encerrado.");
                }
            }

            if (!File.Exists(_capturePath) || new FileInfo(_capturePath).Length == 0)
                throw new InvalidOperationException(ErrorText("O FFmpeg não gerou o vídeo."));

            if (Format == RecordingFormat.Gif)
            {
                await ConvertToGifAsync();
                TryDelete(_capturePath);
            }
            return _outputPath;
        }
        finally
        {
            process.Dispose();
            _process = null;
            _stopping = false;
        }
    }

    private async Task ConvertToGifAsync()
    {
        _errors.Clear();
        using Process convert = CreateProcess(_ffmpeg, FfmpegArguments.ConvertToGif(_capturePath, _outputPath, _gifFps));
        convert.Start();
        convert.BeginErrorReadLine();
        convert.StandardInput.Close();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await convert.WaitForExitAsync(timeout.Token);
        if (convert.ExitCode != 0 || !File.Exists(_outputPath))
            throw new InvalidOperationException(ErrorText("Falha ao converter para GIF."));
    }

    private string ErrorText(string fallback)
    {
        lock (_errors)
        {
            string detail = _errors.ToString().Trim();
            return detail.Length == 0 ? fallback : $"{fallback}\n\n{detail}";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (path.Contains("sharelink_", StringComparison.Ordinal) && File.Exists(path) &&
                Path.GetDirectoryName(Path.GetFullPath(path)) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar))
                File.Delete(path);
        }
        catch (IOException ex)
        {
            Log.Error("apagar arquivo temporário", ex);
        }
    }
}
