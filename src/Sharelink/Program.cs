using Sharelink.Common;
using Sharelink.Settings;

namespace Sharelink;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // "Local\" = uma instância por sessão do Windows.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\Sharelink-SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("O Sharelink já está aberto. Procure o ícone na bandeja, perto do relógio.", "Sharelink",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        // Garante que "await" volte para a thread da interface mesmo antes de existir alguma janela.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        Application.ThreadException += (_, e) =>
        {
            Log.Error("não tratado", e.Exception);
            MessageBox.Show($"Ocorreu um erro inesperado:\n\n{e.Exception.Message}\n\nDetalhes em {Log.FilePath}",
                "Sharelink", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Error("fatal", ex);
        };

        bool startMinimized = args.Contains(StartupManager.MinimizedArgument, StringComparer.OrdinalIgnoreCase);
        Application.Run(new TrayApplication(startMinimized));
    }
}
