using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalTTS.Services;

public class HotkeyService {
    private const int HOTKEY_ID = 9000;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_R = 0x52;
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private Window? _messageWindow;
    private HwndSource? _source;
    private IntPtr _windowHandle;

    public event Action? HotkeyPressed;

    public void Register() {
        _messageWindow = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false };
        var helper = new WindowInteropHelper(_messageWindow);
        helper.EnsureHandle();
        _windowHandle = helper.Handle;

        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(HwndHook);

        if (!RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CTRL | MOD_SHIFT, VK_R)) {
            MessageBox.Show("Failed to register hotkey Ctrl+Shift+R. It may be in use by another application.", "LocalTTS");
        }
    }

    public void Unregister() {
        if (_windowHandle != IntPtr.Zero) {
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
        }
        _source?.RemoveHook(HwndHook);
        _source?.Dispose();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID) {
            Log.Debug("WM_HOTKEY received");
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
