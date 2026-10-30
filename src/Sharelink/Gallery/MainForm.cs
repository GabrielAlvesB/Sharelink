using System.ComponentModel;
using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using Sharelink.Common;
using Sharelink.Controls;
using Sharelink.Output;
using Sharelink.Recording;
using Sharelink.Settings;
using Sharelink.Theme;
using Sharelink.Upload;

namespace Sharelink.Gallery;

/// <summary>
/// Janela principal: galeria com todas as capturas (antigas e novas) e ações de link.
/// Fechar esconde na bandeja; o app continua rodando.
/// </summary>
internal sealed class MainForm : ThemedForm
{
    private const int MaxItems = 2000;

    private readonly IAppCommands _commands;
    private readonly Func<AppSettings> _getSettings;
    private readonly LinkService _links;
    private readonly ToolTip _tips = new();

    private readonly GalleryView _gallery;
    private readonly ThumbnailCache _thumbs;
    private readonly SearchBox _search;
    private readonly ModernButton[] _chips;
    private readonly Label _countLabel;
    private readonly ContextMenuStrip _itemMenu;
    private readonly Panel _updateBanner;
    private readonly Label _updateLabel;

    private readonly PreviewBox _preview;
    private readonly Label _title;
    private readonly Label _meta;
    private readonly Label _destinationLabel;
    private readonly TextBox _linkBox;
    private readonly ModernButton _btnCreateLink, _btnCopyLink, _btnOpenLink, _btnDeleteLink;
    private readonly ModernButton _btnCopy, _btnEdit, _btnReveal, _btnDelete;

    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 400 };
    private readonly Stack<GalleryItem> _thumbQueue = new();
    private readonly HashSet<string> _thumbPending = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private string _watchedFolder = "";
    private List<GalleryItem> _all = [];
    private GalleryFilter _filter = GalleryFilter.All;
    private bool _pumping;
    private int _previewVersion;
    private bool _working;
    private bool _detailsPending;

    /// <summary>Quando true, fechar a janela encerra de verdade (usado ao sair do app).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    /// <summary>A janela foi fechada pelo usuário e foi para a bandeja.</summary>
    public event Action? HiddenToTray;

    public MainForm(IAppCommands commands, Func<AppSettings> getSettings, LinkService links)
    {
        _commands = commands;
        _getSettings = getSettings;
        _links = links;

        Text = "Sharelink";
        ClientSize = new Size(1180, 740);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        float scale = DeviceDpi / 96f;
        HotkeySettings hk = getSettings().Hotkeys;

        // ---------- Cabeçalho ----------
        var header = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(20, 14, 20, 14) };
        ThemeManager.SetRole(header, ThemeRole.Surface);
        var logo = new PictureBox { Image = AppIcon.Get().ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(32, 32), Dock = DockStyle.Left };
        var appName = new Label { Text = "Sharelink", Font = new Font("Segoe UI Semibold", 15f), AutoSize = true, Dock = DockStyle.Left, Padding = new Padding(8, 2, 0, 0) };
        _search = new SearchBox { Dock = DockStyle.Right, Width = 300, Placeholder = "Buscar capturas pelo nome…" };
        _search.TextChanged += (_, _) => ApplyFilter();
        var settingsButton = IconButton(Glyphs.Settings, "Configurações", () => _commands.ShowSettings(false));
        settingsButton.Dock = DockStyle.Right;
        var folderButton = IconButton(Glyphs.Folder, "Abrir pasta de capturas", _commands.OpenOutputFolder);
        folderButton.Dock = DockStyle.Right;
        // O último adicionado encaixa primeiro: logo na ponta esquerda, configurações na ponta direita.
        header.Controls.AddRange([_search, Spacer(8), folderButton, Spacer(4), settingsButton, appName, logo]);

        // ---------- Aviso de atualização (escondido até haver uma) ----------
        _updateBanner = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(20, 8, 20, 8), Visible = false };
        _updateBanner.Paint += (_, e) =>
        {
            Palette p = ThemeManager.Current;
            e.Graphics.Clear(Color.FromArgb(p.IsDark ? 45 : 30, p.Accent));
        };
        _updateLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
        var updateButton = new ModernButton { Text = "Atualizar agora", Glyph = Glyphs.Upload, Variant = ButtonVariant.Primary, Dock = DockStyle.Right, Height = 32, AutoWidth = true };
        updateButton.Click += (_, _) => _commands.InstallUpdate();
        _updateBanner.Controls.Add(_updateLabel);
        _updateBanner.Controls.Add(updateButton);

        // ---------- Ações ----------
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(18, 14, 18, 8), WrapContents = false };
        actions.Controls.AddRange(
        [
            ActionButton("Capturar região", Glyphs.Camera, ButtonVariant.Primary, $"Selecionar uma área ou clicar numa janela ({hk.CaptureRegion})", () => _commands.CaptureRegion(false)),
            ActionButton("Capturar e gerar link", Glyphs.Link, ButtonVariant.Secondary, $"Captura uma área e já copia o link público ({hk.CaptureAndLink})", () => _commands.CaptureRegion(true)),
            ActionButton("Tela inteira", Glyphs.Monitor, ButtonVariant.Secondary, $"Todos os monitores ({hk.CaptureFullScreen})", _commands.CaptureFullScreen),
            ActionButton("Gravar vídeo", Glyphs.Record, ButtonVariant.Secondary, $"Gravar uma área em MP4 ({hk.RecordMp4})", () => _commands.StartRecording(RecordingFormat.Mp4)),
            ActionButton("Gravar GIF", Glyphs.Video, ButtonVariant.Secondary, $"Gravar uma área em GIF ({hk.RecordGif})", () => _commands.StartRecording(RecordingFormat.Gif)),
        ]);

        // ---------- Filtros ----------
        var filterBar = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(20, 6, 20, 8) };
        var chipFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        string[] chipLabels = ["Todas", "Com link", "Imagens", "Vídeos e GIFs"];
        _chips = chipLabels.Select((label, i) =>
        {
            var chip = new ModernButton { Text = label, Variant = ButtonVariant.Chip, Height = 30, Margin = new Padding(0, 0, 8, 0), Checked = i == 0, AutoWidth = true };
            chip.Click += (_, _) => SetFilter((GalleryFilter)i);
            return chip;
        }).ToArray();
        chipFlow.Controls.AddRange(_chips);
        _countLabel = new Label { Dock = DockStyle.Right, AutoSize = true, Padding = new Padding(0, 7, 0, 0) };
        ThemeManager.SetRole(_countLabel, ThemeRole.Muted);
        filterBar.Controls.Add(chipFlow);
        filterBar.Controls.Add(_countLabel);

        // ---------- Galeria ----------
        var thumbSize = new Size((int)(200 * scale), (int)(128 * scale));
        _thumbs = new ThumbnailCache(thumbSize);
        _gallery = new GalleryView
        {
            Dock = DockStyle.Fill,
            ThumbnailSize = thumbSize,
            GetThumbnail = _thumbs.Get,
            HasLink = item => LinkOf(item) is not null,
        };
        _gallery.ThumbnailRequested += OnThumbnailRequested;
        _gallery.SelectionChanged += (_, _) => ScheduleDetailsUpdate();
        _gallery.ItemActivated += (_, _) => OpenSelectedFile();
        _gallery.KeyDown += Gallery_KeyDown;
        _itemMenu = BuildItemMenu();
        _gallery.ContextMenuStrip = _itemMenu;

        // ---------- Painel de detalhes ----------
        _preview = new PreviewBox { Dock = DockStyle.Fill, Height = 180, Margin = new Padding(0, 0, 0, 14), Placeholder = "Selecione uma captura" };
        _title = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 11.5f), Margin = new Padding(0, 0, 0, 2), AutoEllipsis = true };
        _meta = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
        ThemeManager.SetRole(_meta, ThemeRole.Muted);

        Label linkSection = SectionLabel("LINK PÚBLICO");
        _destinationLabel = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        ThemeManager.SetRole(_destinationLabel, ThemeRole.Muted);
        _linkBox = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        _btnCreateLink = DetailButton("Gerar link", Glyphs.Upload, ButtonVariant.Primary, async () => await CreateLinksAsync());
        _btnCreateLink.Margin = new Padding(0, 0, 0, 6);
        _btnCopyLink = DetailButton("Copiar", Glyphs.Copy, ButtonVariant.Secondary, CopyLinks);
        _btnOpenLink = DetailButton("Abrir", Glyphs.OpenExternal, ButtonVariant.Secondary, OpenLink);
        _btnDeleteLink = DetailButton("Apagar", Glyphs.Delete, ButtonVariant.Danger, async () => await DeleteLinksAsync());
        _tips.SetToolTip(_btnDeleteLink, "Remove o arquivo da nuvem; o link deixa de funcionar");

        Label fileSection = SectionLabel("ARQUIVO");
        _btnCopy = DetailButton("Copiar", Glyphs.Copy, ButtonVariant.Secondary, CopySelected);
        _btnEdit = DetailButton("Editar", Glyphs.Edit, ButtonVariant.Secondary, EditSelected);
        _btnReveal = DetailButton("Na pasta", Glyphs.Folder, ButtonVariant.Secondary, RevealSelected);
        _btnDelete = DetailButton("Excluir", Glyphs.Delete, ButtonVariant.Danger, async () => await DeleteFilesAsync());
        _tips.SetToolTip(_btnEdit, "Abre no editor e salva como uma nova captura");
        _tips.SetToolTip(_btnDelete, "Envia para a Lixeira (Del)");

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(18, 18, 18, 12), AutoScroll = true };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (Control c in new Control[]
                 {
                     _preview, _title, _meta, linkSection, _destinationLabel, _linkBox, _btnCreateLink,
                     Row(_btnCopyLink, _btnOpenLink, _btnDeleteLink), fileSection, Row(_btnCopy, _btnEdit), Row(_btnReveal, _btnDelete),
                 })
        {
            details.RowStyles.Add(new RowStyle(c == _preview ? SizeType.Absolute : SizeType.AutoSize, c == _preview ? 180 * scale : 0));
            if (c is Button)
                c.Dock = DockStyle.Fill;
            details.Controls.Add(c);
        }
        // Linha final elástica: as linhas de botões ficam com a altura natural.
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.Controls.Add(new Panel { Height = 1, Margin = Padding.Empty });
        details.Resize += (_, _) => _title.MaximumSize = new Size(details.ClientSize.Width - details.Padding.Horizontal, 0);

        var detailsHost = new Panel { Dock = DockStyle.Right, Width = (int)(340 * scale) };
        ThemeManager.SetRole(detailsHost, ThemeRole.Surface);
        ThemeManager.SetRole(details, ThemeRole.Surface);
        detailsHost.Controls.Add(details);
        var divider = new Panel { Dock = DockStyle.Right, Width = 1 };
        divider.Paint += (_, e) => e.Graphics.Clear(ThemeManager.Current.Border);

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(_gallery);
        body.Controls.Add(divider);
        body.Controls.Add(detailsHost);

        Controls.Add(body);
        Controls.Add(filterBar);
        Controls.Add(actions);
        Controls.Add(_updateBanner);
        Controls.Add(header);

        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            _ = ReloadAsync();
        };
        _links.Store.Changed += OnLinkChanged;

        UpdateDetails();
    }

    public void ShowUpdateBanner(string version)
    {
        _updateLabel.Text = $"Sharelink {version} já foi baixado e verificado. Clique em \"Atualizar agora\": o app fecha, instala e reabre sozinho.";
        _updateBanner.Visible = true;
    }

    // ---------- Montagem da interface ----------

    private static Panel Spacer(int width) => new() { Dock = DockStyle.Right, Width = width };

    private ModernButton IconButton(string glyph, string tip, Action action)
    {
        var button = new ModernButton { Glyph = glyph, Variant = ButtonVariant.Ghost, Width = 36, Height = 36, Padding = Padding.Empty };
        _tips.SetToolTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private ModernButton ActionButton(string text, string glyph, ButtonVariant variant, string tip, Action action)
    {
        var button = new ModernButton { Text = text, Glyph = glyph, Variant = variant, Height = 38, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(14, 0, 16, 0), AutoWidth = true };
        _tips.SetToolTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private static ModernButton DetailButton(string text, string glyph, ButtonVariant variant, Action action)
    {
        var button = new ModernButton { Text = text, Glyph = glyph, Variant = variant, Height = 36, Margin = new Padding(0, 0, 6, 6), Padding = new Padding(8, 0, 8, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Label SectionLabel(string text)
    {
        var label = new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 8.25f), Margin = new Padding(0, 6, 0, 6) };
        ThemeManager.SetRole(label, ThemeRole.Muted);
        return label;
    }

    /// <summary>Linha de botões dividindo a largura igualmente.</summary>
    private static TableLayoutPanel Row(params Control[] controls)
    {
        var row = new TableLayoutPanel { ColumnCount = controls.Length, RowCount = 1, Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
        ThemeManager.SetRole(row, ThemeRole.Surface);
        for (int i = 0; i < controls.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
            controls[i].Dock = DockStyle.Fill;
            if (i == controls.Length - 1)
                controls[i].Margin = controls[i].Margin with { Right = 0 };
            row.Controls.Add(controls[i], i, 0);
        }
        return row;
    }

    private ContextMenuStrip BuildItemMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = true };
        Palette p = ThemeManager.Current;
        ToolStripMenuItem Item(string text, string glyph, EventHandler click, string? shortcut = null) =>
            new(text, Glyphs.Render(glyph, 16, p.Text), click) { ShortcutKeyDisplayString = shortcut };

        var open = Item("Abrir", Glyphs.OpenExternal, (_, _) => OpenSelectedFile(), "Enter");
        open.Font = new Font(menu.Font, FontStyle.Bold);
        var createLink = Item("Gerar link", Glyphs.Upload, async (_, _) => await CreateLinksAsync());
        var copyLink = Item("Copiar link", Glyphs.Link, (_, _) => CopyLinks());
        var deleteLink = Item("Apagar link", Glyphs.Close, async (_, _) => await DeleteLinksAsync());
        var copy = Item("Copiar", Glyphs.Copy, (_, _) => CopySelected(), "Ctrl+C");
        var edit = Item("Editar", Glyphs.Edit, (_, _) => EditSelected());
        var reveal = Item("Mostrar na pasta", Glyphs.Folder, (_, _) => RevealSelected());
        var delete = Item("Excluir", Glyphs.Delete, async (_, _) => await DeleteFilesAsync(), "Del");

        menu.Items.AddRange([open, new ToolStripSeparator(), createLink, copyLink, deleteLink, new ToolStripSeparator(), copy, edit, reveal, new ToolStripSeparator(), delete]);
        menu.Opening += (_, e) =>
        {
            List<GalleryItem> selected = Selected;
            if (selected.Count == 0 || _working)
            {
                e.Cancel = true;
                return;
            }
            int linked = selected.Count(i => LinkOf(i) is not null);
            open.Enabled = selected.Count == 1;
            createLink.Enabled = linked < selected.Count;
            copyLink.Enabled = deleteLink.Enabled = linked > 0;
            edit.Enabled = selected.Count == 1 && selected[0].Kind == GalleryItemKind.Image;
        };
        return menu;
    }

    protected override void OnThemeApplied()
    {
        // Os ícones do menu são bitmaps: recria com a cor do tema novo.
        Palette p = ThemeManager.Current;
        string[] glyphs = [Glyphs.OpenExternal, Glyphs.Upload, Glyphs.Link, Glyphs.Close, Glyphs.Copy, Glyphs.Edit, Glyphs.Folder, Glyphs.Delete];
        int g = 0;
        foreach (ToolStripItem item in _itemMenu.Items)
        {
            if (item is ToolStripMenuItem menuItem && g < glyphs.Length)
            {
                menuItem.Image?.Dispose();
                menuItem.Image = Glyphs.Render(glyphs[g++], 16, p.Text);
            }
        }
    }

    // ---------- Carregamento ----------

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await ReloadAsync();
        _gallery.Focus();
    }

    /// <summary>Pede uma nova varredura (agrupa várias mudanças seguidas).</summary>
    public void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    public async Task ReloadAsync()
    {
        string folder = _getSettings().OutputFolder;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("criar pasta de capturas", ex);
        }

        _all = await Task.Run(() => GalleryScanner.Scan(folder));
        WatchFolder(folder);
        ApplyFilter();
    }

    private void WatchFolder(string folder)
    {
        if (string.Equals(folder, _watchedFolder, StringComparison.OrdinalIgnoreCase) && _watcher is not null)
            return;

        _watcher?.Dispose();
        _watcher = null;
        _watchedFolder = folder;
        if (!Directory.Exists(folder))
            return;

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            SynchronizingObject = this,
        };
        _watcher.Created += (_, _) => ScheduleRefresh();
        _watcher.Deleted += (_, _) => ScheduleRefresh();
        _watcher.Renamed += (_, _) => ScheduleRefresh();
        _watcher.Changed += (_, _) => ScheduleRefresh();
        _watcher.EnableRaisingEvents = true;
    }

    private void SetFilter(GalleryFilter filter)
    {
        _filter = filter;
        for (int i = 0; i < _chips.Length; i++)
            _chips[i].Checked = i == (int)filter;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string search = _search.Text.Trim();
        List<GalleryItem> visible = _all.Where(i => GalleryScanner.Matches(i, search, _filter, _links.Store)).Take(MaxItems).ToList();
        DateTime today = DateTime.Today;

        _gallery.EmptyText = _all.Count == 0
            ? $"Nenhuma captura ainda.\n\nUse {_getSettings().Hotkeys.CaptureRegion} ou o botão \"Capturar região\"."
            : "Nada encontrado com esse filtro.";
        _gallery.SetItems(visible, item => GalleryScanner.GroupName(item.Modified, today));
        UpdateCount(visible.Count);
    }

    private void UpdateCount(int visibleCount)
    {
        int linked = _all.Count(i => LinkOf(i) is not null);
        string total = _all.Count == 1 ? "1 captura" : $"{_all.Count} capturas";
        string shown = visibleCount < _all.Count ? $"{visibleCount} de {total}" : total;
        _countLabel.Text = $"{shown}  ·  {linked} com link";
    }

    private void OnThumbnailRequested(GalleryItem item)
    {
        if (!_thumbPending.Add(item.FullPath))
            return;
        _thumbQueue.Push(item);
        if (!_pumping)
            _ = PumpThumbnailsAsync();
    }

    /// <summary>Gera as miniaturas uma a uma; as mais recentes pedidas (as visíveis agora) primeiro.</summary>
    private async Task PumpThumbnailsAsync()
    {
        _pumping = true;
        Size size = _thumbs.Size;
        while (_thumbQueue.Count > 0 && !IsDisposed)
        {
            GalleryItem item = _thumbQueue.Pop();
            Bitmap thumb = await Task.Run(() => ThumbnailCache.Render(item, size));
            if (IsDisposed)
            {
                thumb.Dispose();
                break;
            }
            _thumbs.Put(item, thumb);
            _thumbPending.Remove(item.FullPath);
            _gallery.InvalidateItem(item.FullPath);
        }
        _pumping = false;
    }

    private void OnLinkChanged(string path)
    {
        _gallery.InvalidateItem(path);
        if (_filter == GalleryFilter.WithLink)
            ApplyFilter();
        else
        {
            UpdateCount(_all.Count(i => GalleryScanner.Matches(i, _search.Text.Trim(), _filter, _links.Store)));
            ScheduleDetailsUpdate();
        }
    }

    // ---------- Painel de detalhes ----------

    private List<GalleryItem> Selected => _gallery.SelectedItems.ToList();

    private LinkInfo? LinkOf(GalleryItem item) => _links.Store.Get(item.FullPath);

    private void ScheduleDetailsUpdate()
    {
        // Selecionar vários itens dispara vários eventos: atualiza uma vez só.
        if (_detailsPending || !IsHandleCreated)
        {
            if (!IsHandleCreated)
                UpdateDetails();
            return;
        }
        _detailsPending = true;
        BeginInvoke(() =>
        {
            _detailsPending = false;
            UpdateDetails();
        });
    }

    private async void UpdateDetails()
    {
        List<GalleryItem> selected = Selected;
        int version = ++_previewVersion;
        _preview.Image = null;

        int linked = selected.Count(i => LinkOf(i) is not null);
        bool any = selected.Count > 0 && !_working;
        _btnCreateLink.Enabled = any && linked < selected.Count;
        _btnCopyLink.Enabled = any && linked > 0;
        _btnOpenLink.Enabled = any && selected.Count == 1 && linked == 1;
        _btnDeleteLink.Enabled = any && linked > 0;
        _btnCopy.Enabled = _btnReveal.Enabled = _btnDelete.Enabled = any;
        _btnEdit.Enabled = any && selected.Count == 1 && selected[0].Kind == GalleryItemKind.Image;
        _btnCreateLink.Text = selected.Count > 1 && linked < selected.Count ? $"Gerar {selected.Count - linked} links" : "Gerar link";
        _destinationLabel.Text = $"Destino: {_links.ActiveDestinationName}";

        if (selected.Count == 0)
        {
            _title.Text = "Nenhuma captura selecionada";
            _meta.Text = "Clique numa miniatura para ver detalhes e gerar o link.";
            _linkBox.Text = "";
            return;
        }

        if (selected.Count > 1)
        {
            _title.Text = $"{selected.Count} capturas selecionadas";
            _meta.Text = $"{GalleryScanner.FormatSize(selected.Sum(i => i.Size))} no total";
            _linkBox.Text = linked == 0 ? "Nenhuma com link" : $"{linked} com link";
            return;
        }

        GalleryItem item = selected[0];
        LinkInfo? link = LinkOf(item);
        _title.Text = item.Name;
        _meta.Text = $"{item.Modified:dd/MM/yyyy HH:mm}  ·  {GalleryScanner.FormatSize(item.Size)}";
        _linkBox.Text = link?.Url ?? "Sem link: a captura está só no seu computador";
        if (link is not null)
            _destinationLabel.Text = $"Enviado para {UploaderFactory.DisplayName(link.EffectiveDestination)} em {link.UploadedAt:dd/MM/yyyy HH:mm}";

        if (item.Kind == GalleryItemKind.Video)
        {
            if (_thumbs.Get(item) is Bitmap thumb)
                _preview.Image = new Bitmap(thumb);
            return;
        }

        try
        {
            Bitmap image = await Task.Run(() => ThumbnailCache.LoadImage(item.FullPath));
            if (version != _previewVersion || IsDisposed)
            {
                image.Dispose();
                return;
            }
            _preview.Image = image;
            _meta.Text = $"{image.Width} × {image.Height}  ·  {item.Modified:dd/MM/yyyy HH:mm}  ·  {GalleryScanner.FormatSize(item.Size)}";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException)
        {
            if (version == _previewVersion)
                _meta.Text = "Não foi possível abrir a imagem.";
        }
    }

    // ---------- Ações de link ----------

    private async Task CreateLinksAsync()
    {
        List<GalleryItem> pending = Selected.Where(i => LinkOf(i) is null).ToList();
        if (pending.Count == 0 || _working)
            return;

        IUploader? uploader = _links.Prepare(this, pending.Count);
        if (uploader is null)
            return;

        await RunWorkAsync(async () =>
        {
            for (int i = 0; i < pending.Count; i++)
            {
                _countLabel.Text = $"Enviando {i + 1} de {pending.Count} para {uploader.Name}…";
                await _links.CreateAsync(uploader, pending[i].FullPath);
            }
            CopyLinks();
            _countLabel.Text = pending.Count == 1 ? "Link gerado e copiado." : $"{pending.Count} links gerados e copiados.";
        });
    }

    private void CopyLinks()
    {
        List<string> urls = Selected.Select(LinkOf).Where(l => l is not null).Select(l => l!.Url).ToList();
        if (urls.Count == 0)
            return;
        ClipboardHelper.SetText(string.Join(Environment.NewLine, urls));
        _countLabel.Text = urls.Count == 1 ? "Link copiado." : $"{urls.Count} links copiados.";
    }

    private void OpenLink()
    {
        List<GalleryItem> selected = Selected;
        if (selected.Count != 1 || LinkOf(selected[0]) is not LinkInfo link)
            return;
        // Só abre endereços https gravados pelo próprio Sharelink.
        if (Uri.TryCreate(link.Url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private async Task DeleteLinksAsync()
    {
        List<GalleryItem> linked = Selected.Where(i => LinkOf(i) is not null).ToList();
        if (linked.Count == 0 || _working)
            return;

        string question = linked.Count == 1 ? "Apagar o link desta captura?" : $"Apagar os links de {linked.Count} capturas?";
        if (MessageBox.Show(this, question + "\n\nO arquivo é removido da nuvem e quem tiver o link não conseguirá mais abrir. " +
                                  "A captura continua no seu computador.", "Sharelink",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        await RunWorkAsync(async () =>
        {
            foreach (GalleryItem item in linked)
            {
                _countLabel.Text = $"Apagando link de {item.Name}…";
                await _links.DeleteAsync(item.FullPath);
            }
            _countLabel.Text = linked.Count == 1 ? "Link apagado." : $"{linked.Count} links apagados.";
        });
    }

    // ---------- Ações de arquivo ----------

    private void OpenSelectedFile()
    {
        List<GalleryItem> selected = Selected;
        if (selected.Count == 1 && File.Exists(selected[0].FullPath))
            Process.Start(new ProcessStartInfo(selected[0].FullPath) { UseShellExecute = true });
    }

    private void CopySelected()
    {
        List<GalleryItem> selected = Selected;
        if (selected.Count == 0)
            return;
        try
        {
            if (selected.Count == 1 && selected[0].Kind == GalleryItemKind.Image)
            {
                using Bitmap image = ThumbnailCache.LoadImage(selected[0].FullPath);
                ClipboardHelper.SetImage(image);
                _countLabel.Text = "Imagem copiada.";
            }
            else
            {
                ClipboardHelper.SetFiles(selected.Select(i => i.FullPath));
                _countLabel.Text = selected.Count == 1 ? "Arquivo copiado." : $"{selected.Count} arquivos copiados.";
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            ShowError("Não foi possível copiar", ex);
        }
    }

    private void EditSelected()
    {
        List<GalleryItem> selected = Selected;
        if (selected.Count == 1 && selected[0].Kind == GalleryItemKind.Image)
            _commands.EditFile(selected[0].FullPath);
    }

    private void RevealSelected()
    {
        List<GalleryItem> selected = Selected;
        if (selected.Count >= 1 && File.Exists(selected[0].FullPath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{selected[0].FullPath}\"") { UseShellExecute = false });
    }

    private async Task DeleteFilesAsync()
    {
        List<GalleryItem> selected = Selected;
        if (selected.Count == 0 || _working)
            return;

        int linked = selected.Count(i => LinkOf(i) is not null);
        string what = selected.Count == 1 ? $"\"{selected[0].Name}\"" : $"{selected.Count} capturas";
        string text = $"Mover {what} para a Lixeira?" +
                      (linked > 0 ? $"\n\n{(linked == 1 ? "O link público também será apagado" : $"Os {linked} links públicos também serão apagados")} da nuvem." : "");
        if (MessageBox.Show(this, text, "Sharelink", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        await RunWorkAsync(async () =>
        {
            foreach (GalleryItem item in selected)
            {
                // Apaga o link primeiro: se falhar, mantém o arquivo para não perder o controle do link.
                if (LinkOf(item) is not null)
                    await _links.DeleteAsync(item.FullPath);
                if (File.Exists(item.FullPath))
                    FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            _countLabel.Text = selected.Count == 1 ? "Captura enviada para a Lixeira." : $"{selected.Count} capturas enviadas para a Lixeira.";
        });
        await ReloadAsync();
    }

    private async Task RunWorkAsync(Func<Task> work)
    {
        _working = true;
        UseWaitCursor = true;
        UpdateDetails();
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log.Error("galeria", ex);
            ShowError("Algo deu errado", ex);
        }
        finally
        {
            _working = false;
            UseWaitCursor = false;
            UpdateDetails();
        }
    }

    private void ShowError(string title, Exception ex)
    {
        _countLabel.Text = title;
        MessageBox.Show(this, ex.Message, $"Sharelink: {title}", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    // ---------- Teclado e janela ----------

    private void Gallery_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyData)
        {
            case Keys.Delete:
                _ = DeleteFilesAsync();
                break;
            case Keys.Control | Keys.C:
                CopySelected();
                break;
            case Keys.Control | Keys.A:
                _gallery.SelectAll();
                break;
            default:
                return;
        }
        e.Handled = e.SuppressKeyPress = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5:
                _ = ReloadAsync();
                return true;
            case Keys.Control | Keys.F:
                _search.FocusText();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _links.Store.Changed -= OnLinkChanged;
            _watcher?.Dispose();
            _refreshTimer.Dispose();
            _thumbs.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
