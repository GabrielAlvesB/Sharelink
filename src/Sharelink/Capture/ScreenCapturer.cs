using System.Drawing.Imaging;
using Sharelink.Native;

namespace Sharelink.Capture;

internal static class ScreenCapturer
{
    /// <summary>Área de todos os monitores juntos, em pixels físicos.</summary>
    public static Rectangle VirtualScreen => SystemInformation.VirtualScreen;

    public static Bitmap CaptureRectangle(Rectangle area)
    {
        var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static Bitmap CaptureFullScreen() => CaptureRectangle(VirtualScreen);

    /// <returns>null se não houver janela ativa capturável (ex.: área de trabalho vazia).</returns>
    public static Bitmap? CaptureActiveWindow()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        Rectangle bounds = Rectangle.Intersect(NativeMethods.GetWindowBounds(hwnd), VirtualScreen);
        return bounds.Width > 0 && bounds.Height > 0 ? CaptureRectangle(bounds) : null;
    }

    /// <summary>Janelas visíveis, da mais à frente para a mais ao fundo (para o clique "selecionar janela").</summary>
    public static List<Rectangle> GetVisibleWindows()
    {
        var result = new List<Rectangle>();
        uint ownPid = (uint)Environment.ProcessId;
        Rectangle screen = VirtualScreen;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd) || NativeMethods.IsCloaked(hwnd))
                return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid)
                return true;

            Rectangle r = Rectangle.Intersect(NativeMethods.GetWindowBounds(hwnd), screen);
            if (r.Width > 1 && r.Height > 1)
                result.Add(r);
            return true;
        }, IntPtr.Zero);

        return result;
    }
}
