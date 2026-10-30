namespace ReaToolMods.Models;

public enum ToolId
{
    DotnetSdk,
    Ilspy,
    Java,
    Jadx,
    Apktool,
    Node,
    Asar,
    Il2CppDumper,
    Cpp2Il,
    Adb,
    ReaCli,
    PeSlice,
    Il2CppAsm,
    UberApkSigner
}

/// <summary>
/// 분석 도구의 실제 위치. Path가 null이면 설치되지 않은 것으로 간주합니다.
/// </summary>
public sealed record ToolLocation(ToolId Id, string DisplayName, string CommandName, string? Path, string InstallHint)
{
    public bool Found => Path != null;
}
