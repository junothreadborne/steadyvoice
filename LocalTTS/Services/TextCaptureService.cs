using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace LocalTTS.Services;

public static class TextCaptureService {
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern IntPtr GetOpenClipboardWindow();

    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_C = 0x43;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static string? CaptureSelectedText() {
        var hwnd = GetForegroundWindow();
        Log.Debug($"Foreground window: {hwnd}");

        // Tier 1: Try UI Automation (no clipboard side effects)
        var text = TryUiaCapture();
        if (!string.IsNullOrWhiteSpace(text)) {
            Log.Debug($"UIA captured: {text.Length} chars");
            return text;
        }

        // Tier 2: Fall back to clipboard simulation
        Log.Debug("UIA returned nothing, falling back to clipboard");
        return TryClipboardCapture();
    }

    private static string? TryUiaCapture() {
        try {
            // Start with the focused element — the control the user is interacting with
            var focused = AutomationElement.FocusedElement;
            if (focused == null) {
                return null;
            }

            // Walk from focused element up through ancestors looking for TextPattern
            var current = focused;
            var walker = TreeWalker.ControlViewWalker;

            for (var depth = 0; depth < 10 && current != null; depth++) {
                if (current.TryGetCurrentPattern(TextPattern.Pattern, out var obj)) {
                    var textPattern = (TextPattern)obj;
                    var selections = textPattern.GetSelection();
                    if (selections.Length > 0) {
                        var sb = new StringBuilder();
                        foreach (var range in selections) {
                            sb.Append(range.GetText(-1));
                        }
                        var result = sb.ToString();
                        if (!string.IsNullOrWhiteSpace(result)) {
                            return result;
                        }
                    }
                }

                if (Automation.Compare(current, AutomationElement.RootElement)) {
                    break;
                }

                current = walker.GetParent(current);
            }

            return null;
        } catch (Exception ex) {
            Log.Debug($"UIA capture failed: {ex.Message}");
            return null;
        }
    }

    private static string? TryClipboardCapture() {
        // Release ALL modifier keys and wait until they're actually up
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        // Poll until modifiers are confirmed released (max 500ms)
        for (var i = 0; i < 50; i++) {
            Thread.Sleep(10);
            var ctrlHeld = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            var shiftHeld = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            if (!ctrlHeld && !shiftHeld) {
                break;
            }
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        var ctrlState = GetAsyncKeyState(VK_CONTROL);
        var shiftState = GetAsyncKeyState(VK_SHIFT);
        Log.Debug($"After release - Ctrl: {ctrlState}, Shift: {shiftState}");

        // Record clipboard sequence number BEFORE Ctrl+C (does not open/lock clipboard)
        var seqBefore = GetClipboardSequenceNumber();

        // Send Ctrl+C via keybd_event with sufficient delays
        Log.Debug("Sending Ctrl+C via keybd_event...");
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        keybd_event(VK_C, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(60);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        // Wait for clipboard sequence number to change (non-locking check)
        var clipboardChanged = false;
        for (var i = 0; i < 60; i++) {
            Thread.Sleep(50);
            if (GetClipboardSequenceNumber() != seqBefore) {
                clipboardChanged = true;
                break;
            }
        }

        Log.Debug($"Clipboard changed: {clipboardChanged} (seq: {GetClipboardSequenceNumber()})");

        if (!clipboardChanged) {
            Log.Debug("Ctrl+C did not update clipboard - nothing selected");
            return null;
        }

        // Brief pause to let any clipboard manager finish processing
        Thread.Sleep(150);

        // Read clipboard with retries and increasing backoff
        string? text = null;
        for (var attempt = 0; attempt < 10; attempt++) {
            try {
                if (Clipboard.ContainsText()) {
                    text = Clipboard.GetText();
                    Log.Debug($"Clipboard read on attempt {attempt + 1}: {text?.Length ?? 0} chars");
                    break;
                }
                var data = Clipboard.GetDataObject();
                var formats = data?.GetFormats() ?? [];
                Log.Debug($"Clipboard formats (attempt {attempt + 1}): {string.Join(", ", formats)}");
                break;
            } catch (Exception ex) {
                var holder = GetOpenClipboardWindow();
                Log.Error($"Clipboard read attempt {attempt + 1} (locked by HWND {holder})", ex);
                Thread.Sleep(150 + (attempt * 100));
            }
        }

        Log.Debug($"Captured text: {(text != null ? $"{text.Length} chars" : "null")}");
        return text;
    }
}
