using System.Collections.Generic;

namespace ReaToolMods.Models;

/// <summary>
/// 대상 파일 내용(시그니처, 폴더 구조, 압축 엔트리)을 검사해 얻은 판별 결과
/// </summary>
public sealed class DetectionResult
{
    public required string FilePath { get; init; }
    public TargetKind Kind { get; set; } = TargetKind.Exe;

    /// <summary>판별 근거 (예: "GameAssembly.dll 발견")</summary>
    public List<string> Evidence { get; } = [];

    /// <summary>확장자/시그니처 불일치 등 사용자가 알아야 할 경고</summary>
    public List<string> Warnings { get; } = [];

    public string? GameRoot { get; set; }
    public string? DataDir { get; set; }
    public string? ManagedDir { get; set; }
    public string? GameAssemblyPath { get; set; }
    public string? MetadataPath { get; set; }
    public int? MetadataVersion { get; set; }
    public string? UnityVersion { get; set; }

    /// <summary>"IL2CPP" / "Mono" / null(미확정)</summary>
    public string? Backend { get; set; }

    /// <summary>"Unity" / "UnrealEngine" / "Godot" / "Electron" / null</summary>
    public string? Engine { get; set; }

    /// <summary>"UE4" / "UE5" / null</summary>
    public string? UnrealVersion { get; set; }

    /// <summary>Steam API 연동 여부</summary>
    public bool HasSteamApi { get; set; }

    /// <summary>Steam AppID (steam_appid.txt 등에서 파싱)</summary>
    public string? SteamAppId { get; set; }

    /// <summary>감지된 안티치트 솔루션 (EAC, BattlEye 등)</summary>
    public string? AntiCheat { get; set; }

    public string? AsarPath { get; set; }
    public bool IsSplitPackage { get; set; }
    public List<string> Abis { get; } = [];
    public List<string> ModLoaders { get; } = [];
}
