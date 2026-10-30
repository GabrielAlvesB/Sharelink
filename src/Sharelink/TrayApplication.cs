using System.Diagnostics;
using Sharelink.Capture;
using Sharelink.Common;
using Sharelink.Gallery;
using Sharelink.Hotkeys;
using Sharelink.Output;
using Sharelink.Recording;
using Sharelink.Settings;
using Sharelink.Theme;
using Sharelink.Upload;

namespace Sharelink;

/// <summary>O app em si: ícone na bandeja, galeria, atalhos globais e as ações de captura/gravação.</summary>
internal sealed class TrayApplication : ApplicationContext, IAppCommands
{
    private const int MenuCloseDelayMs = 250;
    private const int HideWindowDelayMs = 200;

    private readonly NotifyIcon _tray;
    private readonly HotkeyManager _hotkeys = new();
    private readonly RegionRecorder _recorder = new();
    private readonly LinkService _links;
    private readonly CaptureOutput _output;
    private AppSettings _settings;
    private MainForm? _main;
    private RecordingOverlay? _overlay;
    private SettingsForm? _settingsForm;
    private bool _busy;
    private bool _stoppingRecording;
    private string? _balloonFile;

    public TrayApplication(bool startMinimized)
    {
        _settings = SettingsStore.Load(SettingsStore.DefaultPath, out bool firstRun);
        ThemeManager.Initialize(_settings.Theme);
        ThemeManager.Changed += RebuildMenu;
        _links = new LinkService(() => _settings, TrySaveSettings, new LinkStore(LinkStore.DefaultPath), () => ShowSettings(openLinksTab: true));
        _output = new CaptureOutput(() => _settings, _links, Notify);
        _output.FileSaved += _ => _main?.ScheduleRefresh();

        _tray = new NotifyIcon { Icon = AppIcon.Get(), Text = "Sharelink", Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowMainWindow();
        };
        _tray.BalloonTipClicked += (_, _) => RevealFile(_balloonFile);
        _recorder.Failed += message =>
        {
            CloseOverlay();
            ShowError("A gravação parou", message);
        };

        RebuildMenu();
        List<string> failed = RegisterHotkeys();

        if (firstRun)
            TrySaveSettings();
        if (!startMinimized && _settings.ShowGalleryOnStartup)
            ShowMainWindow();
        else if (firstRun)
            Notify("Sharelink está rodando",
                $"Use {_settings.Hotkeys.CaptureRegion} para capturar uma região, ou clique no ícone aqui na bandeja.",
                ToolTipIcon.Info, null);

        if (failed.Count > 0)
            ShowError("Atalhos indisponíveis",
                $"Estes atalhos já estão em uso por outro programa: {string.Join(", ", failed)}.\nTroque-os em Configurações › Atalhos.");
    }

    // ---------- Janela principal ----------

    public void ShowMainWindow()
    {
        if (_main is null || _main.IsDisposed)
        {
            _main = new MainForm(this, () => _settings, _links);
            _main.HiddenToTray += OnMainHidden;
        }
        if (!_main.Visible)
            _main.Show();
        if (_main.WindowState == FormWindowState.Minimized)
            _main.WindowState = FormWindowState.Normal;
        _main.Activate();
    }

    private void OnMainHidden()
    {
        if (_settings.TrayHintShown)
            return;
        _settings.TrayHintShown = true;
        TrySaveSettings();
        _tray.ShowBalloonTip(4000, "O Sharelink continua aberto",
            "Ele fica aqui na bandeja e os atalhos continuam funcionando. Para sair, clique com o botão direito no ícone › Sair.",
            ToolTipIcon.Info);
    }

    /// <summary>Esconde a galeria durante a captura para ela não aparecer no print.</summary>
    private async Task<bool> HideMainForCaptureAsync()
    {
        if (_main is not { Visible: true } main || main.WindowState == FormWindowState.Minimized)
            return false;
        main.Hide();
        await Task.Delay(HideWindowDelayMs);
        return true;
    }

