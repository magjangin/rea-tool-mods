using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ReaToolMods.Models;

namespace ReaToolMods.Services;

public enum CheckStatus { Ok, Warning, Missing, Info }

public sealed record EnvironmentCheck(string Group, string Name, CheckStatus Status, string Detail, string UsedBy)
{
    public string StatusIcon => Status switch
    {
        CheckStatus.Ok => "✅",
        CheckStatus.Warning => "⚠️",
        CheckStatus.Missing => "❌",
        _ => "ℹ️"
    };
}

/// <summary>
/// 분석에 필요한 도구, 시스템 설정(VT), 에뮬레이터, REA 연동, 대상 게임의 모드 로더 설치 여부를 점검합니다.
/// </summary>
public static partial class EnvironmentChecker
{
    private const string GroupTools = "분석 도구";
    private const string GroupAndroid = "Android · 에뮬레이터";
    private const string GroupAgent = "REA · 에이전트";
    public const string GroupGame = "대상 게임 플러그인";

    [DllImport("kernel32.dll")]
    private static extern bool IsProcessorFeaturePresent(uint processorFeature);

    private const uint PF_VIRT_FIRMWARE_ENABLED = 21;

    [GeneratedRegex(@"version ""(\d+)(?:\.(\d+))?")]
    private static partial Regex JavaVersionRegex();

    [GeneratedRegex(@"Android Debug Bridge version (\d+\.\d+\.\d+)")]
    private static partial Regex AdbVersionRegex();

    public static async Task<IReadOnlyList<EnvironmentCheck>> RunAsync(DetectionResult? detection)
    {
        ToolLocator.Invalidate();
        var tools = await Task.Run(ToolLocator.LocateAll);

        var adbCheck = VersionCheck(tools[ToolId.Adb], "version", "APK · Unity APK (녹스)", GroupAndroid);
        var toolChecks = await Task.WhenAll(
            VersionCheck(tools[ToolId.DotnetSdk], "--version", "DLL · Unity 모드 빌드"),
            VersionCheck(tools[ToolId.Ilspy], "--version", "DLL · Unity Mono/IL2CPP"),
            JavaCheck(tools[ToolId.Java]),
            VersionCheck(tools[ToolId.Jadx], "--version", "APK · Unity APK"),
            VersionCheck(tools[ToolId.Apktool], "--version", "APK"),
            VersionCheck(tools[ToolId.Node], "--version", "APP(Electron) · REA CLI"),
            VersionCheck(tools[ToolId.Asar], "--version", "APP(Electron)"),
            Task.FromResult(FileCheck(tools[ToolId.Il2CppDumper], "Unity IL2CPP · Unity APK/APP")),
            Task.FromResult(FileCheck(tools[ToolId.Cpp2Il], "Unity IL2CPP (대체 덤퍼)")),
            VersionCheck(tools[ToolId.ReaCli], "--version", "EXE · 네이티브 분석", GroupAgent),
            VersionCheck(tools[ToolId.PeSlice], "--version", "Unity IL2CPP · EXE (큰 PE의 REA 타임아웃 회피)", GroupAgent),
            VersionCheck(tools[ToolId.Il2CppAsm], "--version", "Unity APK · APP · IL2CPP (REA가 못 여는 ELF · Mach-O)", GroupAgent));

        var results = toolChecks.ToList();
        results.Add(VirtualizationCheck());
        results.Add(await Task.Run(NoxCheck));
        results.Add(await adbCheck);
        if (await AdbConflictCheckAsync() is { } conflict) results.Add(conflict);
        results.Add(ReaSkillCheck());
        results.Add(await Task.Run(ReaMcpCheck));
        results.AddRange(AgentApps.All.Select(AgentAppCheck));
        if (detection != null) results.AddRange(GameChecks(detection));

        string[] order = [GroupTools, GroupAndroid, GroupAgent, GroupGame];
        return results.OrderBy(r => Array.IndexOf(order, r.Group)).ToList();
    }

    // ───────────────────────── 도구 ─────────────────────────

