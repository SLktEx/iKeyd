namespace iKeyd.Profiles.HotkeySkg.Runtime;

public interface IHotkeySkgInteractiveActions
{
    void RunMacro(char slot);
    void EditMacro(char slot);
    void EditMacroRepeat();
    void ShowClipboardHistory();
    void CaptureLatestClipboard();
    void PasteCapturedClipboard();
}
