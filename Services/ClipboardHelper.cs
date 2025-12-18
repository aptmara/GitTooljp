namespace SimplePRClient.Services;

using System;
using System.Threading;
using System.Windows;

public static class ClipboardHelper
{
    public static void SetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                // CLIPBRD_E_CANT_OPEN (0x800401D0)
                if (unchecked((uint)ex.ErrorCode) == 0x800401D0)
                {
                    Thread.Sleep(50); // Wait a bit
                    continue;
                }
                throw;
            }
        }
        
        // If failed after retries, try one last time or show error without crashing
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"クリップボードへのコピーに失敗しました。\n\n{ex.Message}", "コピー失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
