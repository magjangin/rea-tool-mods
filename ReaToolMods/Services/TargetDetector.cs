using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using ReaToolMods.Models;

namespace ReaToolMods.Services;

/// <summary>
/// 확장자만이 아니라 파일 시그니처, 주변 폴더 구조, 압축 엔트리를 보고 타겟 종류를 판별합니다.
/// (예: .exe 옆에 GameAssembly.dll이 있으면 Unity IL2CPP, .apk 안에 libil2cpp.so가 있으면 Unity APK)
/// </summary>
public static partial class TargetDetector
{
    private const uint Il2CppMetadataMagic = 0xFAB11BAF;

    private enum Signature { Unknown, Pe, Zip, Il2CppMetadata, Elf, MachO, Asar }

    [GeneratedRegex(@"(?<![\d.])(\d{1,4}\.\d{1,2}\.\d{1,3}[abfpx]\d{1,3})")]
    private static partial Regex UnityVersionRegex();

    public static DetectionResult Detect(string path)
    {
        var r = new DetectionResult { FilePath = path };
        try
        {
            if (Directory.Exists(path))
            {
                DetectDirectory(path, r);
                return r;
            }

            if (!File.Exists(path))
            {
                r.Warnings.Add("파일을 찾을 수 없습니다.");
                return r;
            }

            if (AppExecutionAlias.TryResolve(path, out var aliasTarget))
            {
                // WindowsApps\*.exe는 0바이트 별칭이라 열 수 없음 → 실제 exe를 판별 (FilePath도 실제 경로로 바뀜)
                if (aliasTarget != null && File.Exists(aliasTarget))
                {
                    var real = Detect(aliasTarget);
                    real.Evidence.Insert(0, "앱 실행 별칭 → 실제 exe로 대체");
                    return real;
                }

                r.Warnings.Add("Microsoft Store 앱 실행 별칭(0바이트 바로가기)이라 직접 열 수 없습니다 — " +
                               (aliasTarget != null ? $"실제 파일: {aliasTarget}" : @"C:\Program Files\WindowsApps 아래의 실제 exe를 선택하세요."));
                return r;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!CheckSignature(path, ext, r) && ext is ".apk" or ".xapk" or ".apks" or ".aab" or ".ipa" or ".zip")
            {
                // ZIP이 아닌 파일은 내부를 열 수 없으므로 확장자 기준으로만 분류
                r.Kind = ext switch { ".ipa" => TargetKind.UnityApp, ".zip" => TargetKind.App, _ => TargetKind.Apk };
                return r;
            }

            switch (ext)
            {
                case ".apk" or ".xapk" or ".apks" or ".aab":
                    DetectAndroidPackage(path, r);
                    break;
                case ".ipa":
                    DetectApplePackage(path, r);
                    break;
                case ".zip":
                    DetectZip(path, r);
                    break;
                case ".asar":
                    r.Kind = TargetKind.App;
                    r.AsarPath = path;
                    r.Evidence.Add("Electron ASAR 아카이브");
                    break;
                case ".dat":
                    DetectMetadataFile(path, r);
                    break;
                case ".dll":
                    DetectDll(path, r);
                    break;
                case ".exe":
                    DetectExe(path, r);
                    break;
                default:
                    r.Evidence.Add($"알 수 없는 확장자({ext}) — 네이티브 바이너리로 간주");
                    break;
            }
        }
        catch (InvalidDataException)
        {
            r.Warnings.Add("압축 구조를 읽을 수 없습니다 — 손상되었거나 일부만 받은 파일일 수 있습니다.");
        }
        catch (Exception ex)
        {
            r.Warnings.Add($"판별 중 오류: {ex.Message}");
        }

        return r;
    }

