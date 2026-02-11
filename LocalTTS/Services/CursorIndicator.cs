using System.Runtime.InteropServices;

namespace LocalTTS.Services;

public static class CursorIndicator {
    private const uint OCR_APPSTARTING = 32650; // Arrow + hourglass
    private const uint OCR_NORMAL = 32512;
    private const uint IMAGE_CURSOR = 2;
    private const uint LR_SHARED = 0x8000;

    [DllImport("user32.dll")]
    private static extern IntPtr LoadImage(IntPtr hInst, uint name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    private const uint SPI_SETCURSORS = 0x0057;

    public static void ShowBusy() {
        try {
            var busyCursor = LoadImage(IntPtr.Zero, OCR_APPSTARTING, IMAGE_CURSOR, 0, 0, LR_SHARED);
            if (busyCursor != IntPtr.Zero) {
                var copy = CopyIcon(busyCursor);
                SetSystemCursor(copy, OCR_NORMAL);
            }
        } catch { }
    }

    public static void Restore() {
        try {
            SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
        } catch { }
    }
}
