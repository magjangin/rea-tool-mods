using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ReaToolMods.Services;

public static class WindowsAutomation
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const byte VK_RETURN = 0x0D;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int SW_RESTORE = 9;

    /// <summary>
    /// 열려있는 프로세스 이름이나 창 제목을 기반으로 메인 윈도우 핸들(HWND)을 검색합니다.
    /// </summary>
    public static IntPtr FindOpenWindow(string processNameKeyword, string windowTitleKeyword)
    {
        IntPtr foundHwnd = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            int len = GetWindowTextLength(hWnd);
            StringBuilder sb = new StringBuilder(len + 1);
            if (len > 0)
            {
                GetWindowText(hWnd, sb, sb.Capacity);
            }
            string title = sb.ToString();

            GetWindowThreadProcessId(hWnd, out uint pid);

            try
            {
                var proc = Process.GetProcessById((int)pid);
                bool matchProc = !string.IsNullOrEmpty(processNameKeyword) &&
                                 proc.ProcessName.Contains(processNameKeyword, StringComparison.OrdinalIgnoreCase);
                bool matchTitle = !string.IsNullOrEmpty(windowTitleKeyword) &&
                                  title.Contains(windowTitleKeyword, StringComparison.OrdinalIgnoreCase);

                // Electron 메인 창은 일반적으로 프로세스명이 일치하고 타이틀이 있거나 표시 가능한 창입니다.
                if (matchProc || matchTitle)
                {
                    // 타이틀이 있는 창을 우선시
                    if (len > 0 || foundHwnd == IntPtr.Zero)
                    {
                        foundHwnd = hWnd;
                        if (len > 0) return false; // 제목이 있는 메인 창 발견 시 즉시 반환
                    }
                }
            }
            catch { }

            return true;
        }, IntPtr.Zero);

        // 프로세스 MainWindowHandle 대체 검색
        if (foundHwnd == IntPtr.Zero && !string.IsNullOrEmpty(processNameKeyword))
        {
            try
            {
                var procs = Process.GetProcessesByName(processNameKeyword);
                foreach (var p in procs)
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        return p.MainWindowHandle;
                    }
                }
            }
            catch { }
        }

        return foundHwnd;
    }

    /// <summary>
    /// 지정된 창을 최상위 전면으로 강제 포커스합니다.
    /// </summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;

        IntPtr fgWnd = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fgWnd, out _);
        uint curThread = GetCurrentThreadId();

        if (fgThread != curThread && fgThread != 0)
        {
            AttachThreadInput(curThread, fgThread, true);
            ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
            AttachThreadInput(curThread, fgThread, false);
        }
        else
        {
            ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
        }
    }

    /// <summary>
    /// 열려있는 대상 앱 창으로 이동하여 프롬프트를 자동 붙여넣기(Ctrl+V)하고 엔터(Enter)를 입력하여 즉시 전송합니다.
    /// </summary>
    public static async Task<bool> SendPromptToWindowAsync(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        // 1. 대상 창을 포커스
        ForceForeground(hWnd);
        await Task.Delay(350);

        // 2. Ctrl + V 입력 (클립보드 붙여넣기)
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        await Task.Delay(250);

        // 3. Enter 입력 (전송)
        keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        return true;
    }
}