    /// <summary>
    /// 사용자가 고른 타겟 버튼과 실제 파일이 맞는지 검사해 경고 목록을 돌려줍니다.
    /// </summary>
    public static List<string> Validate(TargetProfile profile, string path, DetectionResult? detection)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(path)) return warnings;

        var ext = Directory.Exists(path) && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            ? ".app"
            : Path.GetExtension(path).ToLowerInvariant();

        if (!profile.AcceptsExtension(ext))
        {
            warnings.Add($"[{profile.ShortName}] 타겟에는 '{(ext.Length == 0 ? "(확장자 없음)" : ext)}' 파일이 맞지 않습니다. " +
                         $"(허용: {string.Join(", ", profile.Extensions)})");
        }

        if (detection != null && string.Equals(detection.FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            if (detection.Kind != profile.Kind)
            {
                warnings.Add($"자동 판별 결과는 [{TargetCatalog.Get(detection.Kind).ShortName}]입니다 — 의도한 선택인지 확인하세요.");
            }

            warnings.AddRange(detection.Warnings);
        }

        return warnings;
    }

    /// <summary>
    /// 다른 프로세스(게임 실행 파일, 에디터 등)가 파일을 열고 있어도 공유 잠금 충돌 없이 안전하게 읽습니다.
    /// </summary>
    public static FileStream SafeOpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    // ───────────────────────── 파일 시그니처 ─────────────────────────

    private static Signature ReadSignature(string path)
    {
        Span<byte> head = stackalloc byte[16];
        using var fs = SafeOpenRead(path);
        var n = fs.ReadAtLeast(head, 8, throwOnEndOfStream: false);
        if (n < 4) return Signature.Unknown;

        if (head[0] == 'M' && head[1] == 'Z') return Signature.Pe;
        if (head[0] == 'P' && head[1] == 'K' && (head[2] == 3 || head[2] == 5)) return Signature.Zip;
        if (BitConverter.ToUInt32(head) == Il2CppMetadataMagic) return Signature.Il2CppMetadata;
        if (head[0] == 0x7F && head[1] == 'E' && head[2] == 'L' && head[3] == 'F') return Signature.Elf;

        var be = (uint)(head[0] << 24 | head[1] << 16 | head[2] << 8 | head[3]);
        if (be is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE or 0xCAFEBABE) return Signature.MachO;
        if (BitConverter.ToUInt32(head) == 4) return Signature.Asar;

        return Signature.Unknown;
    }

    /// <summary>확장자에 맞는 파일 시그니처인지 검사. 불일치면 경고를 남기고 false.</summary>
    private static bool CheckSignature(string path, string ext, DetectionResult r)
    {
        Signature? expected = ext switch
        {
            ".exe" or ".dll" => Signature.Pe,
            ".apk" or ".xapk" or ".apks" or ".aab" or ".ipa" or ".zip" => Signature.Zip,
            ".asar" => Signature.Asar,
            ".dat" => Signature.Il2CppMetadata,
            ".so" => Signature.Elf,
            ".dylib" => Signature.MachO,
            _ => null
        };
        if (expected == null) return true;

        var actual = ReadSignature(path);
        if (actual == expected) return true;

        if (expected == Signature.Il2CppMetadata)
        {
            // 판별 단계(MetadataEvidence)에서 비표준 헤더 경고를 따로 남김
            return false;
        }

        r.Warnings.Add($"확장자는 {ext}인데 파일 시그니처는 {Describe(actual)}입니다 — 확장자가 잘못되었거나 손상된 파일일 수 있습니다.");
        return false;
    }

    private static string Describe(Signature s) => s switch
    {
        Signature.Pe => "PE(EXE/DLL)",
        Signature.Zip => "ZIP",
        Signature.Il2CppMetadata => "IL2CPP 메타데이터",
        Signature.Elf => "ELF",
        Signature.MachO => "Mach-O",
        Signature.Asar => "ASAR",
        _ => "알 수 없음"
    };

    // ───────────────────────── Windows (EXE / DLL / 폴더) ─────────────────────────

    private static void DetectExe(string path, DetectionResult r)
    {
        var dir = Path.GetDirectoryName(path)!;
        var dataDir = FindUnityDataDir(dir, Path.GetFileNameWithoutExtension(path));

        if (dataDir != null && InspectUnityWindows(dir, dataDir, r))
        {
            InspectSteamAndProtections(dir, r);
            return;
        }

        if (InspectUnrealWindows(dir, path, r))
        {
            InspectSteamAndProtections(dir, r);
            return;
        }

        if (InspectGodotWindows(dir, path, r))
        {
            InspectSteamAndProtections(dir, r);
            return;
        }

        var asar = Path.Combine(dir, "resources", "app.asar");
        if (File.Exists(asar))
        {
            r.Kind = TargetKind.App;
            r.Engine = "Electron";
            r.AsarPath = asar;
            r.Evidence.Add(@"resources\app.asar 발견 — Electron 앱");
            InspectSteamAndProtections(dir, r);
            return;
        }

        var (managed, machine) = InspectPe(path);
        if (managed)
        {
            r.Kind = TargetKind.Dll;
            r.Evidence.Add($".NET 관리 어셈블리(EXE, {machine}) — ILSpy로 C# 복원 가능");
        }
        else
        {
            r.Kind = TargetKind.Exe;
            r.Evidence.Add($"네이티브 PE 실행 파일 ({machine})");
        }

        InspectSteamAndProtections(dir, r);
    }

    private static void DetectDll(string path, DetectionResult r)
    {
        var dir = Path.GetDirectoryName(path)!;
        var fileName = Path.GetFileName(path);

        if (fileName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
        {
            var dataDir = FindUnityDataDir(dir, null);
            if (dataDir == null || !InspectUnityWindows(dir, dataDir, r))
            {
                r.Kind = TargetKind.UnityIl2Cpp;
                r.Backend = "IL2CPP";
                r.GameRoot = dir;
                r.GameAssemblyPath = path;
                r.Evidence.Add("GameAssembly.dll (IL2CPP 네이티브 코드)");
                r.Warnings.Add("같은 폴더에서 *_Data\\il2cpp_data를 찾지 못했습니다 — global-metadata.dat 위치를 직접 확인하세요.");
            }
            return;
        }

        var (managed, machine) = InspectPe(path);
        if (!managed)
        {
            r.Kind = TargetKind.Exe;
            r.Evidence.Add($"네이티브 DLL ({machine}) — 네이티브 분석(REA)으로 처리");
            return;
        }

        var parent = Directory.GetParent(dir);
        if (Path.GetFileName(dir).Equals("Managed", StringComparison.OrdinalIgnoreCase) &&
            parent != null && parent.Name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) &&
            parent.Parent != null)
        {
            if (InspectUnityWindows(parent.Parent.FullName, parent.FullName, r)) return;
        }

        var interopRoot = FindAncestorWith(dir, "BepInEx", "interop") ?? FindAncestorWith(dir, "MelonLoader", "Il2CppAssemblies");
        if (interopRoot != null)
        {
            r.Kind = TargetKind.UnityIl2Cpp;
            r.Backend = "IL2CPP";
            r.GameRoot = interopRoot;
            r.Evidence.Add("모드 로더가 생성한 IL2CPP interop 어셈블리");
            var dataDir = FindUnityDataDir(interopRoot, null);
            if (dataDir != null) InspectUnityWindows(interopRoot, dataDir, r);
            return;
        }

        if (fileName.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
        {
            r.Kind = TargetKind.UnityMono;
            r.Backend = "Mono";
            r.ManagedDir = dir;
            r.Evidence.Add("Unity 스크립트 어셈블리(Assembly-CSharp) — 게임 폴더 밖에 복사된 파일");
            return;
        }

        r.Kind = TargetKind.Dll;
        r.Evidence.Add($".NET 관리 어셈블리 ({machine})");
    }

    private static void DetectMetadataFile(string path, DetectionResult r)
    {
        // ...\<Game>_Data\il2cpp_data\Metadata\global-metadata.dat → 게임 루트를 역추적해 폴더 전체를 검사
        var dataDir = Directory.GetParent(path)?.Parent?.Parent;
        if (dataDir != null && dataDir.Name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) && dataDir.Parent != null &&
            InspectUnityWindows(dataDir.Parent.FullName, dataDir.FullName, r))
        {
            return;
        }

        r.Kind = TargetKind.UnityIl2Cpp;
        r.Backend = "IL2CPP";
        r.MetadataPath = path;
        r.MetadataVersion = ReadMetadataVersion(SafeOpenRead(path));
        r.Evidence.Add(MetadataEvidence(r.MetadataVersion));
        if (r.MetadataVersion == null)
            r.Warnings.Add("global-metadata.dat의 시그니처가 표준(0xFAB11BAF)이 아닙니다 — 암호화/보호된 메타데이터일 수 있습니다.");

        if (path.Replace('/', '\\').Contains(@"assets\bin\Data\", StringComparison.OrdinalIgnoreCase))
        {
            r.Kind = TargetKind.UnityApk;
            r.Evidence.Add(@"APK에서 추출된 경로 (assets\bin\Data)");
        }
    }

    private static void DetectDirectory(string path, DetectionResult r)
    {
        if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            r.Kind = TargetKind.UnityApp;
            r.GameRoot = path;
            var files = Directory.EnumerateFiles(path, "*",
                    new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 8 })
                .Take(50_000)
                .ToList();
            string? Find(string suffix) => files.FirstOrDefault(f =>
                f.Replace('\\', '/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

            if (Find("metadata/global-metadata.dat") is { } meta)
            {
                r.MetadataPath = meta;
                r.MetadataVersion = ReadMetadataVersion(SafeOpenRead(meta));
            }
            r.GameAssemblyPath = Find("/GameAssembly.dylib");
            r.ManagedDir = Find("/Managed/Assembly-CSharp.dll") is { } asm ? Path.GetDirectoryName(asm) : null;
            if (Find("/Data/globalgamemanagers") is { } ggm) r.UnityVersion = ReadUnityVersion(SafeOpenRead(ggm));

            var names = files.Select(f => Path.GetRelativePath(path, f).Replace('\\', '/')).ToList();
            if (!ApplyUnityMarkers(names, r, TargetKind.UnityApp))
                r.Warnings.Add("Unity 흔적이 없습니다 — 일반 macOS 앱일 수 있습니다.");
            return;
        }

        var exe = Directory.EnumerateFiles(path, "*.exe")
            .FirstOrDefault(e => Directory.Exists(Path.Combine(path, Path.GetFileNameWithoutExtension(e) + "_Data")));
        if (exe != null)
        {
            r.Evidence.Add($"폴더 안의 게임 실행 파일: {Path.GetFileName(exe)}");
            DetectExe(exe, r);
            return;
        }

        if (File.Exists(Path.Combine(path, "resources", "app.asar")))
        {
            r.Kind = TargetKind.App;
            r.AsarPath = Path.Combine(path, "resources", "app.asar");
            r.Evidence.Add(@"폴더 안에 resources\app.asar — Electron 앱");
            return;
        }

        r.Warnings.Add("폴더가 선택되었습니다 — 분석할 파일을 직접 선택하면 더 정확하게 판별합니다.");
    }

    /// <summary>
    /// Windows용 Unity 빌드 구조 검사. Unity로 확인되면 true.
    /// </summary>
    private static bool InspectUnityWindows(string root, string dataDir, DetectionResult r)
    {
        var gameAssembly = Path.Combine(root, "GameAssembly.dll");
        var metadata = Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat");
        var managed = Path.Combine(dataDir, "Managed");
        var hasUnityPlayer = File.Exists(Path.Combine(root, "UnityPlayer.dll"));
        var hasGgm = File.Exists(Path.Combine(dataDir, "globalgamemanagers")) ||
                     File.Exists(Path.Combine(dataDir, "data.unity3d"));

        if (File.Exists(gameAssembly) || File.Exists(metadata))
        {
            r.Kind = TargetKind.UnityIl2Cpp;
            r.Backend = "IL2CPP";
            if (File.Exists(gameAssembly))
            {
                r.GameAssemblyPath = gameAssembly;
                r.Evidence.Add("GameAssembly.dll");
            }

            if (File.Exists(metadata))
            {
                r.MetadataPath = metadata;
                r.MetadataVersion = ReadMetadataVersion(SafeOpenRead(metadata));
                r.Evidence.Add(MetadataEvidence(r.MetadataVersion));
                if (r.MetadataVersion == null)
                    r.Warnings.Add("global-metadata.dat의 시그니처가 표준이 아닙니다 — 암호화/보호된 메타데이터일 수 있습니다.");
            }
            else
            {
                r.Warnings.Add(@"il2cpp_data\Metadata\global-metadata.dat가 없습니다 — 위치가 바뀌었거나 보호된 빌드일 수 있습니다.");
            }
        }
        else if (File.Exists(Path.Combine(managed, "Assembly-CSharp.dll")))
        {
            r.Kind = TargetKind.UnityMono;
            r.Backend = "Mono";
            r.ManagedDir = managed;
            r.Evidence.Add(@"Managed\Assembly-CSharp.dll");
            if (Directory.Exists(Path.Combine(root, "MonoBleedingEdge")))
                r.Evidence.Add("MonoBleedingEdge 런타임");
        }
        else if (hasUnityPlayer || hasGgm)
        {
            r.Kind = Directory.Exists(managed) ? TargetKind.UnityMono : TargetKind.UnityIl2Cpp;
            r.Warnings.Add("Unity 빌드지만 스크립팅 백엔드(IL2CPP/Mono)를 확정하지 못했습니다.");
        }
        else
        {
            return false;
        }

        if (hasUnityPlayer) r.Evidence.Add("UnityPlayer.dll");
        r.GameRoot = root;
        r.DataDir = dataDir;
        r.UnityVersion = DetectUnityVersionWindows(root, dataDir);
        DetectModLoaders(root, r);
        return true;
    }

    private static string? FindUnityDataDir(string root, string? exeName)
    {
        if (exeName != null)
        {
            var exact = Path.Combine(root, exeName + "_Data");
            if (Directory.Exists(exact)) return exact;
        }

        try
        {
            return Directory.EnumerateDirectories(root, "*_Data").FirstOrDefault(d =>
                Directory.Exists(Path.Combine(d, "Managed")) ||
                Directory.Exists(Path.Combine(d, "il2cpp_data")) ||
                File.Exists(Path.Combine(d, "globalgamemanagers")) ||
                File.Exists(Path.Combine(d, "data.unity3d")));
        }
        catch
        {
            return null;
        }
    }

    private static string? DetectUnityVersionWindows(string root, string dataDir)
    {
        var player = Path.Combine(root, "UnityPlayer.dll");
        if (File.Exists(player))
        {
            var info = FileVersionInfo.GetVersionInfo(player);
            var m = UnityVersionRegex().Match(info.ProductVersion ?? info.FileVersion ?? "");
            if (m.Success) return m.Groups[1].Value;
        }

        // exe 자체 버전 리소스에서도 검사
        try
        {
            var exe = Directory.EnumerateFiles(root, "*.exe").FirstOrDefault();
            if (exe != null)
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                var m = UnityVersionRegex().Match(info.ProductVersion ?? info.FileVersion ?? "");
                if (m.Success) return m.Groups[1].Value;
            }
        }
        catch { }

        foreach (var name in new[] { "globalgamemanagers", "data.unity3d", "mainData" })
        {
            var file = Path.Combine(dataDir, name);
            if (File.Exists(file)) return ReadUnityVersion(SafeOpenRead(file));
        }

        return null;
    }

    /// <summary>
    /// Unreal Engine 4/5 게임 구조(Engine/, Binaries/Win64, Content/Paks, UE4SS)를 검사합니다.
    /// </summary>
    private static bool InspectUnrealWindows(string dir, string exePath, DetectionResult r)
    {
        // 1) 대상 exe가 Shipping.exe(예: <Game>/Binaries/Win64/<Game>-Win64-Shipping.exe)인 경우
        //    또는 루트의 런처 exe인 경우 모두 지원하도록 루트와 바이너리 디렉터리 판별
        string? root = null;
        string? paksDir = null;

        if (Directory.Exists(Path.Combine(dir, "Engine")) && Directory.EnumerateDirectories(dir).Any(d => Directory.Exists(Path.Combine(d, "Content", "Paks"))))
        {
            root = dir;
        }
        else
        {
            // 상위 디렉터리 역추적 (<Game>/Binaries/Win64 구조 고려해 최대 5단계까지 역추적)
            var cur = new DirectoryInfo(dir);
            for (int i = 0; i < 5 && cur != null; i++)
            {
                if (Directory.Exists(Path.Combine(cur.FullName, "Engine")))
                {
                    root = cur.FullName;
                    break;
                }
                cur = cur.Parent;
            }
        }

        if (root == null)
        {
            // Engine 폴더가 없더라도 Content\Paks 폴더나 *.pak 파일이 있는지 확인
            var contentDir = Path.Combine(dir, "Content", "Paks");
            if (Directory.Exists(contentDir) || Directory.EnumerateFiles(dir, "*.pak").Any())
            {
                root = dir;
                paksDir = Directory.Exists(contentDir) ? contentDir : dir;
            }
            else
            {
                // exe FileVersionInfo에 Unreal/Epic 흔적이 있는지 검사
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(exePath);
                    var desc = $"{info.FileDescription} {info.CompanyName} {info.ProductName}";
                    if (desc.Contains("Unreal", StringComparison.OrdinalIgnoreCase) || desc.Contains("Epic Games", StringComparison.OrdinalIgnoreCase))
                    {
                        root = dir;
                    }
                }
                catch { }
            }
        }

        if (root == null) return false;

        r.Kind = TargetKind.Exe;
        r.Engine = "UnrealEngine";
        r.GameRoot = root;

        // UE4 vs UE5 및 패키징 형식 판별 (.utoc/.ucas = IoStore ZenLoader, UE5 가능성 높음)
        var hasIoStore = false;
        try
        {
            paksDir ??= Directory.EnumerateDirectories(root, "Paks", SearchOption.AllDirectories).FirstOrDefault();
            if (paksDir != null && Directory.Exists(paksDir))
            {
                hasIoStore = Directory.EnumerateFiles(paksDir, "*.utoc").Any() || Directory.EnumerateFiles(paksDir, "*.ucas").Any();
            }
        }
        catch { }

        r.UnrealVersion = hasIoStore ? "UE5" : "UE4/UE5";
        r.Evidence.Add($"Unreal Engine ({r.UnrealVersion}) 프로젝트");
        if (paksDir != null) r.Evidence.Add($"Content Paks: {Path.GetRelativePath(root, paksDir)}");

        // UE4SS 모드 로더 및 프록시 DLL 감지
        DetectUnrealModLoaders(root, dir, r);
        return true;
    }

    private static void DetectUnrealModLoaders(string root, string binDir, DetectionResult r)
    {
        var dirsToCheck = new[] { binDir, root };
        foreach (var d in dirsToCheck.Distinct())
        {
            var ue4ssDll = Path.Combine(d, "ue4ss.dll");
            var ue4ssIni = Path.Combine(d, "UE4SS-settings.ini");
            var dwmapi = Path.Combine(d, "dwmapi.dll");
            var xinput = Path.Combine(d, "xinput1_3.dll");
            var modsDir = Path.Combine(d, "Mods");

            if (File.Exists(ue4ssDll) || File.Exists(ue4ssIni))
            {
                var proxy = File.Exists(dwmapi) ? "dwmapi.dll" : (File.Exists(xinput) ? "xinput1_3.dll" : "직접 주입");
                var modCount = Directory.Exists(modsDir) ? Directory.EnumerateDirectories(modsDir).Count() : 0;
                r.ModLoaders.Add($"UE4SS (프록시: {proxy}, 모드 {modCount}개)");
                break;
            }
        }
    }

    /// <summary>
    /// Godot 엔진 프로젝트(루트 또는 하위 폴더의 *.pck 파일)를 검사합니다.
    /// </summary>
    private static bool InspectGodotWindows(string dir, string exePath, DetectionResult r)
    {
        try
        {
            var pck = Directory.EnumerateFiles(dir, "*.pck").FirstOrDefault();
            if (pck != null)
            {
                r.Kind = TargetKind.Exe;
                r.Engine = "Godot";
                r.GameRoot = dir;
                r.Evidence.Add($"Godot 엔진 게임 ({Path.GetFileName(pck)} 발견) — GDRE 도구 또는 gdsdecomp로 스크립트 복원 가능");
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Steam API 런타임 및 안티치트(Easy Anti-Cheat, BattlEye) 솔루션을 검사합니다.
    /// </summary>
    private static void InspectSteamAndProtections(string dir, DetectionResult r)
    {
        try
        {
            // Steam API DLL 검사
            var hasSteamApi = File.Exists(Path.Combine(dir, "steam_api64.dll")) ||
                              File.Exists(Path.Combine(dir, "steam_api.dll"));
            if (hasSteamApi)
            {
                r.HasSteamApi = true;
                r.Evidence.Add("Steam API 런타임 (steam_api*.dll) — Steam DRM/DLC 연동");

                var appidFile = Path.Combine(dir, "steam_appid.txt");
                if (File.Exists(appidFile))
                {
                    var txt = File.ReadAllText(appidFile).Trim();
                    if (uint.TryParse(txt, out _))
                    {
                        r.SteamAppId = txt;
                        r.Evidence.Add($"Steam AppID: {txt}");
                    }
                }
            }

            // 안티치트 검사
            if (Directory.Exists(Path.Combine(dir, "EasyAntiCheat")) ||
                File.Exists(Path.Combine(dir, "EasyAntiCheat_EOS_Setup.exe")) ||
                Directory.EnumerateFiles(dir, "*EasyAntiCheat*.dll").Any())
            {
                r.AntiCheat = "EasyAntiCheat";
                r.Warnings.Add("Easy Anti-Cheat(EAC) 감지 — 단절 환경/오프라인 모딩 시 안티치트 비활성화 또는 바이패스 필요");
            }
            else if (Directory.Exists(Path.Combine(dir, "BattlEye")) ||
                     File.Exists(Path.Combine(dir, "BEService.exe")) ||
                     Directory.EnumerateFiles(dir, "*BattlEye*.dll").Any())
            {
                r.AntiCheat = "BattlEye";
                r.Warnings.Add("BattlEye 감지 — 오프라인 모드 실행 시 안티치트 우회 필요");
            }
        }
        catch { }
    }

    /// <summary>
    /// 게임 폴더에 설치된 모드 로더(BepInEx, MelonLoader, Doorstop 프록시)를 찾습니다.
    /// </summary>
    private static void DetectModLoaders(string root, DetectionResult r)
    {
        var bepCore = Path.Combine(root, "BepInEx", "core");
        var bepDll = new[] { "BepInEx.Core.dll", "BepInEx.dll" }
            .Select(n => Path.Combine(bepCore, n))
            .FirstOrDefault(File.Exists);
        if (bepDll != null)
        {
            var plugins = CountDlls(Path.Combine(root, "BepInEx", "plugins"));
            var interop = Directory.Exists(Path.Combine(root, "BepInEx", "interop")) ? ", interop 생성됨" : "";
            r.ModLoaders.Add($"BepInEx {FileVersion(bepDll)} (플러그인 {plugins}개{interop})");
        }

        var melonDll = new[] { Path.Combine(root, "MelonLoader", "net6", "MelonLoader.dll"), Path.Combine(root, "MelonLoader", "MelonLoader.dll") }
            .FirstOrDefault(File.Exists);
        if (melonDll != null)
        {
            var mods = CountDlls(Path.Combine(root, "Mods"));
            var interop = Directory.Exists(Path.Combine(root, "MelonLoader", "Il2CppAssemblies")) ? ", Il2CppAssemblies 생성됨" : "";
            r.ModLoaders.Add($"MelonLoader {FileVersion(melonDll)} (모드 {mods}개{interop})");
        }

        if (bepDll == null && File.Exists(Path.Combine(root, "doorstop_config.ini")))
            r.ModLoaders.Add("Unity Doorstop 프록시 (doorstop_config.ini)");
    }

    private static int CountDlls(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories).Count()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string FileVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        var v = info.ProductVersion ?? info.FileVersion ?? "";
        var plus = v.IndexOf('+');
        return plus > 0 ? v[..plus] : v;
    }

    private static (bool Managed, string Machine) InspectPe(string path)
    {
        try
        {
            using var fs = SafeOpenRead(path);
            using var pe = new PEReader(fs);
            var headers = pe.PEHeaders;
            var anyCpu = pe.HasMetadata && headers.CorHeader is { } cor &&
                         (cor.Flags & CorFlags.ILOnly) != 0 && (cor.Flags & CorFlags.Requires32Bit) == 0;
            var machine = headers.CoffHeader.Machine switch
            {
                Machine.I386 when anyCpu => "AnyCPU",
                Machine.Amd64 => "x64",
                Machine.I386 => "x86",
                Machine.Arm64 => "ARM64",
                var m => m.ToString()
            };
            return (pe.HasMetadata, machine);
        }
        catch
        {
            return (false, "PE 아님");
        }
    }

    private static string? FindAncestorWith(string dir, string folder, string child)
    {
        var current = new DirectoryInfo(dir);
        while (current != null)
        {
            if (current.Name.Equals(child, StringComparison.OrdinalIgnoreCase) &&
                current.Parent?.Name.Equals(folder, StringComparison.OrdinalIgnoreCase) == true)
            {
                return current.Parent.Parent?.FullName;
            }
            current = current.Parent;
        }

        return null;
    }

    // ───────────────────────── 압축 패키지 (APK / IPA / ZIP) ─────────────────────────

    private static void DetectAndroidPackage(string path, DetectionResult r)
    {
        r.Kind = TargetKind.Apk;
        var names = CollectZipEntries(path, r);

        foreach (var name in names)
        {
            // lib/<abi>/x.so, base/lib/<abi>/x.so (AAB), split 이름 config.arm64_v8a.apk
            var parts = name.Split('/');
            var libIndex = Array.FindIndex(parts, p => p == "lib");
            if (libIndex >= 0 && libIndex + 2 < parts.Length && parts[^1].EndsWith(".so"))
                AddAbi(r, parts[libIndex + 1]);
            if (parts[^1].StartsWith("config.") && name.EndsWith(".apk"))
            {
                var split = parts[^1]["config.".Length..^".apk".Length];
                AddAbi(r, split == "x86_64" ? split : split.Replace('_', '-'));
            }
        }

        if (ApplyUnityMarkers(names, r, TargetKind.UnityApk))
        {
            r.Evidence.Add("Unity 안드로이드 빌드");
        }
        else
        {
            r.Evidence.Add("일반 안드로이드 패키지 (Unity 흔적 없음)");
        }

        if (r.Abis.Count > 0) r.Evidence.Add("ABI: " + string.Join(", ", r.Abis));
    }

    private static void AddAbi(DetectionResult r, string abi)
    {
        string[] known = ["arm64-v8a", "armeabi-v7a", "armeabi", "x86", "x86_64"];
        if (known.Contains(abi) && !r.Abis.Contains(abi)) r.Abis.Add(abi);
    }

    private static void DetectApplePackage(string path, DetectionResult r)
    {
        r.Kind = TargetKind.UnityApp;
        var names = CollectZipEntries(path, r);
        if (!ApplyUnityMarkers(names, r, TargetKind.UnityApp))
            r.Warnings.Add("Unity 흔적이 없습니다 — 일반 iOS 앱일 수 있습니다.");
    }

    private static void DetectZip(string path, DetectionResult r)
    {
        var names = CollectZipEntries(path, r);

        var asar = names.FirstOrDefault(n => n.EndsWith(".asar"));
        if (asar != null)
        {
            r.Kind = TargetKind.App;
            r.Evidence.Add($"ZIP 안에 {asar} — Electron 앱 패키지");
            return;
        }

        if (names.Any(n => n.StartsWith("payload/") || n.Contains(".app/contents/")))
        {
            r.Kind = TargetKind.UnityApp;
            if (!ApplyUnityMarkers(names, r, TargetKind.UnityApp))
                r.Warnings.Add("Apple 앱 번들이지만 Unity 흔적이 없습니다.");
            return;
        }

        if (ApplyUnityMarkers(names, r, TargetKind.UnityIl2Cpp))
        {
            if (r.Backend == "Mono") r.Kind = TargetKind.UnityMono;
            r.Warnings.Add("압축된 Unity PC 게임입니다 — 압축을 푼 뒤 게임 exe를 다시 선택하면 경로까지 자동으로 채워집니다.");
            return;
        }

        r.Kind = TargetKind.App;
        r.Evidence.Add("ZIP 패키지 — 앱 패키지로 간주");
    }

    /// <summary>
    /// ZIP 엔트리 이름을 소문자/슬래시로 정규화해 수집합니다.
    /// XAPK/APKS처럼 안에 APK가 들어 있는 분할 패키지는 한 단계 더 열어 봅니다.
    /// </summary>
    private static List<string> CollectZipEntries(string path, DetectionResult r)
    {
        var names = new List<string>();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        using var zip = ZipFile.OpenRead(path);
        CollectZipEntries(zip, "", names, r, expandNestedApks: ext is ".xapk" or ".apks");
        return names;
    }

    private static void CollectZipEntries(ZipArchive zip, string prefix, List<string> names, DetectionResult r, bool expandNestedApks)
    {
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/').ToLowerInvariant();
            names.Add(prefix + name);

            if (name.EndsWith("global-metadata.dat") && r.MetadataVersion == null)
            {
                r.MetadataPath ??= prefix + entry.FullName;
                using var s = entry.Open();
                r.MetadataVersion = ReadMetadataVersion(s);
            }
            else if ((name.EndsWith("data/globalgamemanagers") || name.EndsWith("data/data.unity3d")) && r.UnityVersion == null)
            {
                using var s = entry.Open();
                r.UnityVersion = ReadUnityVersion(s);
            }
            else if (expandNestedApks && name.EndsWith(".apk"))
            {
                r.IsSplitPackage = true;
                var temp = Path.Combine(Path.GetTempPath(), $"reatool-{Guid.NewGuid():N}.apk");
                try
                {
                    entry.ExtractToFile(temp);
                    using var inner = ZipFile.OpenRead(temp);
                    CollectZipEntries(inner, name + "!/", names, r, expandNestedApks: false);
                }
                catch (InvalidDataException)
                {
                    r.Warnings.Add($"내부 APK를 열 수 없습니다: {entry.FullName}");
                }
                finally
                {
                    try { File.Delete(temp); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// 경로 목록에서 Unity 흔적을 찾아 r에 기록합니다. Unity면 true.
    /// </summary>
    private static bool ApplyUnityMarkers(IReadOnlyCollection<string> names, DetectionResult r, TargetKind unityKind)
    {
        bool Any(Func<string, bool> predicate) => names.Any(n => predicate(n.ToLowerInvariant()));

        var il2cppLib = Any(n => n.EndsWith("/libil2cpp.so") || n.EndsWith("gameassembly.dll") ||
                                 n.EndsWith("gameassembly.dylib") || n.EndsWith("unityframework.framework/unityframework"));
        var metadata = Any(n => n.EndsWith("metadata/global-metadata.dat"));
        var monoAssembly = Any(n => n.EndsWith("managed/assembly-csharp.dll"));
        var unityRuntime = Any(n => n.EndsWith("/libunity.so") || n.EndsWith("unityplayer.dll") ||
                                    n.EndsWith("unityplayer.dylib") || n.EndsWith("data/globalgamemanagers") ||
                                    n.EndsWith("data/data.unity3d"));

        if (!il2cppLib && !metadata && !monoAssembly && !unityRuntime) return false;

        r.Kind = unityKind;
        if (il2cppLib || metadata)
        {
            r.Backend = "IL2CPP";
            if (il2cppLib) r.Evidence.Add("IL2CPP 네이티브 라이브러리 (libil2cpp / GameAssembly / UnityFramework)");
            if (metadata) r.Evidence.Add(MetadataEvidence(r.MetadataVersion));
            if (metadata && r.MetadataVersion == null)
                r.Warnings.Add("global-metadata.dat의 시그니처가 표준이 아닙니다 — 암호화/보호된 메타데이터일 수 있습니다.");
        }
        else if (monoAssembly)
        {
            r.Backend = "Mono";
            r.Evidence.Add("Managed/Assembly-CSharp.dll (Mono)");
        }
        else
        {
            r.Warnings.Add("Unity 빌드지만 스크립팅 백엔드(IL2CPP/Mono)를 확정하지 못했습니다.");
        }

        return true;
    }

    // ───────────────────────── 헤더 파싱 ─────────────────────────

    /// <summary>global-metadata.dat 헤더(매직 + 버전) 파싱. 표준 시그니처가 아니면 null.</summary>
    private static int? ReadMetadataVersion(Stream stream)
    {
        using (stream)
        {
            Span<byte> head = stackalloc byte[8];
            if (stream.ReadAtLeast(head, 8, throwOnEndOfStream: false) < 8) return null;
            if (BitConverter.ToUInt32(head) != Il2CppMetadataMagic) return null;
            return BitConverter.ToInt32(head[4..]);
        }
    }

    private static string? ReadUnityVersion(Stream stream)
    {
        using (stream)
        {
            var buffer = new byte[16384];
            var n = stream.Read(buffer, 0, buffer.Length);
            if (n <= 0) return null;
            var m = UnityVersionRegex().Match(Encoding.ASCII.GetString(buffer, 0, n));
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    private static string MetadataEvidence(int? version) =>
        version is { } v ? $"global-metadata.dat (v{v})" : "global-metadata.dat (비표준 헤더)";
}