    private static async Task<EnvironmentCheck> VersionCheck(ToolLocation tool, string args, string usedBy, string group = GroupTools)
    {
        if (!tool.Found) return Missing(tool, usedBy, group);

        var (ok, output) = await RunAsync(tool.Path!, args);
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        return ok
            ? new EnvironmentCheck(group, tool.DisplayName, CheckStatus.Ok, $"{firstLine} — {tool.Path}", usedBy)
            : new EnvironmentCheck(group, tool.DisplayName, CheckStatus.Warning, $"설치되어 있지만 실행 실패: {Shorten(firstLine)} — {tool.Path}", usedBy);
    }

    private static async Task<EnvironmentCheck> JavaCheck(ToolLocation tool)
    {
        const string usedBy = "JADX · Apktool";
        if (!tool.Found)
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            return string.IsNullOrEmpty(javaHome) || File.Exists(Path.Combine(javaHome, "bin", "java.exe"))
                ? Missing(tool, usedBy)
                : new EnvironmentCheck(GroupTools, tool.DisplayName, CheckStatus.Missing,
                    $"JAVA_HOME이 없는 경로를 가리킵니다({javaHome}) → JDK 17+ 설치 후 JAVA_HOME을 실제 설치 경로로 고치세요" +
                    (javaHome.TrimEnd('\\').EndsWith(@"\bin", StringComparison.OrdinalIgnoreCase) ? " (JAVA_HOME 끝의 \\bin은 빼야 합니다)" : ""),
                    usedBy);
        }

        var (ok, output) = await RunAsync(tool.Path!, "-version");
        var m = JavaVersionRegex().Match(output);
        if (!ok || !m.Success)
            return new EnvironmentCheck(GroupTools, tool.DisplayName, CheckStatus.Warning, $"버전 확인 실패 — {tool.Path}", usedBy);

