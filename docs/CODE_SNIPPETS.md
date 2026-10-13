# REA Tool Mods — 핵심 코드 스니펫

코드를 고치거나 기능을 추가할 때 먼저 봐야 할 부분만 모았습니다. 스니펫은 실제 소스에서 발췌했고, 길이를 줄인 곳은 `// ...`로 표시했습니다.

## 목차
1. [전체 흐름](#1-전체-흐름)
2. [새 타겟 추가하기 — `TargetCatalog`](#2-새-타겟-추가하기--targetcatalog)
3. [파일 시그니처 판별 — `ReadSignature`](#3-파일-시그니처-판별--readsignature)
4. [IL2CPP 메타데이터 헤더 — `ReadMetadataVersion`](#4-il2cpp-메타데이터-헤더--readmetadataversion)
5. [Unity PC 빌드 판별 — `InspectUnityWindows`](#5-unity-pc-빌드-판별--inspectunitywindows)
6. [APK · XAPK 내부 검사 — `CollectZipEntries`](#6-apk--xapk-내부-검사--collectzipentries)
7. [버튼과 파일 일치 검사 — `Validate`](#7-버튼과-파일-일치-검사--validate)
8. [기존 작업 폴더 이어하기](#8-기존-작업-폴더-이어하기)
9. [사용자가 고친 프롬프트 보호 — `RegeneratePrompt`](#9-사용자가-고친-프롬프트-보호--regenerateprompt)
10. [CPU 가상화(VT) 점검 — `VirtualizationCheck`](#10-cpu-가상화vt-점검--virtualizationcheck)
11. [외부 도구 버전 확인 (타임아웃) — `RunAsync`](#11-외부-도구-버전-확인-타임아웃--runasync)
12. [배치 스크립트 요령](#12-배치-스크립트-요령)

---

## 1. 전체 흐름

```text
파일 선택 / 경로 입력
  └─ TargetDetector.Detect()        시그니처 · 폴더 구조 · 압축 엔트리 → DetectionResult
       └─ MainViewModel.SelectTarget()
            ├─ UpdateWorkDirPath()   <이름><접미사> 또는 같은 대상의 기존 폴더
            ├─ RefreshValidation()   TargetDetector.Validate() → 경고 표시
            └─ RegeneratePrompt()
                 ├─ WorkspaceInspector.Inspect()   새 폴더 / 빈 폴더 / 이어하기
                 ├─ ToolLocator.LocateAll()        도구 실제 경로
                 └─ PromptBuilder.Build()          머리말 + 0.작업폴더 + 도구 + 단계 + 공통 규칙
전송 버튼
  └─ AgentLauncher.SendAsync()     .reatool/prompts 스냅샷 + workspace.json → 클립보드 → 창 포커스 → Ctrl+V → Enter
```

| 파일 | 역할 |
|---|---|
| `Models/TargetProfile.cs` | 8종 타겟 정의 (`TargetCatalog`) |
| `Services/TargetDetector.cs` | 판별 + 검증 |
| `Services/WorkspaceInspector.cs` | 작업 폴더 상태, `.reatool/workspace.json` |
| `Services/PromptBuilder.cs` | 프롬프트 조립 |
| `Services/EnvironmentChecker.cs` | 환경 점검 카드 |
| `Services/ToolLocator.cs` | `tools/`, PATH, 녹스 설치 경로에서 도구 찾기 |
| `Services/AgentLauncher.cs` | Claude / Antigravity 전송 |

---

## 2. 새 타겟 추가하기 — `TargetCatalog`

`Models/TargetProfile.cs` — 버튼, 작업 폴더 접미사, 허용 확장자, 필요한 도구가 모두 여기 한 줄에 정의됩니다. `IsUnity`가 `true`면 Unity 줄에 버튼이 생깁니다.

```csharp
new(TargetKind.UnityIl2Cpp, "Unity IL2CPP", "⚡", "IL2CPP (메타 덤프 & 후킹)", "-unity-il2cpp",
    [".exe", ".dll", ".dat"], true,
    [ToolId.Il2CppDumper, ToolId.Cpp2Il, ToolId.Ilspy, ToolId.DotnetSdk, ToolId.ReaCli]),
```

새 타겟을 추가할 때 손댈 곳:
1. `TargetKind` enum에 값 추가
2. `TargetCatalog.All`에 위와 같은 한 줄 추가
3. `PromptBuilder.Title()` / `Steps()`에 제목과 단계 메서드 추가
4. 자동 판별이 필요하면 `TargetDetector.Detect()`의 `switch (ext)`에 분기 추가

---

## 3. 파일 시그니처 판별 — `ReadSignature`

`Services/TargetDetector.cs` — 앞 8바이트로 실제 형식을 판단합니다. 확장자와 다르면 `CheckSignature`가 경고를 남기고, ZIP이 아닌 `.apk`처럼 내부를 열 수 없는 파일은 확장자 기준으로만 분류합니다.

```csharp
private static Signature ReadSignature(string path)
{
    Span<byte> head = stackalloc byte[8];
    using var fs = File.OpenRead(path);
    var n = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
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
```

| 형식 | 시그니처 |
|---|---|
| PE (EXE/DLL) | `MZ` |
| ZIP (APK/IPA/XAPK) | `PK\x03\x04` (빈 ZIP은 `PK\x05\x06`) |
| IL2CPP 메타데이터 | `AF 1B B1 FA` (little-endian `0xFAB11BAF`) |
| ELF (.so) | `7F 45 4C 46` |
| Mach-O | `FEEDFACE` / `FEEDFACF` / 역순 / `CAFEBABE`(fat) |
| ASAR | 첫 uint32 = `4` (pickle 헤더 크기) |

---

## 4. IL2CPP 메타데이터 헤더 — `ReadMetadataVersion`

`global-metadata.dat`은 `매직(4바이트) + 버전(int32)`으로 시작합니다. 매직이 다르면 `null`을 반환하고, 호출부에서 "암호화/보호된 메타데이터일 수 있음" 경고를 남깁니다. 프롬프트도 이 경우 **복호화하지 말고 보고 후 멈추도록** 지시합니다.

```csharp
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
```

> 메타데이터 버전은 Il2CppDumper 지원 범위를 판단하는 기준입니다. 높은 버전(v31 이상)에서 Il2CppDumper가 실패하면 프롬프트가 Cpp2IL로 재시도하도록 안내합니다.

---

## 5. Unity PC 빌드 판별 — `InspectUnityWindows`

`Game.exe`, `GameAssembly.dll`, `global-metadata.dat`, `Assembly-CSharp.dll` 중 무엇을 골라도 결국 이 메서드로 게임 루트 전체를 검사합니다.

```csharp
private static bool InspectUnityWindows(string root, string dataDir, DetectionResult r)
{
    var gameAssembly = Path.Combine(root, "GameAssembly.dll");
    var metadata = Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat");
    var managed = Path.Combine(dataDir, "Managed");
    var hasUnityPlayer = File.Exists(Path.Combine(root, "UnityPlayer.dll"));
    // ...

    if (File.Exists(gameAssembly) || File.Exists(metadata))
    {
        r.Kind = TargetKind.UnityIl2Cpp;          // IL2CPP: 네이티브 GameAssembly + 메타데이터
        // ...
    }
    else if (File.Exists(Path.Combine(managed, "Assembly-CSharp.dll")))
    {
        r.Kind = TargetKind.UnityMono;            // Mono: 관리 어셈블리가 그대로 있음
        // ...
    }
    else if (hasUnityPlayer || hasGgm)
    {
        // Unity는 맞지만 백엔드 미확정 → 경고
    }
    else
    {
        return false;
    }

    r.UnityVersion = DetectUnityVersionWindows(root, dataDir);   // UnityPlayer.dll ProductVersion → globalgamemanagers
    DetectModLoaders(root, r);                                   // BepInEx / MelonLoader / Doorstop
    return true;
}
```

판별 기준 요약:

| 구조 | 결과 |
|---|---|
| `GameAssembly.dll` 또는 `<Game>_Data\il2cpp_data\Metadata\global-metadata.dat` | Unity IL2CPP |
| `<Game>_Data\Managed\Assembly-CSharp.dll` | Unity Mono |
| `BepInEx\interop\*.dll`, `MelonLoader\Il2CppAssemblies\*.dll` | Unity IL2CPP (모드 로더가 생성한 interop) |
| `resources\app.asar` | APP (Electron) |
| PE에 .NET 메타데이터 있음 | DLL (.NET) |

---

## 6. APK · XAPK 내부 검사 — `CollectZipEntries`

APK는 압축을 풀지 않고 엔트리 이름만 훑습니다. `global-metadata.dat`과 `globalgamemanagers`는 스트림 앞부분만 읽어 버전을 얻고, XAPK/APKS 안의 분할 APK는 임시 파일로 꺼내 한 단계 더 검사합니다.

```csharp
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
    else if (expandNestedApks && name.EndsWith(".apk"))   // .xapk / .apks 일 때만
    {
        r.IsSplitPackage = true;
        // 임시 파일로 추출 → ZipFile.OpenRead → CollectZipEntries(inner, name + "!/", ...)
    }
}
```

이후 `ApplyUnityMarkers`가 수집한 이름에서 흔적을 찾습니다.

| 흔적 | 의미 |
|---|---|
| `lib/<abi>/libil2cpp.so`, `.../metadata/global-metadata.dat` | IL2CPP |
| `assets/bin/data/managed/assembly-csharp.dll` | Mono |
| `lib/<abi>/libunity.so`, `data/globalgamemanagers` | Unity (백엔드 미확정 가능) |
| `config.arm64_v8a.apk` (XAPK 안) | ABI = arm64-v8a |

---

## 7. 버튼과 파일 일치 검사 — `Validate`

자동 판별과 별개로, 사용자가 누른 버튼이 파일과 맞는지 확인합니다. 경고는 타겟 버튼 아래에 주황색으로 표시됩니다.

```csharp
public static List<string> Validate(TargetProfile profile, string path, DetectionResult? detection)
{
    var warnings = new List<string>();
    // ...
    if (!profile.AcceptsExtension(ext))
    {
        warnings.Add($"[{profile.ShortName}] 타겟에는 '{ext}' 파일이 맞지 않습니다. " +
                     $"(허용: {string.Join(", ", profile.Extensions)})");
    }

    if (detection != null && string.Equals(detection.FilePath, path, StringComparison.OrdinalIgnoreCase))
    {
        if (detection.Kind != profile.Kind)
            warnings.Add($"자동 판별 결과는 [{TargetCatalog.Get(detection.Kind).ShortName}]입니다 — 의도한 선택인지 확인하세요.");

        warnings.AddRange(detection.Warnings);   // 시그니처 불일치, 비표준 메타데이터 등
    }

    return warnings;
}
```

---

## 8. 기존 작업 폴더 이어하기

### 작업 폴더 결정 — `MainViewModel.UpdateWorkDirPath`
사용자가 직접 고른 폴더(`_workDirPinned`)는 건드리지 않습니다. 자동 이름 폴더가 없으면, 접미사가 달라도 같은 대상을 다루던 `<이름>-*` 폴더를 찾아 재사용합니다.

```csharp
private void UpdateWorkDirPath()
{
    if (_workDirPinned) return;
    // ... baseName = 대상 파일 이름(확장자 제외)

    var auto = Path.Combine(ReposRoot, baseName + _profile.FolderSuffix);
    SetWorkDir(Directory.Exists(auto) ? auto : FindExistingWorkspace(baseName, target) ?? auto);
}
```

`FindExistingWorkspace`는 `.reatool/workspace.json`의 `TargetPath`가 같은 폴더를 찾고, 메타데이터가 없는 예전(v1.3) 폴더는 `claude_prompt.md` / `antigravity_prompt.md`에 대상 경로가 들어 있는지로 판단합니다.

### 상태 메타데이터 — `.reatool/workspace.json`
전송할 때 `AgentLauncher`가 저장합니다. 다음 번 `WorkspaceInspector.Inspect()`가 이 값과 현재 파일을 비교합니다.

```csharp
public sealed record WorkspaceMetadata(
    string TargetPath,
    TargetKind Kind,
    long TargetSize,
    DateTime TargetLastWriteUtc,
    DateTime CreatedAt,
    DateTime UpdatedAt);
```

| 상태 | 프롬프트에 들어가는 지시 |
|---|---|
| 폴더 없음 | 새로 만들고 `PROGRESS.md`로 진행 기록 |
| 빈 폴더 | 여기서 새로 시작 |
| 산출물 있음 | `PROGRESS.md` 먼저 읽고 요약 → 끝난 단계 건너뛰기, 기존 산출물 덮어쓰지 않기(`src_v2`) + 최근 기록 20줄 첨부 |
| `TargetChanged` (크기/수정 시각 다름) | 바뀐 클래스/함수 비교부터 |
| `TargetDiffers` (다른 대상 경로) | 진행하지 말고 확인 요청 |

---

## 9. 사용자가 고친 프롬프트 보호 — `RegeneratePrompt`

마지막으로 생성한 프롬프트를 기억해 두고, 백그라운드 갱신(환경 점검 완료 등)에서는 사용자가 내용을 고쳤으면 덮어쓰지 않습니다. 버튼 클릭이나 파일 선택 같은 명시적 동작은 항상 다시 생성합니다.

```csharp
private void RegeneratePrompt(bool onlyIfUnmodified = false)
{
    var target = CleanPath(TargetFilePath);
    var ws = WorkspaceInspector.Inspect(WorkDirPath, target);
    WorkspaceSummary = DescribeWorkspace(ws);

    if (onlyIfUnmodified && PromptText != _lastGeneratedPrompt) return;

    var ctx = new PromptContext(_profile, target, WorkDirPath, _detection, ws, ToolLocator.LocateAll());
    PromptText = _lastGeneratedPrompt = PromptBuilder.Build(ctx);
}
```

---

## 10. CPU 가상화(VT) 점검 — `VirtualizationCheck`

WMI나 `systeminfo`(한글 Windows에서는 출력이 현지화됨) 대신 CPU 명령과 Win32 API로 언어와 상관없이 판단합니다.

```csharp
// CPUID(1).ECX bit 31 = 하이퍼바이저 위에서 실행 중
var hypervisor = X86Base.CpuId(1, 0).Ecx < 0;
if (hypervisor)
{
    // BIOS VT는 켜져 있지만 Hyper-V 계열(WSL2 · 메모리 무결성 등)이 점유 중 → Warning
}

return OperatingSystem.IsWindows() && IsProcessorFeaturePresent(PF_VIRT_FIRMWARE_ENABLED)   // = 21
    ? /* Ok: BIOS/UEFI에서 활성화됨 */
    : /* Missing: BIOS/UEFI에서 VT-x / SVM 켜기 */;
```

> 하이퍼바이저가 돌고 있으면 Windows는 "펌웨어 VT 사용 가능"을 `false`로 보고합니다. 그래서 하이퍼바이저 비트를 **먼저** 확인해야 "VT가 꺼져 있다"는 오진을 피할 수 있습니다. `bat/check_android_emulator_env.ps1`도 같은 순서(`HypervisorPresent` → `VirtualizationFirmwareEnabled`)로 판단합니다.

---

## 11. 외부 도구 버전 확인 (타임아웃) — `RunAsync`

`.cmd` / `.bat` 셔임(npm 전역 도구, `jadx.bat` 등)은 `cmd /c`로 감싸고, stdin을 바로 닫아 입력 대기로 멈추는 일을 막습니다. 시간 초과 시 프로세스 트리 전체를 종료합니다.

```csharp
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
// ...
proc.StandardInput.Close();
using var cts = new CancellationTokenSource(timeoutMs);
try { await proc.WaitForExitAsync(cts.Token); }
catch (OperationCanceledException)
{
    try { proc.Kill(entireProcessTree: true); } catch { }
    return (false, "응답 없음 (시간 초과)");
}
```

> Il2CppDumper와 Cpp2IL은 인자 없이 실행하면 키 입력을 기다리므로 실행하지 않고 `FileVersionInfo`로만 확인합니다.

---

## 12. 배치 스크립트 요령

### `if (...)` 블록 안에서 파일 쓰기 — `download_analysis_tools.bat`
블록 안에 `::` 주석과 `( ... ) > file` 블록이 함께 있으면 cmd가 리다이렉트를 블록 전체에 적용해, 다운로드 로그까지 `apktool.bat`에 섞여 들어가는 버그가 있었습니다. 블록 밖에서 한 줄씩 쓰도록 바꿨습니다.

```bat
rem Write the apktool.bat wrapper outside the if-block
if exist "%APKTOOL_DIR%\apktool.jar" (
    > "%APKTOOL_DIR%\apktool.bat" echo @echo off
    >> "%APKTOOL_DIR%\apktool.bat" echo java -jar "%%~dp0apktool.jar" %%*
)
```

### cmd 따옴표 안에서 PowerShell에 `"` 넘기기
`-Command "..."` 안에서 `\"`를 쓰면 cmd의 따옴표 상태가 뒤집혀 `|` 같은 문자가 cmd에 해석됩니다. `[char]34`로 만들어 씁니다. Il2CppDumper가 덤프 후 키 입력을 기다리지 않도록 `config.json`을 고치는 부분입니다.

```bat
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$q = [char]34;" ^
    "Get-ChildItem -Path '%IL2CPPDUMPER_DIR%' -Filter config.json -Recurse -ErrorAction SilentlyContinue | ForEach-Object {" ^
    "  $text = [IO.File]::ReadAllText($_.FullName);" ^
    "  $new = $text -replace ($q + 'RequireAnyKey' + $q + ':\s*true'), ($q + 'RequireAnyKey' + $q + ': false');" ^
    "  if ($new -ne $text) { [IO.File]::WriteAllText($_.FullName, $new); Write-Host '  - config.json: RequireAnyKey disabled for non-interactive runs.' }" ^
    "}"
```

### 긴 점검 로직은 `.bat` + `.ps1`로 분리
`check_android_emulator_env.bat`은 `.ps1`을 호출만 합니다. 여러 줄 PowerShell을 `^`로 이어 붙이는 것보다 안전하고, 종료 코드(0 준비됨 / 1 문제 / 2 경고)를 그대로 전달합니다.

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0check_android_emulator_env.ps1" %*
set "RESULT=%ERRORLEVEL%"
echo.
pause
exit /b %RESULT%
```