    private void RestoreMain(bool wasVisible)
    {
        if (wasVisible)
            ShowMainWindow();
    }

    // ---------- Menu e atalhos ----------

    private void RebuildMenu()
    {
        HotkeySettings hk = _settings.Hotkeys;
        bool recording = _recorder.IsRecording;
        var menu = new ContextMenuStrip();

        menu.Items.Add(Item("Abrir Sharelink", Glyphs.Image, null, ShowMainWindow, bold: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Capturar região", Glyphs.Camera, hk.CaptureRegion, () => AfterMenu(() => CaptureRegionAsync(false))));
        menu.Items.Add(Item("Capturar e gerar link", Glyphs.Link, hk.CaptureAndLink, () => AfterMenu(() => CaptureRegionAsync(true))));
        menu.Items.Add(Item("Capturar tela inteira", Glyphs.Monitor, hk.CaptureFullScreen, () => AfterMenu(CaptureFullScreenAsync)));
        menu.Items.Add(new ToolStripSeparator());
        if (recording)
        {
            menu.Items.Add(Item("Parar gravação", Glyphs.Stop, _recorder.Format == RecordingFormat.Gif ? hk.RecordGif : hk.RecordMp4, () => _ = StopRecordingAsync()));
        }
        else
        {
            menu.Items.Add(Item("Gravar vídeo (MP4)…", Glyphs.Record, hk.RecordMp4, () => AfterMenu(() => StartRecordingAsync(RecordingFormat.Mp4))));
            menu.Items.Add(Item("Gravar GIF…", Glyphs.Video, hk.RecordGif, () => AfterMenu(() => StartRecordingAsync(RecordingFormat.Gif))));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Abrir pasta de capturas", Glyphs.Folder, null, OpenOutputFolder));
        menu.Items.Add(Item("Configurações…", Glyphs.Settings, null, () => ShowSettings(false)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Sair", Glyphs.Close, null, () => _ = ExitAsync()));
        ThemeManager.Apply(menu);

        ContextMenuStrip? old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
        _tray.Text = recording ? "Sharelink: gravando…" : "Sharelink";
    }

    private static ToolStripMenuItem Item(string text, string glyph, string? shortcut, Action onClick, bool bold = false)
    {
        var item = new ToolStripMenuItem(text, Glyphs.Render(glyph, 16, ThemeManager.Current.Text))
        {
            ShortcutKeyDisplayString = string.IsNullOrEmpty(shortcut) ? null : shortcut,
            Padding = new Padding(0, 3, 0, 3),
        };
        if (bold)
            item.Font = new Font(item.Font, FontStyle.Bold);
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <returns>Atalhos que não puderam ser registrados.</returns>
    private List<string> RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        HotkeySettings hk = _settings.Hotkeys;
        var failed = new List<string>();

        void Register(string text, Action action)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            if (!HotkeyParser.TryParse(text, out Keys keys) || !_hotkeys.Register(keys, action))
                failed.Add(text);
        }

        Register(hk.CaptureRegion, () => Run(() => CaptureRegionAsync(false)));
        Register(hk.CaptureAndLink, () => Run(() => CaptureRegionAsync(true)));
        Register(hk.CaptureFullScreen, () => Run(CaptureFullScreenAsync));
        Register(hk.CaptureActiveWindow, () => Run(CaptureActiveWindowAsync));
        Register(hk.RecordMp4, () => ToggleRecording(RecordingFormat.Mp4));
        Register(hk.RecordGif, () => ToggleRecording(RecordingFormat.Gif));
        return failed;
    }

    /// <summary>Espera o menu da bandeja sumir para ele não aparecer na captura.</summary>
    private async void AfterMenu(Func<Task> action)
    {
        await Task.Delay(MenuCloseDelayMs);
        Run(action);
    }

    /// <summary>Executa uma ação por vez (ignora atalhos enquanto o seletor ou o editor estão abertos).</summary>
    private async void Run(Func<Task> action)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Error("ação", ex);
            ShowError("Algo deu errado", ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------- IAppCommands (botões da galeria) ----------

    void IAppCommands.CaptureRegion(bool generateLink) => Run(() => CaptureRegionAsync(generateLink));

    void IAppCommands.CaptureFullScreen() => Run(CaptureFullScreenAsync);

    void IAppCommands.StartRecording(RecordingFormat format) => ToggleRecording(format);

    public void EditFile(string path) => Run(async () =>
    {
        Bitmap image = ThumbnailCache.LoadImage(path);
        await _output.HandleImageAsync(image, generateLink: false, forceEditor: true);
    });

    // ---------- Capturas ----------

    private async Task CaptureRegionAsync(bool generateLink)
    {
        bool wasVisible = await HideMainForCaptureAsync();
        try
        {
            Rectangle? area = SelectArea(out Bitmap screenshot,
                generateLink ? "Selecione a área para gerar o link  ·  Esc cancela" : "Arraste para selecionar  ·  clique numa janela  ·  Esc cancela");
            using (screenshot)
            {
                if (area is not Rectangle r)
                    return;

                Rectangle screen = ScreenCapturer.VirtualScreen;
                var local = r with { X = r.X - screen.X, Y = r.Y - screen.Y };
                Bitmap crop = screenshot.Clone(local, screenshot.PixelFormat);
                await _output.HandleImageAsync(crop, generateLink);
            }
        }
        finally
        {
            RestoreMain(wasVisible);
        }
    }

    private async Task CaptureFullScreenAsync()
    {
        bool wasVisible = await HideMainForCaptureAsync();
        try
        {
            await _output.HandleImageAsync(ScreenCapturer.CaptureFullScreen(), generateLink: false);
        }
        finally
        {
            RestoreMain(wasVisible);
        }
    }

    private Task CaptureActiveWindowAsync()
    {
        Bitmap? image = ScreenCapturer.CaptureActiveWindow();
        if (image is null)
        {
            Notify("Nenhuma janela ativa", "Clique na janela que quer capturar e tente de novo.", ToolTipIcon.Warning, null);
            return Task.CompletedTask;
        }
        return _output.HandleImageAsync(image, generateLink: false);
    }

    /// <summary>Congela a tela e deixa o usuário escolher uma área. Retorna em coordenadas da tela.</summary>
    private static Rectangle? SelectArea(out Bitmap screenshot, string hint)
    {
        Rectangle screen = ScreenCapturer.VirtualScreen;
        List<Rectangle> windows = ScreenCapturer.GetVisibleWindows();
        screenshot = ScreenCapturer.CaptureRectangle(screen);

        using var selector = new RegionSelectorForm(screenshot, screen, windows, hint);
        return selector.ShowDialog() == DialogResult.OK && selector.SelectedArea is { Width: > 0, Height: > 0 } area
            ? area
            : null;
    }

    // ---------- Gravação ----------

    private void ToggleRecording(RecordingFormat format)
    {
        if (_recorder.IsRecording)
            _ = StopRecordingAsync();
        else
            Run(() => StartRecordingAsync(format));
    }

    private async Task StartRecordingAsync(RecordingFormat format)
    {
        string? ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
        if (ffmpeg is null)
        {
            MessageBox.Show(
                "Para gravar a tela o Sharelink usa o FFmpeg, que não foi encontrado.\n\n" +
                $"Instale abrindo o terminal e rodando:\n\n    {FfmpegLocator.InstallCommand}\n\n" +
                "Depois tente gravar de novo (não precisa reiniciar o Sharelink). " +
                "Se ele estiver em outra pasta, indique o caminho em Configurações › Gravação.",
                "Sharelink: FFmpeg necessário", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool wasVisible = await HideMainForCaptureAsync();
        string hint = format == RecordingFormat.Gif ? "Selecione a área do GIF  ·  Esc cancela" : "Selecione a área do vídeo  ·  Esc cancela";
        Rectangle? selected = SelectArea(out Bitmap screenshot, hint);
        screenshot.Dispose();
        if (selected is not Rectangle area || (area = RectangleMath.MakeEven(area)).Width < 2 || area.Height < 2)
        {
            RestoreMain(wasVisible);
            return;
        }

        // A galeria fica escondida durante a gravação e volta quando ela terminar.
        string output = FileNamer.CreatePath(_settings, DateTime.Now, format == RecordingFormat.Gif ? ".gif" : ".mp4");
        _recorder.Start(ffmpeg, area, format, _settings.VideoFps, _settings.GifFps, _settings.RecordCursor, output);
        _overlay = new RecordingOverlay(area, format, () => _ = StopRecordingAsync());
        _restoreMainAfterRecording = wasVisible;
        RebuildMenu();
    }

    private bool _restoreMainAfterRecording;

    private async Task StopRecordingAsync()
    {
        if (!_recorder.IsRecording || _stoppingRecording)
            return;
        _stoppingRecording = true;
        _overlay?.SetStopping();
        try
        {
            string path = await _recorder.StopAsync();
            CloseOverlay();
            await _output.HandleVideoAsync(path);
        }
        catch (Exception ex)
        {
            Log.Error("parar gravação", ex);
            CloseOverlay();
            ShowError("Falha na gravação", ex.Message);
        }
        finally
        {
            _stoppingRecording = false;
            RebuildMenu();
        }
    }

    private void CloseOverlay()
    {
        _overlay?.Dispose();
        _overlay = null;
        RebuildMenu();
        if (_restoreMainAfterRecording)
        {
            _restoreMainAfterRecording = false;
            ShowMainWindow();
        }
    }

    // ---------- Outros ----------

    private void Notify(string title, string text, ToolTipIcon icon, string? fileToReveal)
    {
        if (!_settings.ShowNotification && icon == ToolTipIcon.Info)
            return;
        _balloonFile = fileToReveal;
        _tray.ShowBalloonTip(3000, title, string.IsNullOrEmpty(text) ? " " : text, icon);
    }

    private void ShowError(string title, string message)
    {
        _balloonFile = null;
        _tray.ShowBalloonTip(8000, title, message.Length > 250 ? message[..250] + "…" : message, ToolTipIcon.Error);
    }

    private static void RevealFile(string? path)
    {
        if (path is null || !File.Exists(path))
            return;
        // O caminho foi gerado e sanitizado pelo próprio app (não pode conter aspas).
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
    }

    public void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.OutputFolder);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { _settings.OutputFolder } });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Não foi possível abrir a pasta", ex.Message);
        }
    }

    public void ShowSettings(bool openLinksTab)
    {
        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }

        string oldFolder = _settings.OutputFolder;
        using var form = new SettingsForm(SettingsStore.Clone(_settings), openLinksTab);
        _settingsForm = form;
        try
        {
            IWin32Window? owner = _main is { Visible: true } ? _main : null;
            if (form.ShowDialog(owner) != DialogResult.OK)
                return;
            _settings = form.Result;
            TrySaveSettings();
            ThemeManager.SetMode(_settings.Theme);
            List<string> failed = RegisterHotkeys();
            RebuildMenu();
            if (!string.Equals(oldFolder, _settings.OutputFolder, StringComparison.OrdinalIgnoreCase))
                _main?.ScheduleRefresh();
            if (failed.Count > 0)
                ShowError("Atalhos indisponíveis", $"Em uso por outro programa: {string.Join(", ", failed)}.");
        }
        finally
        {
            _settingsForm = null;
        }
    }

    private void TrySaveSettings()
    {
        try
        {
            SettingsStore.Save(_settings, SettingsStore.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("salvar configurações", ex);
            ShowError("Não foi possível salvar as configurações", ex.Message);
        }
    }

    private async Task ExitAsync()
    {
        if (_recorder.IsRecording)
            await StopRecordingAsync();
        if (_main is not null)
        {
            _main.AllowClose = true;
            _main.Close();
        }
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.Changed -= RebuildMenu;
            _hotkeys.Dispose();
            _overlay?.Dispose();
            _main?.Dispose();
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
