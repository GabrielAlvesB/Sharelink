using Sharelink.Common;
using Sharelink.Native;
using Sharelink.Theme;

namespace Sharelink.Recording;

/// <summary>
/// Moldura vermelha em volta da área gravada + barrinha com tempo e botão Parar.
/// As duas janelas ficam fora da área e também são excluídas da captura pelo Windows.
/// </summary>
internal sealed class RecordingOverlay : IDisposable
{
    private readonly BorderForm _border;
    private readonly ToolbarForm _toolbar;

    public RecordingOverlay(Rectangle region, RecordingFormat format, Action stop)
    {
        _border = new BorderForm(region);
        _toolbar = new ToolbarForm(region, format, stop);
        _border.Show();
        _toolbar.Show();
    }

    public void SetStopping() => _toolbar.SetStopping();

    public void Dispose()
    {
        _toolbar.Close();
        _border.Close();
        _toolbar.Dispose();
        _border.Dispose();
    }

    private static void ExcludeFromCapture(Form form) =>
        NativeMethods.SetWindowDisplayAffinity(form.Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

    private sealed class BorderForm : OverlayForm
    {
        private const int Width_ = 3;

        public BorderForm(Rectangle region)
        {
            Rectangle bounds = region;
            bounds.Inflate(Width_, Width_);
            Bounds = bounds;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ExcludeFromCapture(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(230, 40, 40), Width_);
            e.Graphics.DrawRectangle(pen, Width_ / 2, Width_ / 2, ClientSize.Width - Width_, ClientSize.Height - Width_);
        }
    }

    private sealed class ToolbarForm : OverlayForm
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
        private readonly Label _time;
        private readonly Button _stop;
        private readonly DateTime _start = DateTime.Now;

        public ToolbarForm(Rectangle region, RecordingFormat format, Action stop)
        {
            Palette palette = ThemeManager.Current;
            BackColor = palette.Surface;
            // Tamanho proporcional à fonte, que já vem ajustada ao DPI.
            int unit = TextRenderer.MeasureText("M", Font).Height;
            Size = new Size(unit * 13, unit * 2 + 8);
            Padding = new Padding(6, 4, 4, 4);

            _time = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = palette.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = $"● {format.ToString().ToUpperInvariant()} 00:00",
            };
            _stop = new Button
            {
                Dock = DockStyle.Right,
                Width = unit * 5,
                Text = "Parar",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = palette.Danger,
                Cursor = Cursors.Hand,
            };
            _stop.FlatAppearance.BorderSize = 0;
            _stop.Click += (_, _) => stop();
            Controls.Add(_time);
            Controls.Add(_stop);

            Location = PickLocation(region, Size);
            _timer.Tick += (_, _) =>
            {
                TimeSpan t = DateTime.Now - _start;
                _time.Text = $"● {format.ToString().ToUpperInvariant()} {(int)t.TotalMinutes:00}:{t.Seconds:00}";
            };
            _timer.Start();
        }

        /// <summary>Abaixo da área; se não couber, acima; senão no canto interno (a janela é excluída da captura).</summary>
        private static Point PickLocation(Rectangle region, Size size)
        {
            Rectangle screen = Screen.FromRectangle(region).Bounds;
            int x = Math.Clamp(region.Right - size.Width, screen.Left, screen.Right - size.Width);
            if (region.Bottom + 6 + size.Height <= screen.Bottom)
                return new Point(x, region.Bottom + 6);
            if (region.Top - 6 - size.Height >= screen.Top)
                return new Point(x, region.Top - 6 - size.Height);
            return new Point(x - 10, region.Bottom - size.Height - 10);
        }

        public void SetStopping()
        {
            _timer.Stop();
            _stop.Enabled = false;
            _time.Text = "Finalizando…";
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ExcludeFromCapture(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
