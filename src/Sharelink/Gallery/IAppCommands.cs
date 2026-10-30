using Sharelink.Recording;

namespace Sharelink.Gallery;

/// <summary>Ações do app que a galeria pode disparar (implementadas pelo <see cref="TrayApplication"/>).</summary>
internal interface IAppCommands
{
    void CaptureRegion(bool generateLink);
    void CaptureFullScreen();
    void StartRecording(RecordingFormat format);
    void EditFile(string path);
    void OpenOutputFolder();
    void ShowSettings(bool openLinksTab);
}
