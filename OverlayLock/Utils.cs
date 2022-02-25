using System;
using Vanara.PInvoke;

namespace OverlayLock;

public static class Utils
{
    public static void MakeFullScreenOverlay(IntPtr hWnd, bool clickable = false)
    {
        var flag = 0
                   | User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE)
                   // hide in alt tab
                   | (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW 
                   | 0;
        if (!clickable)
        {
            // make entire window click through
            flag |= (int)User32.WindowStylesEx.WS_EX_TRANSPARENT;
            flag |= (int)User32.WindowStylesEx.WS_EX_LAYERED;
        }
            
        User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, flag);
        User32.SetWindowPos(hWnd, HWND.NULL, 0, 0, 0, 0, 0
                                                          | User32.SetWindowPosFlags.SWP_NOSIZE
                                                          | User32.SetWindowPosFlags.SWP_NOMOVE
                                                          | 0);
        DwmApi.MARGINS margins = new DwmApi.MARGINS(-1);
        DwmApi.DwmExtendFrameIntoClientArea(hWnd, margins);
    }
}