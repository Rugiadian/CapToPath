using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CapToPath
{
    public static class ClipboardHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        /// <summary>
        /// Robustly sets text to Windows clipboard using both Native Win32 API and WinForms fallback.
        /// Retries for up to 3.5 seconds to wait for PicPick or other tools to release the clipboard lock.
        /// </summary>
        public static bool CopyText(string text, out string errorDetail)
        {
            errorDetail = string.Empty;
            if (text == null) text = string.Empty;

            int maxAttempts = 70; // 70 attempts * 50ms = 3.5 seconds max wait
            int lastWin32Error = 0;
            string lastExceptionMessage = string.Empty;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                // Method 1: High-performance Native Win32 Clipboard (bypasses COM/OLE issues)
                if (TrySetTextNative(text, out lastWin32Error))
                {
                    // Verify clipboard has our text
                    if (VerifyClipboardContent(text))
                    {
                        return true;
                    }
                }

                // Method 2: Windows Forms OLE Clipboard fallback
                try
                {
                    Clipboard.SetDataObject(text, copy: true, retryTimes: 2, retryDelay: 25);
                    if (VerifyClipboardContent(text))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    lastExceptionMessage = ex.Message;
                }

                Thread.Sleep(50);
            }

            errorDetail = $"클립보드 잠금 해제 지연 (Win32: {lastWin32Error}, OLE: {lastExceptionMessage})";
            return false;
        }

        private static bool TrySetTextNative(string text, out int win32Error)
        {
            win32Error = 0;

            if (!OpenClipboard(IntPtr.Zero))
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                if (!EmptyClipboard())
                {
                    win32Error = Marshal.GetLastWin32Error();
                    return false;
                }

                // UTF-16 null-terminated characters
                int byteCount = (text.Length + 1) * 2;
                IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
                if (hGlobal == IntPtr.Zero)
                {
                    win32Error = Marshal.GetLastWin32Error();
                    return false;
                }

                IntPtr target = GlobalLock(hGlobal);
                if (target == IntPtr.Zero)
                {
                    win32Error = Marshal.GetLastWin32Error();
                    GlobalFree(hGlobal);
                    return false;
                }

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    Marshal.WriteInt16(target + (text.Length * 2), 0); // null terminator
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                IntPtr result = SetClipboardData(CF_UNICODETEXT, hGlobal);
                if (result == IntPtr.Zero)
                {
                    win32Error = Marshal.GetLastWin32Error();
                    GlobalFree(hGlobal);
                    return false;
                }

                // System now owns hGlobal
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static bool VerifyClipboardContent(string expectedText)
        {
            try
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        IntPtr hData = GetClipboardData(CF_UNICODETEXT);
                        if (hData != IntPtr.Zero)
                        {
                            IntPtr ptr = GlobalLock(hData);
                            if (ptr != IntPtr.Zero)
                            {
                                try
                                {
                                    string? content = Marshal.PtrToStringUni(ptr);
                                    if (string.Equals(content, expectedText, StringComparison.Ordinal))
                                    {
                                        return true;
                                    }
                                }
                                finally
                                {
                                    GlobalUnlock(hData);
                                }
                            }
                        }
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
            }
            catch
            {
                // Ignore verification errors, fallback to checking via WinForms
            }

            try
            {
                return string.Equals(Clipboard.GetText(), expectedText, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}
