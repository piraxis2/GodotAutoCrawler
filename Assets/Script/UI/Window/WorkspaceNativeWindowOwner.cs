using System;
using System.Runtime.InteropServices;
using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

/// <summary>
/// Windows native pop-out 창을 마스터의 owned window로 묶는다.
/// Godot transient/exclusive 플래그만으로는 "마스터 위 z-order"와 "마스터 입력 허용"을 동시에
/// 안정적으로 만족하기 어렵기 때문에, Win32 owner 관계를 직접 보강한다.
/// </summary>
internal static class WorkspaceNativeWindowOwner
{
    private const int GwlpHwndParent = -8;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    public static bool TryAttachToMaster(Godot.Window child, Godot.Window master)
    {
        if (!GodotObject.IsInstanceValid(child) || !GodotObject.IsInstanceValid(master)) return false;

        int childId = child.GetWindowId();
        int masterId = master.GetWindowId();
        if (childId == masterId || childId < 0 || masterId < 0) return false;

        // Godot 쪽 플래그는 모달 입력 잠금을 만들지 않는 값만 보장한다.
        // WindowSetTransient(child, parent)는 같은 parent에 반복 호출하면 Godot Windows backend가 에러 로그를 찍으므로
        // native owner 관계는 아래 Win32 HWND owner 보강에 맡긴다.
        if (!child.Transient) child.Transient = true;
        if (child.TransientToFocused) child.TransientToFocused = false;
        if (child.Exclusive) child.Exclusive = false;

        if (OS.GetName() != "Windows") return true;

        IntPtr childHandle = GetWindowHandle(childId);
        IntPtr masterHandle = GetWindowHandle(masterId);
        if (childHandle == IntPtr.Zero || masterHandle == IntPtr.Zero) return true;

        SetWindowLongPtr(childHandle, GwlpHwndParent, masterHandle);
        SetWindowPos(childHandle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        return true;
    }

    private static IntPtr GetWindowHandle(int windowId)
    {
        long handle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, windowId);
        return handle == 0 ? IntPtr.Zero : new IntPtr(handle);
    }

    private static IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : new IntPtr(SetWindowLong32(windowHandle, index, value.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}



