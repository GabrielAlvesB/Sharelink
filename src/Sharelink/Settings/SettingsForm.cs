using System.Diagnostics;
using Sharelink.Common;
using Sharelink.Controls;
using Sharelink.Hotkeys;
using Sharelink.Output;
using Sharelink.Recording;
using Sharelink.Theme;
using Sharelink.Update;
using Sharelink.Upload;
using Sharelink.Upload.Providers;

namespace Sharelink.Settings;

/// <summary>Configurações com navegação lateral. Nada é aplicado até clicar em Salvar.</summary>
internal sealed class SettingsForm : ThemedForm
{
    private static readonly (ClipboardAction Value, string Label)[] ClipboardOptions =
    [
        (ClipboardAction.Image, "A imagem"),
        (ClipboardAction.Link, "O caminho do arquivo"),
        (ClipboardAction.File, "O arquivo"),
        (ClipboardAction.None, "Nada"),
    ];

    private static readonly (ThemeMode Value, string Label)[] ThemeOptions =
    [
        (ThemeMode.System, "Seguir o Windows"),
        (ThemeMode.Dark, "Escuro"),
        (ThemeMode.Light, "Claro"),
    ];

    private static readonly UploadDestination[] Destinations =
        [UploadDestination.CloudflareR2, UploadDestination.Supabase, UploadDestination.S3Compatible, UploadDestination.Imgur, UploadDestination.WebDav];

    /// <summary>Atalhos para preencher o S3 de serviços conhecidos. {r} = região, {b} = bucket.</summary>
    private static readonly (string Name, string Endpoint, string Region, string PublicUrl, bool PathStyle, bool Acl, string Hint)[] S3Presets =
    [
        ("Amazon S3", "https://s3.{r}.amazonaws.com", "us-east-1", "https://{b}.s3.{r}.amazonaws.com", false, false,
            "Libere leitura pública do bucket com uma Bucket Policy (ou use CloudFront) e crie uma chave IAM só com s3:PutObject/DeleteObject neste bucket."),
        ("Backblaze B2", "https://s3.{r}.backblazeb2.com", "us-west-004", "https://f004.backblazeb2.com/file/{b}", false, false,
            "Crie o bucket como \"Public\" e uma Application Key restrita a ele. A região aparece no endpoint do bucket (ex.: us-west-004)."),
        ("Wasabi", "https://s3.{r}.wasabisys.com", "us-east-1", "https://s3.{r}.wasabisys.com/{b}", true, false,
            "Contas Wasabi precisam de uma policy de leitura pública no bucket para os links abrirem."),
        ("DigitalOcean Spaces", "https://{r}.digitaloceanspaces.com", "nyc3", "https://{b}.{r}.digitaloceanspaces.com", false, true,
            "Crie uma Spaces Key. Os arquivos ficam públicos via ACL (opção marcada)."),
        ("MinIO / outro", "", "us-east-1", "", true, false,
            "Informe o endpoint https do seu servidor. A maioria dos servidores próprios precisa de path-style."),
    ];

