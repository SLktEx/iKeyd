using iKeyd.Core.Input;
using iKeyd.Core.Platform;

namespace iKeyd.Wayland.Input;

public interface IWaylandVirtualInput : IKeyboardOutput, IBackendCapabilityProvider
{
    void EmitKeyCode(ushort evdevCode, int value);
    void MovePointerBy(int deltaX, int deltaY);
    void SetMouseButton(ushort buttonCode, bool down);
    void ClickMouseButton(ushort buttonCode);
    void ScrollVertical(int wheelClicks);
    void SendMediaKey(ushort keyCode);
}