        var major = int.Parse(m.Groups[1].Value);
        if (major == 1 && m.Groups[2].Success) major = int.Parse(m.Groups[2].Value); // 1.8 → 8
        return major >= 11
            ? new EnvironmentCheck(GroupTools, tool.DisplayName, CheckStatus.Ok, $"Java {major} — {tool.Path}", usedBy)
            : new EnvironmentCheck(GroupTools, tool.DisplayName, CheckStatus.Warning, $"Java {major} — JADX/Apktool은 Java 11 이상이 필요합니다", usedBy);
    }

    private static EnvironmentCheck FileCheck(ToolLocation tool, string usedBy)
    {
        if (!tool.Found) return Missing(tool, usedBy);
        var info = FileVersionInfo.GetVersionInfo(tool.Path!);
        var version = string.IsNullOrEmpty(info.FileVersion) ? "" : $"v{info.FileVersion} — ";
        return new EnvironmentCheck(GroupTools, tool.DisplayName, CheckStatus.Ok, version + tool.Path, usedBy);
    }

    private static EnvironmentCheck Missing(ToolLocation tool, string usedBy, string group = GroupTools) =>
        new(group, tool.DisplayName, CheckStatus.Missing, "설치되지 않음 → " + tool.InstallHint, usedBy);

    // ───────────────────────── 시스템 / 에뮬레이터 ─────────────────────────

    private static EnvironmentCheck VirtualizationCheck()
    {
        const string name = "CPU 가상화 (VT-x / AMD-V)";
        const string usedBy = "녹스 · 안드로이드 에뮬레이터";

        if (!X86Base.IsSupported)
            return new EnvironmentCheck(GroupAndroid, name, CheckStatus.Info, "x86/x64 CPU가 아니라 확인할 수 없습니다", usedBy);

        // CPUID(1).ECX bit 31 = 하이퍼바이저 위에서 실행 중
        var hypervisor = X86Base.CpuId(1, 0).Ecx < 0;
        if (hypervisor)
        {
            return new EnvironmentCheck(GroupAndroid, name, CheckStatus.Warning,
                "BIOS의 VT는 켜져 있지만 Hyper-V 계열(WSL2 · 메모리 무결성 · Windows Sandbox 등)이 실행 중입니다. " +
                @"녹스 버전에 따라 VT를 못 쓰거나 느릴 수 있으니 bat\check_android_emulator_env.bat로 자세히 확인하세요", usedBy);
        }

        return OperatingSystem.IsWindows() && IsProcessorFeaturePresent(PF_VIRT_FIRMWARE_ENABLED)
            ? new EnvironmentCheck(GroupAndroid, name, CheckStatus.Ok, "BIOS/UEFI에서 활성화됨", usedBy)
            : new EnvironmentCheck(GroupAndroid, name, CheckStatus.Missing,
                "비활성화됨 → BIOS/UEFI에서 Intel VT-x(Virtualization Technology) 또는 AMD SVM Mode를 켜세요", usedBy);
    }

    private static EnvironmentCheck NoxCheck()
    {
        const string usedBy = "Unity APK 확보 · 실행 검증";
        var bin = ToolLocator.FindNoxBinDir();
        if (bin == null)
            return new EnvironmentCheck(GroupAndroid, "녹스 (NoxPlayer)", CheckStatus.Missing, "설치되지 않음 → https://www.bignox.com 에서 설치", usedBy);

        var exe = Path.Combine(bin, "Nox.exe");
        var version = File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion?.TrimStart('V', 'v', '.') : null;
        var running = Process.GetProcessesByName("Nox").Length > 0 || Process.GetProcessesByName("NoxVMHandle").Length > 0;
        var hasAdb = File.Exists(Path.Combine(bin, "nox_adb.exe"));
        var detail = $"{(version != null ? $"v{version} · " : "")}{(running ? "실행 중" : "실행 안 됨")}" +
                     $"{(hasAdb ? " · nox_adb 있음" : "")} — {bin}";
        return new EnvironmentCheck(GroupAndroid, "녹스 (NoxPlayer)", CheckStatus.Ok, detail, usedBy);
    }

    /// <summary>
    /// PATH의 adb와 녹스의 nox_adb 버전이 다르면 서로 adb 서버를 죽이고 다시 띄워 연결이 끊깁니다.
    /// </summary>
    private static async Task<EnvironmentCheck?> AdbConflictCheckAsync()
    {
        var noxBin = ToolLocator.FindNoxBinDir();
        var noxAdb = noxBin == null ? null : Path.Combine(noxBin, "nox_adb.exe");
        var pathAdb = ToolLocator.FindOnPath("adb");
        if (noxAdb == null || !File.Exists(noxAdb) || pathAdb == null) return null;

        var noxVersion = AdbVersion((await RunAsync(noxAdb, "version")).Output);
        var pathVersion = AdbVersion((await RunAsync(pathAdb, "version")).Output);
        if (noxVersion == pathVersion) return null;

        return new EnvironmentCheck(GroupAndroid, "ADB 버전 충돌 주의", CheckStatus.Warning,
            $"녹스 nox_adb {noxVersion} ↔ PATH adb {pathVersion}. 서로 adb 서버를 재시작해 녹스 연결이 끊기므로 앱과 프롬프트는 nox_adb를 씁니다. " +
            $"터미널에서도 `adb` 대신 nox_adb를 쓰세요 ({noxAdb})",
            "녹스 ADB 연결");
    }

    private static string AdbVersion(string output)
    {
        var m = AdbVersionRegex().Match(output);
        return m.Success ? m.Groups[1].Value : "?";
    }

    // ───────────────────────── REA / 에이전트 ─────────────────────────

    private static EnvironmentCheck ReaSkillCheck()
    {
        var skill = ToolLocator.RepoRoot == null
            ? null
            : Path.Combine(ToolLocator.RepoRoot, ".agents", "skills", "reverse-engineer-anything", "SKILL.md");
        return skill != null && File.Exists(skill)
            ? new EnvironmentCheck(GroupAgent, "REA 워크플로우 스킬", CheckStatus.Ok, Path.GetDirectoryName(skill)!, "에이전트 분석 지침")
            : new EnvironmentCheck(GroupAgent, "REA 워크플로우 스킬", CheckStatus.Missing, @"없음 → bat\setup_rea_workflows.bat 실행", "에이전트 분석 지침");
    }

    private static EnvironmentCheck ReaMcpCheck()
    {
        const string name = "REA MCP 서버 등록";
        const string usedBy = "Claude · Antigravity 분석 도구";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var found = new List<string>();
        if (HasReaServer(Path.Combine(home, ".claude.json"))) found.Add("Claude Code");
        if (HasReaServer(Path.Combine(appData, "Claude", "claude_desktop_config.json"))) found.Add("Claude 앱");

        return found.Count > 0
            ? new EnvironmentCheck(GroupAgent, name, CheckStatus.Ok, "등록됨: " + string.Join(", ", found), usedBy)
            : new EnvironmentCheck(GroupAgent, name, CheckStatus.Warning,
                @"설정 파일에서 찾지 못함 → bat\setup_rea_workflows.bat 실행 (플러그인으로 연결했다면 무시)", usedBy);
    }

    /// <summary>MCP 설정 JSON의 mcpServers(최상위 또는 projects.*)에 "rea" 서버가 있는지 확인</summary>
    private static bool HasReaServer(string configFile)
    {
        if (!File.Exists(configFile)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configFile));
            static bool Has(JsonElement e) =>
                e.ValueKind == JsonValueKind.Object &&
                e.TryGetProperty("mcpServers", out var servers) &&
                servers.ValueKind == JsonValueKind.Object &&
                servers.EnumerateObject().Any(p => p.Name.Equals("rea", StringComparison.OrdinalIgnoreCase));

            var root = doc.RootElement;
            if (Has(root)) return true;
            return root.TryGetProperty("projects", out var projects) &&
                   projects.ValueKind == JsonValueKind.Object &&
                   projects.EnumerateObject().Any(p => Has(p.Value));
        }
        catch
        {
            return false;
        }
    }

    private static EnvironmentCheck AgentAppCheck(AgentApp app)
    {
        var path = app.ExecutablePath;
        return path != null && File.Exists(path)
            ? new EnvironmentCheck(GroupAgent, app.DisplayName, CheckStatus.Ok, path, "프롬프트 자동 전송")
            : new EnvironmentCheck(GroupAgent, app.DisplayName, CheckStatus.Info,
                "기본 경로에서 찾지 못함 — 창이 열려 있으면 전송은 동작합니다", "프롬프트 자동 전송");
    }

    // ───────────────────────── 대상 게임 ─────────────────────────

    public static IEnumerable<EnvironmentCheck> GameChecks(DetectionResult d)
    {
        if (!TargetCatalog.Get(d.Kind).IsUnity) yield break;

        var facts = new List<string>();
        if (d.UnityVersion != null) facts.Add($"Unity {d.UnityVersion}");
        if (d.Backend != null) facts.Add(d.Backend);
        if (d.MetadataVersion != null) facts.Add($"metadata v{d.MetadataVersion}");
        if (d.Abis.Count > 0) facts.Add(string.Join("/", d.Abis));
        yield return new EnvironmentCheck(GroupGame, "Unity 빌드 정보", CheckStatus.Info,
            facts.Count > 0 ? string.Join(" · ", facts) : "버전 정보를 찾지 못함", Path.GetFileName(d.FilePath));

        // 모드 로더는 PC 게임 폴더에만 설치되므로 APK/IPA에서는 건너뜀
        if (d.Kind is not (TargetKind.UnityIl2Cpp or TargetKind.UnityMono) || d.GameRoot == null) yield break;

        if (d.ModLoaders.Count == 0)
        {
            yield return new EnvironmentCheck(GroupGame, "모드 로더", CheckStatus.Info,
                $"BepInEx / MelonLoader 미설치 — 모드 실행 검증 단계에서 필요합니다 ({d.GameRoot})", "모드 실행 검증");
            yield break;
        }

        foreach (var loader in d.ModLoaders)
            yield return new EnvironmentCheck(GroupGame, "모드 로더", CheckStatus.Ok, loader, "모드 실행 검증");
    }

    // ───────────────────────── 프로세스 실행 ─────────────────────────

    private static async Task<(bool Ok, string Output)> RunAsync(string path, string args, int timeoutMs = 15000)
    {
        var isScript = path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                       path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        var psi = new ProcessStartInfo
        {
            FileName = isScript ? "cmd.exe" : path,
            Arguments = isScript ? $"/d /s /c \"\"{path}\" {args}\"" : args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return (false, "");
            proc.StandardInput.Close();

            using var cts = new CancellationTokenSource(timeoutMs);
            var stdout = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = proc.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return (false, "응답 없음 (시간 초과)");
            }

            var output = (await stdout) + "\n" + (await stderr);
            return (proc.ExitCode == 0, output.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string Shorten(string s) => s.Length > 80 ? s[..80] + "…" : s;
}