    private readonly AppSettings _settings;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new Padding(28, 20, 28, 12) };
    private readonly Label _pageTitle = new() { AutoSize = true, Dock = DockStyle.Top, Font = new Font("Segoe UI Semibold", 15f), Padding = new Padding(0, 0, 0, 12) };
    private readonly List<(ModernButton Nav, Control Page, string Title)> _pages = [];

    // Aparência e sistema
    private readonly ComboBox _theme = Combo(220);
    private readonly CheckBox _showGallery = Check("Abrir a galeria ao iniciar o Sharelink");
    private readonly CheckBox _autoUpdate = Check("Verificar e baixar atualizações automaticamente (a instalação sempre pede seu clique)");
    private readonly Label _updateStatus = new() { AutoSize = true, MaximumSize = new Size(520, 0), UseMnemonic = false };
    private readonly Func<Task<string>>? _checkUpdates;
    private readonly CheckBox _startup = Check("Iniciar o Sharelink junto com o Windows (abre só na bandeja)");

    // Captura
    private readonly CheckBox _openEditor = Check("Abrir o editor de anotações após capturar");
    private readonly CheckBox _saveToFile = Check("Salvar a imagem na pasta de capturas");
    private readonly ComboBox _clipboard = Combo(260);
    private readonly CheckBox _notify = Check("Mostrar aviso ao terminar");
    private readonly ComboBox _format = Combo(100);
    private readonly NumericUpDown _jpegQuality = new() { Minimum = 10, Maximum = 100, Width = 70 };

    // Arquivos
    private readonly TextBox _folder = Field(360);
    private readonly TextBox _pattern = Field(360);
    private readonly Label _patternPreview = Muted("");
    private readonly CheckBox _monthFolder = Check("Criar uma subpasta por mês (ex.: 2026-10)");

    // Atalhos
    private readonly HotkeyBox _hkRegion = new() { Width = 220 };
    private readonly HotkeyBox _hkLink = new() { Width = 220 };
    private readonly HotkeyBox _hkFull = new() { Width = 220 };
    private readonly HotkeyBox _hkWindow = new() { Width = 220 };
    private readonly HotkeyBox _hkMp4 = new() { Width = 220 };
    private readonly HotkeyBox _hkGif = new() { Width = 220 };

    // Gravação
    private readonly TextBox _ffmpeg = Field(360, "Vazio = procurar automaticamente");
    private readonly Label _ffmpegStatus = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly NumericUpDown _videoFps = new() { Minimum = 5, Maximum = 60, Width = 70 };
    private readonly NumericUpDown _gifFps = new() { Minimum = 5, Maximum = 30, Width = 70 };
    private readonly CheckBox _recordCursor = Check("Mostrar o cursor do mouse na gravação");

    // Links: geral
    private readonly ComboBox _destination = Combo(260);
    private readonly CheckBox _confirmUpload = Check("Pedir confirmação antes de enviar");
    private readonly Label _testStatus = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly Dictionary<UploadDestination, Control> _destinationPanels = [];
    private readonly Dictionary<string, (string UserName, string Secret)?> _stored = [];

    // Links: Cloudflare R2
    private readonly TextBox _r2Account = Field(360, "32 caracteres, ex.: 0123abcd…");
    private readonly TextBox _r2Bucket = Field(360, "ex.: minhas-capturas");
    private readonly TextBox _r2PublicUrl = Field(360, "https://pub-xxxx.r2.dev ou https://img.seudominio.com");
    private readonly TextBox _r2KeyId = Field(360);
    private readonly TextBox _r2Secret = Secret();

    // Links: Supabase
    private readonly TextBox _sbProject = Field(360, "ex.: abcdefghijklmnopqrst");
    private readonly TextBox _sbRegion = Field(160, "ex.: sa-east-1");
    private readonly TextBox _sbBucket = Field(360, "ex.: capturas");
    private readonly TextBox _sbKeyId = Field(360);
    private readonly TextBox _sbSecret = Secret();
    private readonly Label _sbPublicPreview = Muted("");

    // Links: S3 compatível
    private readonly ComboBox _s3Preset = Combo(220);
    private readonly Label _s3Hint = Muted("");
    private readonly TextBox _s3Endpoint = Field(360);
    private readonly TextBox _s3Region = Field(160);
    private readonly TextBox _s3Bucket = Field(360);
    private readonly TextBox _s3PublicUrl = Field(360);
    private readonly CheckBox _s3PathStyle = Check("Usar path-style (endpoint/bucket/arquivo)");
    private readonly CheckBox _s3Acl = Check("Tornar cada arquivo público via ACL (public-read)");
    private readonly TextBox _s3KeyId = Field(360);
    private readonly TextBox _s3Secret = Secret();

    // Links: Imgur
    private readonly TextBox _imgurClientId = Secret();

    // Links: Nextcloud / WebDAV
    private readonly ComboBox _davMode = Combo(260);
    private readonly TextBox _davServer = Field(360, "https://nuvem.seudominio.com");
    private readonly TextBox _davUser = Field(220);
    private readonly TextBox _davPassword = Secret();
    private readonly TextBox _davFolder = Field(220, "Sharelink");
    private readonly TextBox _davPublicUrl = Field(360, "https://arquivos.seudominio.com/sharelink");
    private readonly Label _davPublicLabel = new() { Text = "URL pública:", AutoSize = true };

    public AppSettings Result => _settings;

    /// <param name="settings">Uma cópia: só é aplicada se o usuário clicar em Salvar.</param>
    public SettingsForm(AppSettings settings, bool openLinksTab = false, Func<Task<string>>? checkUpdates = null)
    {
        _settings = settings;
        _checkUpdates = checkUpdates;

        Text = "Sharelink: configurações";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 640);
        MinimumSize = new Size(820, 560);
        MaximizeBox = MinimizeBox = false;

        // ---------- Navegação lateral ----------
        var nav = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 210, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 18, 12, 12) };
        ThemeManager.SetRole(nav, ThemeRole.Surface);
        var navTitle = new Label { Text = "Configurações", AutoSize = true, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(8, 0, 0, 14) };
        nav.Controls.Add(navTitle);

        AddPage(nav, "Aparência", Glyphs.Palette, BuildAppearancePage());
        AddPage(nav, "Captura", Glyphs.Camera, BuildCapturePage());
        AddPage(nav, "Arquivos", Glyphs.Folder, BuildFilesPage());
        AddPage(nav, "Atalhos", Glyphs.Keyboard, BuildHotkeysPage());
        AddPage(nav, "Gravação", Glyphs.Record, BuildRecordingPage());
        AddPage(nav, "Links e upload", Glyphs.Link, BuildLinksPage());
        AddPage(nav, "Atualizações", Glyphs.Refresh, BuildUpdatesPage());
        AddPage(nav, "Privacidade", Glyphs.Shield, BuildPrivacyPage());

        // ---------- Rodapé ----------
        var save = new ModernButton { Text = "Salvar", Glyph = Glyphs.Check, Variant = ButtonVariant.Primary, Dock = DockStyle.Right, Width = 120 };
        save.Click += (_, _) => SaveAndClose();
        var cancel = new ModernButton { Text = "Cancelar", Variant = ButtonVariant.Secondary, Dock = DockStyle.Right, Width = 110, DialogResult = DialogResult.Cancel };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(16, 12, 20, 12) };
        footer.Controls.AddRange([cancel, new Panel { Dock = DockStyle.Right, Width = 10 }, save]);

        var divider = new Panel { Dock = DockStyle.Left, Width = 1 };
        divider.Paint += (_, e) => e.Graphics.Clear(ThemeManager.Current.Border);

        _content.Controls.Add(_pageTitle);
        Controls.Add(_content);
        Controls.Add(divider);
        Controls.Add(nav);
        Controls.Add(footer);
        AcceptButton = save;
        CancelButton = cancel;

        LoadValues();
        ShowPage(openLinksTab ? 5 : 0);
    }

    // ---------- Construção ----------

    private static ComboBox Combo(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true };
    private static TextBox Field(int width, string placeholder = "") => new() { Width = width, PlaceholderText = placeholder };
    private static TextBox Secret() => new() { Width = 360, UseSystemPasswordChar = true };

    private static Label Muted(string text)
    {
        var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), UseMnemonic = false };
        ThemeManager.SetRole(label, ThemeRole.Muted);
        return label;
    }

    private void AddPage(FlowLayoutPanel nav, string title, string glyph, Control page)
    {
        int index = _pages.Count;
        var button = new ModernButton
        {
            Text = title, Glyph = glyph, Variant = ButtonVariant.Ghost, AlignLeft = true,
            Width = 184, Height = 38, Margin = new Padding(0, 0, 0, 4), Padding = new Padding(12, 0, 8, 0),
        };
        button.Click += (_, _) => ShowPage(index);
        nav.Controls.Add(button);
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _content.Controls.Add(page);
        page.BringToFront();
        _pages.Add((button, page, title));
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            bool active = i == index;
            _pages[i].Page.Visible = active;
            _pages[i].Nav.Variant = active ? ButtonVariant.Chip : ButtonVariant.Ghost;
            _pages[i].Nav.Checked = active;
        }
        _pageTitle.Text = _pages[index].Title;
    }

    private static TableLayoutPanel NewTable()
    {
        var table = new TableLayoutPanel { ColumnCount = 2, AutoScroll = true, Padding = new Padding(0, 4, 12, 4) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static void AddRow(TableLayoutPanel table, string? label, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(3, 6, 3, 6);
        if (label is null)
        {
            table.Controls.Add(control, 0, row);
            table.SetColumnSpan(control, 2);
        }
        else
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 10, 16, 6) }, 0, row);
            table.Controls.Add(control, 1, row);
        }
    }

    private static void AddLabeledRow(TableLayoutPanel table, Label label, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(3, 6, 3, 6);
        label.Anchor = AnchorStyles.Left;
        label.Margin = new Padding(3, 10, 16, 6);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static FlowLayoutPanel Inline(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        flow.Controls.AddRange(controls);
        return flow;
    }

    private static ModernButton SmallButton(string text, string glyph = "", ButtonVariant variant = ButtonVariant.Secondary)
    {
        var button = new ModernButton { Text = text, Glyph = glyph, Variant = variant, Height = 32, Margin = new Padding(0, 0, 8, 0), AutoWidth = true };
        return button;
    }

    private Control BuildAppearancePage()
    {
        foreach ((ThemeMode _, string label) in ThemeOptions)
            _theme.Items.Add(label);
        TableLayoutPanel t = NewTable();
        AddRow(t, "Tema:", _theme);
        AddRow(t, null, Muted("\"Seguir o Windows\" usa o modo claro/escuro definido em Configurações do Windows › Personalização › Cores, e muda sozinho quando você troca lá."));
        AddRow(t, null, _showGallery);
        AddRow(t, null, _startup);
        return t;
    }

    private Control BuildCapturePage()
    {
        foreach ((ClipboardAction _, string label) in ClipboardOptions)
            _clipboard.Items.Add(label);
        _format.Items.AddRange(["PNG", "JPEG"]);
        _format.SelectedIndexChanged += (_, _) => _jpegQuality.Enabled = _format.SelectedIndex == 1;

        TableLayoutPanel t = NewTable();
        AddRow(t, null, _openEditor);
        AddRow(t, null, _saveToFile);
        AddRow(t, "Copiar para a área de transferência:", _clipboard);
        AddRow(t, "Formato da imagem:", Inline(_format, new Label { Text = "Qualidade JPEG:", AutoSize = true, Margin = new Padding(16, 7, 6, 0) }, _jpegQuality));
        AddRow(t, null, _notify);
        AddRow(t, null, Muted("Para gerar link público, use \"Capturar e gerar link\", o botão \"Gerar link\" da galeria ou Ctrl+Enter no editor."));
        return t;
    }

    private Control BuildFilesPage()
    {
        ModernButton browse = SmallButton("Procurar…", Glyphs.Folder);
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _folder.Text, ShowNewFolderButton = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _folder.Text = dialog.SelectedPath;
        };
        _pattern.TextChanged += (_, _) => UpdatePatternPreview();

        TableLayoutPanel t = NewTable();
        AddRow(t, "Pasta de capturas:", Inline(_folder, new Panel { Width = 8, Height = 1 }, browse));
        AddRow(t, "Nome do arquivo:", _pattern);
        AddRow(t, "", _patternPreview);
        AddRow(t, null, Muted("O texto entre chaves vira data/hora. Ex.: {yyyy-MM-dd} = ano-mês-dia, {HH-mm-ss} = hora-minuto-segundo."));
        AddRow(t, null, _monthFolder);
        return t;
    }

    private Control BuildHotkeysPage()
    {
        TableLayoutPanel t = NewTable();
        AddRow(t, null, Muted("Clique no campo e pressione a combinação desejada. Backspace limpa o atalho.\n" +
                              "Para usar a tecla PrintScreen, desative antes em Configurações do Windows › Acessibilidade › Teclado › " +
                              "\"Usar a tecla Print Screen para abrir a captura de tela\"."));
        AddRow(t, "Capturar região:", _hkRegion);
        AddRow(t, "Capturar e gerar link:", _hkLink);
        AddRow(t, "Capturar tela inteira:", _hkFull);
        AddRow(t, "Capturar janela ativa:", _hkWindow);
        AddRow(t, "Gravar vídeo (MP4):", _hkMp4);
        AddRow(t, "Gravar GIF:", _hkGif);
        return t;
    }

    private Control BuildRecordingPage()
    {
        ModernButton browse = SmallButton("Procurar…", Glyphs.Folder);
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe", Title = "Localizar ffmpeg.exe" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _ffmpeg.Text = dialog.FileName;
        };
        _ffmpeg.TextChanged += (_, _) => UpdateFfmpegStatus();

        TableLayoutPanel t = NewTable();
        AddRow(t, "FFmpeg:", Inline(_ffmpeg, new Panel { Width = 8, Height = 1 }, browse));
        AddRow(t, "", _ffmpegStatus);
        AddRow(t, "Quadros por segundo (MP4):", _videoFps);
        AddRow(t, "Quadros por segundo (GIF):", _gifFps);
        AddRow(t, null, _recordCursor);
        return t;
    }

    private Control BuildLinksPage()
    {
        foreach (UploadDestination d in Destinations)
            _destination.Items.Add(UploaderFactory.DisplayName(d));
        _destination.SelectedIndexChanged += (_, _) => ShowDestinationPanel();

        ModernButton test = SmallButton("Testar conexão", Glyphs.Refresh, ButtonVariant.Primary);
        test.Click += async (_, _) => await TestDestinationAsync(test);
        ModernButton forget = SmallButton("Apagar chaves salvas", Glyphs.Delete, ButtonVariant.Danger);
        forget.Click += (_, _) => ForgetCredentials();

        TableLayoutPanel t = NewTable();
        AddRow(t, "Destino dos links:", _destination);
        AddRow(t, null, Muted("Os links só são gerados quando você pede. Cada destino guarda as próprias chaves no Gerenciador de Credenciais do Windows, " +
                              "e links já criados continuam podendo ser apagados mesmo se você trocar o destino."));

        _destinationPanels[UploadDestination.CloudflareR2] = BuildR2Panel();
        _destinationPanels[UploadDestination.Supabase] = BuildSupabasePanel();
        _destinationPanels[UploadDestination.S3Compatible] = BuildS3Panel();
        _destinationPanels[UploadDestination.Imgur] = BuildImgurPanel();
        _destinationPanels[UploadDestination.WebDav] = BuildWebDavPanel();
        foreach (Control panel in _destinationPanels.Values)
            AddRow(t, null, panel);

        AddRow(t, null, _confirmUpload);
        AddRow(t, null, Inline(test, forget));
        AddRow(t, null, _testStatus);
        return t;
    }

    private static TableLayoutPanel SubTable()
    {
        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 4) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static LinkLabel WebLink(string text, string url)
    {
        var link = new LinkLabel { Text = text, AutoSize = true };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return link;
    }

    private Control BuildR2Panel()
    {
        TableLayoutPanel t = SubTable();
        AddRow(t, null, Muted(
            "Grátis até 10 GB e sem custo de tráfego. Como configurar:\n" +
            "1. No painel da Cloudflare: R2 › Create bucket.\n" +
            "2. No bucket: Settings › Public access › ative o \"R2.dev subdomain\" (ou conecte um domínio) e copie a URL pública.\n" +
            "3. R2 › Manage API tokens › Create API token: \"Object Read & Write\" e, em \"Specify bucket\", SÓ este bucket.\n" +
            "4. Cole abaixo o Account ID, o Access Key ID e a Secret Access Key (a Secret só aparece uma vez)."));
        AddRow(t, null, WebLink("Abrir o painel R2 da Cloudflare", "https://dash.cloudflare.com/?to=/:account/r2"));
        AddRow(t, "Account ID:", _r2Account);
        AddRow(t, "Bucket:", _r2Bucket);
        AddRow(t, "URL pública:", _r2PublicUrl);
        AddRow(t, "Access Key ID:", _r2KeyId);
        AddRow(t, "Secret Access Key:", _r2Secret);
        return t;
    }

    private Control BuildSupabasePanel()
    {
        _sbProject.TextChanged += (_, _) => UpdateSupabasePreview();
        _sbBucket.TextChanged += (_, _) => UpdateSupabasePreview();

        TableLayoutPanel t = SubTable();
        AddRow(t, null, Muted(
            "Grátis até 1 GB. Os cinco campos abaixo vêm do painel do Supabase; siga os passos na ordem. " +
            "(Os nomes dos menus podem mudar um pouco, porque o Supabase atualiza a interface de vez em quando.)"));
        AddRow(t, null, WebLink("Abrir o painel do Supabase", "https://supabase.com/dashboard/projects"));

        AddRow(t, null, GuideHeading("1. Criar o projeto (se ainda não tiver)"));
        AddRow(t, null, Muted(
            "• Entre em supabase.com e faça login (dá para usar a conta do GitHub).\n" +
            "• Clique em \"New project\", dê um nome (ex.: sharelink), crie uma senha de banco (não é usada no Sharelink) " +
            "e escolha a região \"South America (São Paulo)\".\n" +
            "• Espere uns 2 minutos até o projeto ficar pronto."));

        AddRow(t, null, GuideHeading("2. Project ref"));
        AddRow(t, null, Muted(
            "É o código do projeto, cerca de 20 letras minúsculas. Aparece:\n" +
            "• na barra de endereço do navegador, com o projeto aberto: supabase.com/dashboard/project/abcdefghijklmnopqrst (a parte final);\n" +
            "• ou em Project Settings › General, no campo \"Project ID\".\n" +
            "Cole só o código, sem https:// e sem .supabase.co."));
        AddRow(t, "Project ref:", _sbProject);

        AddRow(t, null, GuideHeading("3. Bucket (a \"pasta\" das imagens)"));
        AddRow(t, null, Muted(
            "• No menu da esquerda, clique em Storage › New bucket.\n" +
            "• Dê um nome simples, sem espaços (ex.: capturas).\n" +
            "• ATIVE a opção \"Public bucket\": sem ela, quem receber o link não consegue abrir a imagem.\n" +
            "• Clique em Create e escreva aqui o nome exatamente igual."));
        AddRow(t, "Bucket:", _sbBucket);

        AddRow(t, null, GuideHeading("4. Região"));
        AddRow(t, null, Muted(
            "• Em Storage, abra Settings (em algumas versões: Project Settings › Storage).\n" +
            "• Na seção \"S3 Connection\", copie o valor de \"Region\" (ex.: sa-east-1 para São Paulo, us-east-1)."));
        AddRow(t, "Região:", _sbRegion);

        AddRow(t, null, GuideHeading("5. Chaves de acesso"));
        AddRow(t, null, Muted(
            "• Na mesma tela, em \"S3 Access Keys\", clique em \"New access key\" e dê uma descrição (ex.: Sharelink no meu PC).\n" +
            "• Copie o \"Access key ID\" e a \"Secret access key\" para os campos abaixo.\n" +
            "• Copie a Secret NA HORA: ela só aparece uma vez. Se perder, apague a chave e crie outra."));
        var serviceRoleWarning = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            UseMnemonic = false,
            Text = "⚠ Não use as chaves de Project Settings › API (\"anon\" ou \"service_role\"). A service_role dá acesso ao seu " +
                   "banco de dados inteiro. As chaves certas são as de \"S3 Access Keys\" do Storage.",
        };
        ThemeManager.SetRole(serviceRoleWarning, ThemeRole.Warning);
        AddRow(t, null, serviceRoleWarning);
        AddRow(t, "Access Key ID:", _sbKeyId);
        AddRow(t, "Secret Access Key:", _sbSecret);
        AddRow(t, "", _sbPublicPreview);

        AddRow(t, null, GuideHeading("6. Testar e salvar"));
        AddRow(t, null, Muted(
            "Clique em \"Testar conexão\" (mais abaixo): ele envia um arquivo minúsculo, abre o link e apaga.\n" +
            "• \"Tudo certo\": clique em Salvar e pronto.\n" +
            "• \"o link público respondeu 400/404\": o bucket não está marcado como Public (passo 3).\n" +
            "• \"chaves de acesso inválidas\": confira se copiou a Secret inteira, sem espaço no começo ou no fim.\n" +
            "• \"região incorreta\": confira a Region em S3 Connection (passo 4).\n" +
            "Dica de segurança: essa chave acessa todos os buckets do projeto, então use um projeto Supabase só para o Sharelink."));
        return t;
    }

    private static Label GuideHeading(string text) =>
        new() { Text = text, AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(3, 14, 3, 2) };

    private void UpdateSupabasePreview()
    {
        string project = _sbProject.Text.Trim().ToLowerInvariant();
        string bucket = _sbBucket.Text.Trim();
        _sbPublicPreview.Text = project.Length > 0 && bucket.Length > 0
            ? $"Os links ficarão assim: https://{project}.supabase.co/storage/v1/object/public/{bucket}/…"
            : "";
    }

    private Control BuildS3Panel()
    {
        foreach (var preset in S3Presets)
            _s3Preset.Items.Add(preset.Name);
        _s3Preset.SelectedIndexChanged += (_, _) => ApplyS3Preset(fill: !_loading);

        TableLayoutPanel t = SubTable();
        AddRow(t, null, Muted("Qualquer serviço compatível com a API S3: Amazon S3, Backblaze B2, Wasabi, DigitalOcean Spaces, MinIO no seu servidor... " +
                              "Use sempre uma chave com permissão só de escrita/remoção neste bucket."));
        AddRow(t, "Serviço:", _s3Preset);
        AddRow(t, null, _s3Hint);
        AddRow(t, "Endpoint:", _s3Endpoint);
        AddRow(t, "Região:", _s3Region);
        AddRow(t, "Bucket:", _s3Bucket);
        AddRow(t, "URL pública:", _s3PublicUrl);
        AddRow(t, null, _s3PathStyle);
        AddRow(t, null, _s3Acl);
        AddRow(t, "Access Key ID:", _s3KeyId);
        AddRow(t, "Secret Access Key:", _s3Secret);
        return t;
    }

    private void ApplyS3Preset(bool fill)
    {
        if (_s3Preset.SelectedIndex < 0)
            return;
        var preset = S3Presets[_s3Preset.SelectedIndex];
        _s3Hint.Text = preset.Hint;
        _s3Endpoint.PlaceholderText = preset.Endpoint.Length > 0 ? preset.Endpoint.Replace("{r}", preset.Region) : "https://minio.seudominio.com";
        _s3Region.PlaceholderText = preset.Region;
        _s3PublicUrl.PlaceholderText = preset.PublicUrl.Length > 0
            ? preset.PublicUrl.Replace("{r}", preset.Region).Replace("{b}", "meu-bucket")
            : "https://minio.seudominio.com/meu-bucket";
        if (!fill)
            return;

        _s3PathStyle.Checked = preset.PathStyle;
        _s3Acl.Checked = preset.Acl;
        string region = _s3Region.Text.Trim().Length > 0 ? _s3Region.Text.Trim() : preset.Region;
        if (_s3Region.Text.Trim().Length == 0)
            _s3Region.Text = region;
        if (preset.Endpoint.Length > 0)
            _s3Endpoint.Text = preset.Endpoint.Replace("{r}", region);
        if (preset.PublicUrl.Length > 0 && _s3Bucket.Text.Trim().Length > 0)
            _s3PublicUrl.Text = preset.PublicUrl.Replace("{r}", region).Replace("{b}", _s3Bucket.Text.Trim());
    }

    private Control BuildImgurPanel()
    {
        TableLayoutPanel t = SubTable();
        var warning = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            UseMnemonic = false,
            Text = "Atenção: o Imgur é um serviço de terceiros. As imagens ficam nos servidores dele, podem ser comprimidas, e o Imgur pode " +
                   "removê-las. Para prints com dados pessoais, prefira um destino seu (R2, S3 ou Nextcloud).",
        };
        ThemeManager.SetRole(warning, ThemeRole.Warning);
        AddRow(t, null, warning);
        AddRow(t, null, Muted("Como configurar:\n" +
                              "1. Entre na sua conta do Imgur e abra a página de registro de aplicativos.\n" +
                              "2. Em \"Authorization type\", escolha \"OAuth 2 authorization without a callback URL\".\n" +
                              "3. Copie o Client ID e cole abaixo (o Client Secret não é necessário).\n" +
                              "Se o link abaixo levar para a tela \"Apps Used\" em vez do formulário: confirme que está logado e abra o link de novo. " +
                              "Se continuar caindo lá, o Imgur não está aceitando novos aplicativos para a sua conta; use outro destino (Supabase é grátis)."));
        AddRow(t, null, WebLink("Registrar aplicativo no Imgur", "https://api.imgur.com/oauth2/addclient"));
        AddRow(t, "Client ID:", _imgurClientId);
        return t;
    }

    private Control BuildWebDavPanel()
    {
        _davMode.Items.AddRange(["Nextcloud / ownCloud", "WebDAV genérico"]);
        _davMode.SelectedIndexChanged += (_, _) => _davPublicUrl.Visible = _davPublicLabel.Visible = _davMode.SelectedIndex == 1;

        TableLayoutPanel t = SubTable();
        AddRow(t, null, Muted("Nextcloud: o arquivo vai para a pasta escolhida e o Sharelink cria um link público de compartilhamento (só leitura).\n" +
                              "Use uma SENHA DE APLICATIVO, nunca a sua senha principal: no Nextcloud, Configurações pessoais › Segurança › " +
                              "\"Criar nova senha de aplicativo\".\n" +
                              "WebDAV genérico: informe a URL da pasta WebDAV e a URL pública onde essa pasta pode ser acessada."));
        AddRow(t, "Tipo:", _davMode);
        AddRow(t, "Servidor:", _davServer);
        AddRow(t, "Usuário:", _davUser);
        AddRow(t, "Senha de aplicativo:", _davPassword);
        AddRow(t, "Pasta:", _davFolder);
        AddLabeledRow(t, _davPublicLabel, _davPublicUrl);
        return t;
    }

    private Control BuildUpdatesPage()
    {
        ModernButton check = SmallButton("Verificar agora", Glyphs.Refresh, ButtonVariant.Primary);
        check.Enabled = _checkUpdates is not null;
        check.Click += async (_, _) =>
        {
            check.Enabled = false;
            _updateStatus.ForeColor = ThemeManager.Current.Text;
            _updateStatus.Text = "Verificando…";
            _updateStatus.Text = await _checkUpdates!();
            check.Enabled = true;
        };

        TableLayoutPanel t = NewTable();
        AddRow(t, "Versão instalada:", new Label { Text = AppVersion.CurrentText, AutoSize = true, Font = new Font("Segoe UI Semibold", 10f) });
        AddRow(t, null, _autoUpdate);
        AddRow(t, null, Inline(check));
        AddRow(t, null, _updateStatus);
        AddRow(t, null, WebLink("Ver todas as versões no GitHub", $"https://github.com/{GitHubUpdateClient.Owner}/{GitHubUpdateClient.Repository}/releases"));
        AddRow(t, null, Muted(
            "Como funciona: o Sharelink consulta as versões publicadas no GitHub ao abrir e a cada 6 horas. Quando encontra uma nova, " +
            "baixa em segundo plano e confere a ASSINATURA DIGITAL do instalador. Se a assinatura não bater, o arquivo é apagado na hora. " +
            "A chave que assina as versões fica só no computador de quem publica, então nem uma conta do GitHub invadida consegue " +
            "distribuir uma atualização falsa.\n\nQuando a versão estiver pronta, aparece um aviso na galeria e na bandeja. " +
            "Ao clicar em \"Atualizar agora\", o Sharelink fecha, instala e reabre sozinho."));
        return t;
    }

    private Control BuildPrivacyPage()
    {
        ModernButton openData = SmallButton("Abrir pasta de configurações e log", Glyphs.Folder);
        openData.Click += (_, _) =>
        {
            Directory.CreateDirectory(SettingsStore.AppDataFolder);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { SettingsStore.AppDataFolder } });
        };

        TableLayoutPanel t = NewTable();
        AddRow(t, null, new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Text = "Nada sai do seu computador, a não ser quando você pede \"Gerar link\". Nesse caso o arquivo vai apenas para o destino " +
                   "escolhido em \"Links e upload\".\n\n" +
                   "• A única consulta automática é a verificação de versão nova no GitHub (pode desligar em Atualizações).\n" +
                   "• Sem telemetria e sem nenhum pacote de terceiros. Atualizações só são aceitas com assinatura digital válida.\n" +
                   "• Senhas e chaves ficam criptografadas no Gerenciador de Credenciais do Windows.\n" +
                   "• Toda conexão é HTTPS, sem redirecionamentos, e só para o servidor configurado.\n" +
                   "• Só imagens (PNG, JPG, GIF) e vídeos MP4 podem ser enviados, com nomes aleatórios.\n" +
                   "• Para esconder dados num print, use Pixelar no editor: ele descarta a informação original.",
        });
        AddRow(t, null, openData);
        return t;
    }

    // ---------- Valores ----------

    private bool _loading;

    private void LoadValues()
    {
        _loading = true;
        _theme.SelectedIndex = Math.Max(0, Array.FindIndex(ThemeOptions, o => o.Value == _settings.Theme));
        _showGallery.Checked = _settings.ShowGalleryOnStartup;
        _startup.Checked = StartupManager.IsEnabled();
        _autoUpdate.Checked = _settings.AutoUpdate;

        _openEditor.Checked = _settings.OpenEditorAfterCapture;
        _saveToFile.Checked = _settings.SaveToFile;
        _clipboard.SelectedIndex = Math.Max(0, Array.FindIndex(ClipboardOptions, o => o.Value == _settings.ClipboardAction));
        _notify.Checked = _settings.ShowNotification;
        _format.SelectedIndex = _settings.ImageFormat == ImageFormatOption.Jpeg ? 1 : 0;
        _jpegQuality.Value = _settings.JpegQuality;
        _jpegQuality.Enabled = _format.SelectedIndex == 1;

        _folder.Text = _settings.OutputFolder;
        _pattern.Text = _settings.FileNamePattern;
        _monthFolder.Checked = _settings.UseMonthSubfolder;

        _hkRegion.Hotkey = ParseOrNone(_settings.Hotkeys.CaptureRegion);
        _hkLink.Hotkey = ParseOrNone(_settings.Hotkeys.CaptureAndLink);
        _hkFull.Hotkey = ParseOrNone(_settings.Hotkeys.CaptureFullScreen);
        _hkWindow.Hotkey = ParseOrNone(_settings.Hotkeys.CaptureActiveWindow);
        _hkMp4.Hotkey = ParseOrNone(_settings.Hotkeys.RecordMp4);
        _hkGif.Hotkey = ParseOrNone(_settings.Hotkeys.RecordGif);

        _ffmpeg.Text = _settings.FfmpegPath ?? "";
        _videoFps.Value = _settings.VideoFps;
        _gifFps.Value = _settings.GifFps;
        _recordCursor.Checked = _settings.RecordCursor;

        _destination.SelectedIndex = Math.Max(0, Array.IndexOf(Destinations, _settings.Destination));
        _confirmUpload.Checked = _settings.ConfirmBeforeUpload;

        foreach (string target in new[] { CredentialStore.R2Target, CredentialStore.S3Target, CredentialStore.ImgurTarget, CredentialStore.WebDavTarget, CredentialStore.SupabaseTarget })
            _stored[target] = CredentialStore.Load(target);

        _r2Account.Text = _settings.R2AccountId;
        _r2Bucket.Text = _settings.R2Bucket;
        _r2PublicUrl.Text = _settings.R2PublicBaseUrl;
        _r2KeyId.Text = _stored[CredentialStore.R2Target]?.UserName ?? "";

        _sbProject.Text = _settings.SupabaseProjectRef;
        _sbRegion.Text = _settings.SupabaseRegion;
        _sbBucket.Text = _settings.SupabaseBucket;
        _sbKeyId.Text = _stored[CredentialStore.SupabaseTarget]?.UserName ?? "";

        _s3Endpoint.Text = _settings.S3Endpoint;
        _s3Region.Text = _settings.S3Region;
        _s3Bucket.Text = _settings.S3Bucket;
        _s3PublicUrl.Text = _settings.S3PublicBaseUrl;
        _s3PathStyle.Checked = _settings.S3PathStyle;
        _s3Acl.Checked = _settings.S3PublicReadAcl;
        _s3KeyId.Text = _stored[CredentialStore.S3Target]?.UserName ?? "";
        _s3Preset.SelectedIndex = DetectS3Preset();
        ApplyS3Preset(fill: false);

        _davMode.SelectedIndex = _settings.WebDavMode == WebDavMode.Generic ? 1 : 0;
        _davServer.Text = _settings.WebDavServer;
        _davUser.Text = _stored[CredentialStore.WebDavTarget]?.UserName ?? "";
        _davFolder.Text = _settings.WebDavFolder;
        _davPublicUrl.Text = _settings.WebDavPublicBaseUrl;
        _davPublicUrl.Visible = _davPublicLabel.Visible = _davMode.SelectedIndex == 1;

        UpdateSecretPlaceholders();
        UpdatePatternPreview();
        UpdateFfmpegStatus();
        ShowDestinationPanel();
        _loading = false;
    }

    /// <summary>Escolhe o serviço S3 pelo endpoint salvo (sem disparar o preenchimento automático).</summary>
    private int DetectS3Preset()
    {
        string endpoint = _settings.S3Endpoint;
        for (int i = 0; i < S3Presets.Length - 1; i++)
        {
            string domain = S3Presets[i].Endpoint.Split("{r}")[^1];
            if (endpoint.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return endpoint.Length > 0 ? S3Presets.Length - 1 : 0;
    }

    private void UpdateSecretPlaceholders()
    {
        const string Kept = "(salva com segurança; deixe vazio para manter)";
        _r2Secret.PlaceholderText = _stored[CredentialStore.R2Target] is null ? "" : Kept;
        _s3Secret.PlaceholderText = _stored[CredentialStore.S3Target] is null ? "" : Kept;
        _sbSecret.PlaceholderText = _stored[CredentialStore.SupabaseTarget] is null ? "" : Kept;
        _imgurClientId.PlaceholderText = _stored[CredentialStore.ImgurTarget] is null ? "" : Kept;
        _davPassword.PlaceholderText = _stored[CredentialStore.WebDavTarget] is null ? "" : Kept;
    }

    private UploadDestination SelectedDestination => Destinations[Math.Max(0, _destination.SelectedIndex)];

    private void ShowDestinationPanel()
    {
        foreach ((UploadDestination d, Control panel) in _destinationPanels)
            panel.Visible = d == SelectedDestination;
        _testStatus.Text = "";
    }

    private static Keys ParseOrNone(string text) => HotkeyParser.TryParse(text, out Keys keys) ? keys : Keys.None;

    private void UpdatePatternPreview() =>
        _patternPreview.Text = "Exemplo: " + FileNamer.Sanitize(FileNamer.Expand(_pattern.Text, DateTime.Now)) + ".png";

    private void UpdateFfmpegStatus()
    {
        string? found = FfmpegLocator.Find(_ffmpeg.Text);
        _ffmpegStatus.ForeColor = found is null ? ThemeManager.Current.Danger : ThemeManager.Current.Success;
        _ffmpegStatus.Text = found is not null
            ? $"Encontrado: {found}"
            : string.IsNullOrWhiteSpace(_ffmpeg.Text)
                ? $"FFmpeg não encontrado. A gravação precisa dele. Instale abrindo o terminal e rodando:\n{FfmpegLocator.InstallCommand}"
                : "Caminho inválido: selecione o arquivo ffmpeg.exe.";
    }

    // ---------- Destinos de upload ----------

    private string Stored(string target, TextBox secretBox) =>
        secretBox.Text.Length > 0 ? secretBox.Text : _stored[target]?.Secret ?? "";

    private static bool AnyFilled(params Control[] fields) => fields.Any(f => f.Text.Trim().Length > 0);

    /// <summary>Monta o destino com os valores da tela (para testar ou validar antes de salvar).</summary>
    private bool TryBuild(UploadDestination destination, out IUploader? uploader, out string error)
    {
        uploader = null;
        switch (destination)
        {
            case UploadDestination.CloudflareR2:
                if (!R2Config.TryCreate(_r2Account.Text.Trim(), _r2Bucket.Text.Trim(), _r2PublicUrl.Text, _r2KeyId.Text.Trim(),
                        Stored(CredentialStore.R2Target, _r2Secret), out S3Target? r2, out error))
                    return false;
                uploader = new S3Client(r2!);
                return true;
            case UploadDestination.Supabase:
                if (!SupabaseConfig.TryCreate(_sbProject.Text, _sbRegion.Text, _sbBucket.Text, _sbKeyId.Text.Trim(),
                        Stored(CredentialStore.SupabaseTarget, _sbSecret), out S3Target? supabase, out error))
                    return false;
                uploader = new S3Client(supabase!);
                return true;
            case UploadDestination.S3Compatible:
                if (!S3Config.TryCreate(_s3Endpoint.Text, _s3Region.Text.Trim(), _s3Bucket.Text.Trim(), _s3PublicUrl.Text, _s3PathStyle.Checked,
                        _s3Acl.Checked, _s3KeyId.Text.Trim(), Stored(CredentialStore.S3Target, _s3Secret), out S3Target? s3, out error))
                    return false;
                uploader = new S3Client(s3!);
                return true;
            case UploadDestination.Imgur:
                if (!UploaderFactory.TryCreateImgur(Stored(CredentialStore.ImgurTarget, _imgurClientId), out ImgurClient? imgur, out error))
                    return false;
                uploader = imgur;
                return true;
            case UploadDestination.WebDav:
                if (!UploaderFactory.TryCreateWebDav(_davMode.SelectedIndex == 1 ? WebDavMode.Generic : WebDavMode.Nextcloud, _davServer.Text,
                        _davUser.Text.Trim(), Stored(CredentialStore.WebDavTarget, _davPassword), _davFolder.Text, _davPublicUrl.Text,
                        out WebDavTarget? dav, out error))
                    return false;
                uploader = new WebDavClient(dav!);
                return true;
            default:
                error = "Destino desconhecido.";
                return false;
        }
    }

    private bool IsFilled(UploadDestination destination) => destination switch
    {
        UploadDestination.CloudflareR2 => AnyFilled(_r2Account, _r2Bucket, _r2PublicUrl, _r2KeyId, _r2Secret),
        UploadDestination.S3Compatible => AnyFilled(_s3Endpoint, _s3Bucket, _s3PublicUrl, _s3KeyId, _s3Secret),
        UploadDestination.Supabase => AnyFilled(_sbProject, _sbRegion, _sbBucket, _sbKeyId, _sbSecret),
        UploadDestination.Imgur => AnyFilled(_imgurClientId) || _stored[CredentialStore.ImgurTarget] is not null,
        UploadDestination.WebDav => AnyFilled(_davServer, _davUser, _davPassword),
        _ => false,
    };

    private async Task TestDestinationAsync(ModernButton button)
    {
        if (!TryBuild(SelectedDestination, out IUploader? uploader, out string error))
        {
            SetTestStatus(error, ok: false);
            return;
        }

        button.Enabled = false;
        UseWaitCursor = true;
        SetTestStatus($"Testando {uploader!.Name}…", ok: true);
        try
        {
            (bool ok, string message) = await uploader.TestConnectionAsync();
            SetTestStatus(message, ok);
        }
        catch (Exception ex)
        {
            Log.Error("teste de conexão", ex);
            SetTestStatus(ex.Message, ok: false);
        }
        finally
        {
            button.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private void SetTestStatus(string text, bool ok)
    {
        _testStatus.ForeColor = ok ? ThemeManager.Current.Success : ThemeManager.Current.Danger;
        _testStatus.Text = text;
    }

    private void ForgetCredentials()
    {
        UploadDestination destination = SelectedDestination;
        string target = CredentialTarget(destination);
        if (MessageBox.Show(this, $"Remover as chaves salvas do {UploaderFactory.DisplayName(destination)} do Gerenciador de Credenciais do Windows?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        CredentialStore.Delete(target);
        _stored[target] = null;
        switch (destination)
        {
            case UploadDestination.CloudflareR2: _r2KeyId.Text = _r2Secret.Text = ""; break;
            case UploadDestination.S3Compatible: _s3KeyId.Text = _s3Secret.Text = ""; break;
            case UploadDestination.Supabase: _sbKeyId.Text = _sbSecret.Text = ""; break;
            case UploadDestination.Imgur: _imgurClientId.Text = ""; break;
            case UploadDestination.WebDav: _davUser.Text = _davPassword.Text = ""; break;
        }
        UpdateSecretPlaceholders();
        SetTestStatus("Chaves removidas.", ok: true);
    }

    private static string CredentialTarget(UploadDestination destination) => destination switch
    {
        UploadDestination.CloudflareR2 => CredentialStore.R2Target,
        UploadDestination.S3Compatible => CredentialStore.S3Target,
        UploadDestination.Supabase => CredentialStore.SupabaseTarget,
        UploadDestination.Imgur => CredentialStore.ImgurTarget,
        _ => CredentialStore.WebDavTarget,
    };

    /// <summary>Usuário/ID público e segredo de cada destino, como serão salvos no Gerenciador de Credenciais.</summary>
    private (string User, string Secret) CredentialsFromForm(UploadDestination destination) => destination switch
    {
        UploadDestination.CloudflareR2 => (_r2KeyId.Text.Trim(), Stored(CredentialStore.R2Target, _r2Secret)),
        UploadDestination.S3Compatible => (_s3KeyId.Text.Trim(), Stored(CredentialStore.S3Target, _s3Secret)),
        UploadDestination.Supabase => (_sbKeyId.Text.Trim(), Stored(CredentialStore.SupabaseTarget, _sbSecret)),
        UploadDestination.Imgur => ("imgur", Stored(CredentialStore.ImgurTarget, _imgurClientId).Trim()),
        _ => (_davUser.Text.Trim(), Stored(CredentialStore.WebDavTarget, _davPassword)),
    };

    /// <summary>Valida cada destino preenchido e guarda as chaves que mudaram.</summary>
    private bool ValidateDestinations()
    {
        foreach (UploadDestination destination in Destinations)
        {
            if (!IsFilled(destination))
                continue;
            if (!TryBuild(destination, out _, out string error))
            {
                ShowPage(5);
                _destination.SelectedIndex = Array.IndexOf(Destinations, destination);
                MessageBox.Show(this, $"Configuração do {UploaderFactory.DisplayName(destination)} incompleta:\n\n{error}", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string target = CredentialTarget(destination);
            (string user, string secret) = CredentialsFromForm(destination);
            if (_stored[target] is not { } stored || stored.UserName != user || stored.Secret != secret)
            {
                try
                {
                    CredentialStore.Save(target, user, secret);
                    _stored[target] = (user, secret);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
        }
        return true;
    }

    // ---------- Salvar ----------

    private void SaveAndClose()
    {
        HotkeyBox[] boxes = [_hkRegion, _hkLink, _hkFull, _hkWindow, _hkMp4, _hkGif];
        var repeated = boxes.Where(b => b.Hotkey != Keys.None).GroupBy(b => b.Hotkey).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
        {
            ShowPage(3);
            MessageBox.Show(this, $"O atalho {HotkeyParser.Format(repeated.Key)} está repetido.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!string.IsNullOrWhiteSpace(_ffmpeg.Text) && !FfmpegLocator.IsValid(_ffmpeg.Text))
        {
            ShowPage(4);
            MessageBox.Show(this, "O caminho do FFmpeg precisa apontar para um arquivo ffmpeg.exe existente.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!Path.IsPathFullyQualified(_folder.Text))
        {
            ShowPage(2);
            MessageBox.Show(this, "Informe um caminho completo para a pasta de capturas (ex.: C:\\Users\\...\\Pictures\\Sharelink).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!ValidateDestinations())
            return;

        _settings.Theme = ThemeOptions[Math.Max(0, _theme.SelectedIndex)].Value;
        _settings.ShowGalleryOnStartup = _showGallery.Checked;
        _settings.AutoUpdate = _autoUpdate.Checked;

        _settings.OpenEditorAfterCapture = _openEditor.Checked;
        _settings.SaveToFile = _saveToFile.Checked;
        _settings.ClipboardAction = ClipboardOptions[_clipboard.SelectedIndex].Value;
        _settings.ShowNotification = _notify.Checked;
        _settings.ImageFormat = _format.SelectedIndex == 1 ? ImageFormatOption.Jpeg : ImageFormatOption.Png;
        _settings.JpegQuality = (int)_jpegQuality.Value;

        _settings.OutputFolder = _folder.Text.Trim();
        _settings.FileNamePattern = _pattern.Text;
        _settings.UseMonthSubfolder = _monthFolder.Checked;

        _settings.Hotkeys.CaptureRegion = HotkeyParser.Format(_hkRegion.Hotkey);
        _settings.Hotkeys.CaptureAndLink = HotkeyParser.Format(_hkLink.Hotkey);
        _settings.Hotkeys.CaptureFullScreen = HotkeyParser.Format(_hkFull.Hotkey);
        _settings.Hotkeys.CaptureActiveWindow = HotkeyParser.Format(_hkWindow.Hotkey);
        _settings.Hotkeys.RecordMp4 = HotkeyParser.Format(_hkMp4.Hotkey);
        _settings.Hotkeys.RecordGif = HotkeyParser.Format(_hkGif.Hotkey);

        _settings.FfmpegPath = string.IsNullOrWhiteSpace(_ffmpeg.Text) ? null : _ffmpeg.Text.Trim();
        _settings.VideoFps = (int)_videoFps.Value;
        _settings.GifFps = (int)_gifFps.Value;
        _settings.RecordCursor = _recordCursor.Checked;

        _settings.Destination = SelectedDestination;
        _settings.ConfirmBeforeUpload = _confirmUpload.Checked;
        _settings.R2AccountId = _r2Account.Text.Trim();
        _settings.R2Bucket = _r2Bucket.Text.Trim();
        _settings.R2PublicBaseUrl = _r2PublicUrl.Text.Trim().TrimEnd('/');
        _settings.SupabaseProjectRef = _sbProject.Text.Trim().ToLowerInvariant();
        _settings.SupabaseRegion = _sbRegion.Text.Trim();
        _settings.SupabaseBucket = _sbBucket.Text.Trim();
        _settings.S3Endpoint = _s3Endpoint.Text.Trim().TrimEnd('/');
        _settings.S3Region = _s3Region.Text.Trim();
        _settings.S3Bucket = _s3Bucket.Text.Trim();
        _settings.S3PublicBaseUrl = _s3PublicUrl.Text.Trim().TrimEnd('/');
        _settings.S3PathStyle = _s3PathStyle.Checked;
        _settings.S3PublicReadAcl = _s3Acl.Checked;
        _settings.WebDavMode = _davMode.SelectedIndex == 1 ? WebDavMode.Generic : WebDavMode.Nextcloud;
        _settings.WebDavServer = _davServer.Text.Trim().TrimEnd('/');
        _settings.WebDavFolder = _davFolder.Text.Trim();
        _settings.WebDavPublicBaseUrl = _davPublicUrl.Text.Trim().TrimEnd('/');
        _settings.Normalize();

        try
        {
            StartupManager.SetEnabled(_startup.Checked);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("iniciar com o Windows", ex);
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
