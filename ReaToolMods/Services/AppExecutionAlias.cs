using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ReaToolMods.Services;

/// <summary>
/// Microsoft Store 앱의 실행 별칭(%LOCALAPPDATA%\Microsoft\WindowsApps\*.exe)을 실제 exe 경로로 풀어 줍니다.
/// 별칭은 0바이트 재분석 지점(IO_REPARSE_TAG_APPEXECLINK)이라 직접 열면 "시스템에서 파일에 액세스할 수 없습니다" 오류가 납니다.
/// </summary>
public static class AppExecutionAlias
{
    private const uint IO_REPARSE_TAG_APPEXECLINK = 0x8000001B;
    private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const int MaximumReparseDataBufferSize = 16 * 1024;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, int inBufferSize,
        byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    /// <summary>
    /// path가 앱 실행 별칭이면 true. target에는 별칭이 가리키는 실제 exe 경로(읽지 못하면 null)가 들어갑니다.
    /// </summary>
    public static bool TryResolve(string path, out string? target)
    {
        target = null;
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
        }
        catch
        {
            return false;
        }

        // 대상이 아니라 재분석 지점 자체를 열어야 함 (FSCTL_GET_REPARSE_POINT는 접근 권한 0으로 충분)
        using var handle = CreateFileW(path, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle.IsInvalid) return false;

        var buffer = new byte[MaximumReparseDataBufferSize];
        if (!DeviceIoControl(handle, FSCTL_GET_REPARSE_POINT, IntPtr.Zero, 0, buffer, buffer.Length, out var n, IntPtr.Zero) || n < 12)
            return false;
        if (BitConverter.ToUInt32(buffer, 0) != IO_REPARSE_TAG_APPEXECLINK) return false;

        // REPARSE_DATA_BUFFER 헤더(태그 4 + 길이 2 + 예약 2) 뒤: 버전(4) + 널 종료 UTF-16 문자열 [패키지 ID, AppUserModelID, 대상 exe, ...]
        var stringBytes = Math.Min(BitConverter.ToUInt16(buffer, 4), n - 8) - 4;
        if (stringBytes > 0)
        {
            var strings = Encoding.Unicode.GetString(buffer, 12, stringBytes).Split('\0');
            if (strings.Length > 2 && strings[2].Length > 0) target = strings[2];
        }

        return true;
    }
}
