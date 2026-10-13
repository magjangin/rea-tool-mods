using System;
using System.Collections.Generic;
using System.Linq;

namespace ReaToolMods.Models;

public enum TargetKind
{
    Exe,
    Dll,
    Apk,
    App,
    UnityIl2Cpp,
    UnityMono,
    UnityApk,
    UnityApp
}

/// <summary>
/// 분석 타겟 한 종류에 대한 정의 (버튼 표시, 작업 폴더 접미사, 허용 확장자, 필요한 도구)
/// </summary>
public sealed record TargetProfile(
    TargetKind Kind,
    string ShortName,
    string Icon,
    string ButtonLabel,
    string FolderSuffix,
    string[] Extensions,
    bool IsUnity,
    ToolId[] RequiredTools)
{
    public bool AcceptsExtension(string extension) =>
        Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}

public static class TargetCatalog
{
    public static readonly IReadOnlyList<TargetProfile> All =
    [
        new(TargetKind.Exe, "EXE", "🖥️", "EXE (디컴파일 & 스켈레톤)", "-windows",
            [".exe", ".dll", ".sys", ".so", ".dylib"], false,
            [ToolId.ReaCli]),
        new(TargetKind.Dll, "DLL", "⚙️", "DLL (C# 디컴파일 & 빌드)", "-dotnet",
            [".dll", ".exe"], false,
            [ToolId.DotnetSdk, ToolId.Ilspy]),
        new(TargetKind.Apk, "APK", "📱", "APK (Java 디컴파일 & ADB)", "-android",
            [".apk", ".xapk", ".apks", ".aab"], false,
            [ToolId.Java, ToolId.Jadx, ToolId.Apktool, ToolId.Adb]),
        new(TargetKind.App, "APP", "📦", "APP (ASAR 언팩 & 앱 실행)", "-electron",
            [".asar", ".zip", ".exe"], false,
            [ToolId.Node, ToolId.Asar]),

        new(TargetKind.UnityIl2Cpp, "Unity IL2CPP", "⚡", "IL2CPP (메타 덤프 & 후킹)", "-unity-il2cpp",
            [".exe", ".dll", ".dat"], true,
            [ToolId.Il2CppDumper, ToolId.Cpp2Il, ToolId.Ilspy, ToolId.DotnetSdk, ToolId.ReaCli]),
        new(TargetKind.UnityMono, "Unity Mono", "🐵", "Mono (C# 디컴파일 & 모드)", "-unity-mono",
            [".exe", ".dll"], true,
            [ToolId.Ilspy, ToolId.DotnetSdk]),
        new(TargetKind.UnityApk, "Unity APK", "📲", "Unity APK (녹스 · ADB)", "-unity-android",
            [".apk", ".xapk", ".apks", ".aab"], true,
            [ToolId.Il2CppDumper, ToolId.Cpp2Il, ToolId.Ilspy, ToolId.Jadx, ToolId.Adb]),
        new(TargetKind.UnityApp, "Unity APP", "🍎", "Unity APP (iOS IPA · macOS)", "-unity-app",
            [".ipa", ".app", ".zip"], true,
            [ToolId.Il2CppDumper, ToolId.Ilspy, ToolId.ReaCli]),
    ];

    public static TargetProfile Get(TargetKind kind) => All.First(p => p.Kind == kind);
}
