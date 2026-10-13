using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using ReaToolMods.Models;

namespace ReaToolMods.Services;

/// <summary>
/// 분석 도구(ilspycmd, JADX, Il2CppDumper 등)와 저장소/도구 폴더 위치를 찾습니다.
/// 결과는 캐시되며 환경 점검을 다시 실행할 때 Invalidate()로 갱신합니다.
/// </summary>
public static class ToolLocator
{
    private static IReadOnlyDictionary<ToolId, ToolLocation>? _cache;

    public static string? RepoRoot { get; } = FindRepoRoot();

    public static string ToolsDir =>
        Path.Combine(RepoRoot ?? AppContext.BaseDirectory, "tools");

    public static string? BatPath(string fileName) =>
        RepoRoot == null ? null : Path.Combine(RepoRoot, "bat", fileName);

    public static void Invalidate() => _cache = null;

    public static IReadOnlyDictionary<ToolId, ToolLocation> LocateAll() => _cache ??= Locate();

    private static IReadOnlyDictionary<ToolId, ToolLocation> Locate()
    {
        const string downloader = @"bat\download_analysis_tools.bat 실행";
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var list = new[]
        {
            new ToolLocation(ToolId.DotnetSdk, ".NET SDK", "dotnet",
                FindOnPath("dotnet"), "https://dotnet.microsoft.com/download 에서 .NET SDK 설치"),
            new ToolLocation(ToolId.Ilspy, "ilspycmd", "ilspycmd",
                FindOnPath("ilspycmd") ?? ExistingFile(Path.Combine(userProfile, ".dotnet", "tools", "ilspycmd.exe")),
                "dotnet tool install -g ilspycmd (또는 " + downloader + ")"),
            new ToolLocation(ToolId.Java, "Java (JRE/JDK)", "java",
                FindOnPath("java") ?? FindJavaHome(), "JDK 17+ 설치 (JADX · Apktool 실행용)"),
            new ToolLocation(ToolId.Jadx, "JADX", "jadx",
                ExistingFile(Path.Combine(ToolsDir, "jadx", "bin", "jadx.bat")) ?? FindOnPath("jadx"), downloader),
            new ToolLocation(ToolId.Apktool, "Apktool", "apktool",
                (File.Exists(Path.Combine(ToolsDir, "apktool", "apktool.jar"))
                    ? ExistingFile(Path.Combine(ToolsDir, "apktool", "apktool.bat"))
                    : null) ?? FindOnPath("apktool"), downloader),
            new ToolLocation(ToolId.Node, "Node.js", "node",
                FindOnPath("node"), "https://nodejs.org 에서 Node.js 22+ 설치"),
            new ToolLocation(ToolId.Asar, "@electron/asar", "asar",
                FindOnPath("asar"), "npm install -g @electron/asar (또는 " + downloader + ")"),
            new ToolLocation(ToolId.Il2CppDumper, "Il2CppDumper", "Il2CppDumper",
                FindInToolsDir("il2cppdumper", "Il2CppDumper.exe") ?? FindOnPath("Il2CppDumper"), downloader),
            new ToolLocation(ToolId.Cpp2Il, "Cpp2IL", "Cpp2IL",
                FindInToolsDir("cpp2il", "Cpp2IL*.exe") ?? FindOnPath("Cpp2IL"), downloader),
            // 녹스가 있으면 nox_adb를 우선: 다른 버전의 adb를 쓰면 녹스의 adb 서버를 재시작시켜 연결이 끊김
            new ToolLocation(ToolId.Adb, "ADB", "adb",
                (FindNoxBinDir() is { } noxBin ? ExistingFile(Path.Combine(noxBin, "nox_adb.exe")) : null)
                    ?? FindOnPath("adb")
                    ?? ExistingFile(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe")),
                "Android SDK platform-tools 설치 또는 녹스(nox_adb.exe) 설치"),
            new ToolLocation(ToolId.ReaCli, "REA CLI", "rea",
                FindOnPath("rea"), @"npm install -g rea-agents (또는 bat\setup_rea_workflows.bat 실행)"),
        };

        return list.ToDictionary(t => t.Id);
    }

    /// <summary>
    /// 녹스(NoxPlayer) 설치 경로의 bin 폴더를 레지스트리 및 기본 설치 경로에서 찾습니다.
    /// </summary>
    public static string? FindNoxBinDir()
    {
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            string[] uninstallKeys =
            [
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
            ];
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var keyPath in uninstallKeys)
                {
                    try
                    {
                        using var key = root.OpenSubKey(keyPath);
                        if (key == null) continue;
                        foreach (var name in key.GetSubKeyNames())
                        {
                            using var sub = key.OpenSubKey(name);
                            var displayName = sub?.GetValue("DisplayName") as string ?? "";
                            if (!displayName.Contains("Nox", StringComparison.OrdinalIgnoreCase)) continue;

                            if (sub?.GetValue("InstallLocation") is string loc && loc.Length > 0)
                                candidates.Add(loc.Trim('"'));
                            if (sub?.GetValue("DisplayIcon") is string icon && icon.Length > 0)
                                candidates.Add(Path.GetDirectoryName(icon.Split(',')[0].Trim('"')) ?? "");
                            if (sub?.GetValue("UninstallString") is string un && un.Length > 0)
                                candidates.Add(Path.GetDirectoryName(un.Trim('"').Split(".exe")[0] + ".exe") ?? "");
                        }
                    }
                    catch { }
                }
            }
        }

        foreach (var pf in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     @"D:\Program Files", @"D:\Program Files (x86)"
                 })
        {
            if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, "Nox"));
        }

        foreach (var dir in candidates.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            foreach (var bin in new[] { dir, Path.Combine(dir, "bin") })
            {
                if (File.Exists(Path.Combine(bin, "Nox.exe")) || File.Exists(Path.Combine(bin, "nox_adb.exe")))
                    return bin;
            }
        }

        return null;
    }

    public static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exts = Path.HasExtension(name)
            ? new[] { "" }
            :(Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in exts)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim('"'), name + ext);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        return null;
    }

    private static string? FindInToolsDir(string folder, string pattern)
    {
        var dir = Path.Combine(ToolsDir, folder);
        if (!Directory.Exists(dir)) return null;
        try
        {
            return Directory.EnumerateFiles(dir, pattern,
                    new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2 })
                .OrderBy(f => f.Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindJavaHome()
    {
        var home = Environment.GetEnvironmentVariable("JAVA_HOME");
        return string.IsNullOrEmpty(home) ? null : ExistingFile(Path.Combine(home, "bin", "java.exe"));
    }

    private static string? ExistingFile(string path) => File.Exists(path) ? path : null;

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ReaToolMods.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}
