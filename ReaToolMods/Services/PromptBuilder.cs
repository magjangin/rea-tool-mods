using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ReaToolMods.Models;

namespace ReaToolMods.Services;

public sealed record PromptContext(
    TargetProfile Profile,
    string TargetPath,
    string WorkDir,
    DetectionResult? Detection,
    WorkspaceState Workspace,
    IReadOnlyDictionary<ToolId, ToolLocation> Tools);

/// <summary>
/// 공통 머리말(대상 정보) + 작업 폴더 상태(새로 시작/이어서) + 도구 경로 + 타겟별 단계 + 공통 규칙으로 프롬프트를 조립합니다.
/// </summary>
public static class PromptBuilder
{
    private const string TargetPlaceholder = "[분석 대상 파일 경로를 입력하거나 선택하세요]";

    public static string Build(PromptContext ctx)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, ctx);
        AppendWorkspace(sb, ctx);
        AppendTools(sb, ctx);
        if (ctx.Profile.IsUnity) AppendUnityGuardrails(sb, ctx);
        sb.AppendLine("## 수행 지침");
        sb.AppendLine(Steps(ctx).Trim());
        sb.AppendLine();
        AppendRules(sb, ctx);
        return sb.ToString().TrimEnd();
    }

    private static string Title(TargetKind kind) => kind switch
    {
        TargetKind.Exe => "네이티브 바이너리 역공학 및 스켈레톤 복원",
        TargetKind.Dll => ".NET 어셈블리 디컴파일 및 C# 프로젝트 복원",
        TargetKind.Apk => "APK 디컴파일 및 안드로이드 앱 분석",
        TargetKind.App => "Electron 앱 ASAR 언팩 및 실행 검증",
        TargetKind.UnityIl2Cpp => "Unity IL2CPP 메타데이터 덤프 및 모드 스켈레톤",
        TargetKind.UnityMono => "Unity Mono 어셈블리 디컴파일 및 모드 스켈레톤",
        TargetKind.UnityApk => "Unity 안드로이드(APK) 분석",
        TargetKind.UnityApp => "Unity iOS/macOS 앱 분석",
        _ => "역공학 워크플로우"
    };

    private static void AppendHeader(StringBuilder sb, PromptContext ctx)
    {
        var d = Matching(ctx);
        sb.AppendLine($"# [{ctx.Profile.ShortName}] {Title(ctx.Profile.Kind)} 요청");
        sb.AppendLine($"- 대상 파일: {Target(ctx)}");
        sb.AppendLine($"- 작업 디렉터리: {ctx.WorkDir}");

        if (d != null)
        {
            var kindName = TargetCatalog.Get(d.Kind).ShortName;
            var evidence = d.Evidence.Count > 0 ? " — " + string.Join(" · ", d.Evidence) : "";
            sb.AppendLine($"- 자동 판별: {kindName}{evidence}");
            if (d.UnityVersion != null) sb.AppendLine($"- Unity 버전: {d.UnityVersion}");
            if (d.Backend != null) sb.AppendLine($"- 스크립팅 백엔드: {d.Backend}");
            if (d.GameRoot != null) sb.AppendLine($"- 게임 루트: {d.GameRoot}");
            if (d.ModLoaders.Count > 0) sb.AppendLine($"- 설치된 모드 로더: {string.Join(", ", d.ModLoaders)}");
            foreach (var w in d.Warnings) sb.AppendLine($"- ⚠️ {w}");
        }

        sb.AppendLine();
    }

    private static void AppendWorkspace(StringBuilder sb, PromptContext ctx)
    {
        var ws = ctx.Workspace;
        var w = ctx.WorkDir;
        sb.AppendLine("## 0. 작업 폴더 상태");

        if (!ws.Exists)
        {
            sb.AppendLine($"- '{w}' 폴더가 아직 없습니다. 새로 만들고 `PROGRESS.md`를 생성해 단계별 진행 상황을 기록하면서 진행해줘.");
            sb.AppendLine();
            return;
        }

        if (!ws.HasContent)
        {
            sb.AppendLine($"- '{w}' 폴더가 비어 있습니다. 여기서 새로 시작하고 `PROGRESS.md`를 만들어 진행 상황을 기록해줘.");
            sb.AppendLine();
            return;
        }

        var more = ws.TotalEntries > ws.Entries.Count ? $" 외 {ws.TotalEntries - ws.Entries.Count}개" : "";
        sb.AppendLine($"- **이미 작업하던 폴더입니다.** 기존 항목: {string.Join(", ", ws.Entries)}{more}");
        sb.AppendLine("- 처음부터 다시 하지 말고 **기존 작업에 이어서** 진행해줘:");

        var progressName = ws.ProgressFile != null ? Path.GetFileName(ws.ProgressFile) : null;
        sb.AppendLine(progressName != null
            ? $"  1. 먼저 `{progressName}`과 기존 산출물을 읽고, 완료된 단계 / 남은 단계 / 막혔던 지점을 3~5줄로 요약한 뒤 시작해줘."
            : "  1. `PROGRESS.md`가 없으니 기존 폴더 구조와 산출물(logs, docs 등)을 훑어 어디까지 진행됐는지 추정해 `PROGRESS.md`를 먼저 만들어줘.");
        sb.AppendLine("  2. 기존 산출물 폴더는 삭제하거나 덮어쓰지 마. 다시 생성해야 하면 `src_v2`처럼 새 폴더로 출력하고 이유를 기록해줘.");
        sb.AppendLine("  3. 아래 '수행 지침' 중 이미 끝난 단계는 산출물이 실제로 있는지만 확인하고 건너뛰어줘.");

        if (ws.TargetDiffers && ws.Metadata != null)
        {
            sb.AppendLine($"  4. ⚠️ 이 폴더는 원래 '{ws.Metadata.TargetPath}' 분석용으로 만들어졌습니다. " +
                          "같은 프로젝트의 다른 파일이 맞는지 먼저 확인하고, 아니라면 진행하지 말고 알려줘.");
        }
        else if (ws.TargetChanged && ws.Metadata != null)
        {
            sb.AppendLine($"  4. ⚠️ 이전 작업({ws.Metadata.UpdatedAt:yyyy-MM-dd HH:mm}) 이후 대상 파일이 바뀌었습니다(크기/수정 시각 변경). " +
                          "업데이트된 버전으로 보이면 기존 산출물과 비교해 바뀐 클래스/함수 목록부터 정리해줘.");
        }

        if (ws.ProgressTail != null)
        {
            sb.AppendLine($"- `{progressName}` 최근 기록:");
            sb.AppendLine("```");
            sb.AppendLine(ws.ProgressTail);
            sb.AppendLine("```");
        }

        sb.AppendLine();
    }

    private static void AppendTools(StringBuilder sb, PromptContext ctx)
    {
        sb.AppendLine("## 사용할 도구");
        sb.AppendLine("수행 지침의 명령에 쓰인 도구 이름(`ilspycmd`, `adb` 등)은 아래 경로로 실행해줘.");
        foreach (var id in ctx.Profile.RequiredTools)
        {
            var t = ctx.Tools[id];
            sb.AppendLine(t.Found
                ? $"- {t.DisplayName} (`{t.CommandName}`): `{t.Path}`"
                : $"- {t.DisplayName} (`{t.CommandName}`): ❌ 설치되지 않음 → {t.InstallHint}. 설치할 수 없으면 해당 단계는 멈추고 알려줘.");
        }

        sb.AppendLine("- REA MCP 도구(open_binary, binary_overview, procedure_pseudo_code 등)가 연결되어 있으면 정적 분석에 우선 사용해줘.");
        if (UsesSlicer(ctx))
        {
            sb.AppendLine("- ⚠️ 10MB가 넘는 PE(GameAssembly.dll 등)는 REA `open_binary`로 통째로 열지 마. Ghidra가 파일 전체를 자동 분석하느라 응답이 MCP 시간 제한을 넘겨 타임아웃이 나. " +
                          "보고 싶은 함수만 `pe_slice`로 조각 PE(수백 KB)를 만들어 열면 1분 안에 의사코드가 나와.");
            sb.AppendLine("- REA가 앞선 호출 때문에 계속 응답이 없으면 `binary_session`으로 상태를 확인하고, 그래도 막혀 있으면 REA MCP 서버를 다시 시작해 달라고 내게 말해줘.");
        }
        if (ctx.Profile.RequiredTools.Any(id => ctx.Tools[id].Path?.Contains(' ') == true))
            sb.AppendLine("- 경로에 공백이 있으므로 PowerShell에서는 `& \"경로\" 인자` 형태로 실행해줘.");
        sb.AppendLine();
    }

    /// <summary>
    /// Unity 게임 전용 가드레일. 수행 지침보다 먼저 통과해야 하는 확인 단계입니다 (docs/USAGE_SCOPE.md 0·5절 반영).
    /// </summary>
    private static void AppendUnityGuardrails(StringBuilder sb, PromptContext ctx)
    {
        sb.AppendLine("## 시작 전 확인 (Unity 게임 가드레일)");
        sb.AppendLine("아래 확인을 **먼저** 마치고, 하나라도 통과하지 못하면 '수행 지침'을 시작하지 말고 이유와 근거를 보고한 뒤 멈춰줘. 결과는 `PROGRESS.md`에 남겨줘.");
        sb.AppendLine("1. **온라인 여부 확정 (구조 → 덤프 → 웹 검색 순서):** 파일 이름이나 Unity 백엔드만 보고 판단하지 마.");
        sb.AppendLine("   - 구조(읽기만): 난독화·보호 흔적과 네트워크 엔진·매치메이킹·안티치트·로그인 관련 파일을 찾아줘. 보호 흔적은 복호화하지 말고 \"보호됨\"이라고만 기록해줘.");
        sb.AppendLine("   - 덤프(읽기 전용): 서버 주소, 매치메이킹, 계정·로그인, 안티치트 관련 클래스와 문자열, 싱글플레이 전용 기능이 분리되어 있는지만 봐줘.");
        sb.AppendLine("   - 웹 검색: 위에서 찾은 구체적인 이름과 게임 이름으로 제작사 모드 정책, EULA, 스토어 페이지를 찾고 출처 링크를 남겨줘.");
        sb.AppendLine("   - 확정은 근거를 내게 보여 주고 내가 확인한 뒤에만 해줘. 난독화된 이름을 추측해서 단정하지 마. 근거가 엇갈리면 \"불확실\"로 두고 내게 물어줘.");
        sb.AppendLine("2. **받아들여지는 근거 찾기:** 제작사의 모드 정책·공식 SDK, EULA의 리버싱·수정 조항, 같은 일을 공개적으로 해 온 커뮤니티 관행 중 내 목적에 맞는 근거를 찾아줘. 근거가 불분명하면 바로 멈추지 말고 \"회색지대\"로 표시한 뒤 어디까지 할 수 있을지 후보를 내게 보여 줘. 근거도 없고 목적까지 불분명할 때만 멈춰줘.");
        sb.AppendLine("3. **온라인으로 확정되면** 제작사가 허락한 범위(공식 모드 도구·SDK·워크샵, 정책에 명시된 항목)를 우선 안내해줘. " +
                      "허락도 금지도 불분명하면 회색지대 후보(싱글플레이 부분만, 다른 플레이어에 영향 없는 클라이언트 전용 변경)를 근거와 \"확인되지 않은 것\"과 함께 보여 주고, 범위는 내가 고르게 해줘. " +
                      "단, 아래 6번 안전장치를 모드에 반드시 넣는 조건이면 멈추지 말고 클라이언트 전용 변경으로 진행해도 돼. 후보도 못 찾으면 멈춰.");
        sb.AppendLine("4. **어떤 경우에도 하지 마:** 서버 통신·인증·안티치트 우회와 패킷 변조, 다른 플레이어에게 보이는 능력·랭킹·재화·거래 변경(단, 6번 안전장치가 업로드를 확실히 막는 로컬 점수·기록 변경은 예외), 메모리 읽기·쓰기로 우위 확보(핵), 사설 서버 제작·운영, 라이선스·DRM·체험판 제한 제거(크랙).");
        sb.AppendLine("5. **클로닝 금지:** 결과물은 원본에 붙는 작은 패치(모드)로 한정해줘. 원본을 대신할 복제품을 만들지 말고, 디컴파일한 코드와 추출한 리소스(이미지·사운드·문구)를 복사해 옮기거나 내 PC 밖으로 내보내지 마. " +
                      "참고가 필요하면 동작을 글로 정리한 뒤 그 메모를 보고 새로 작성해줘.");
        sb.AppendLine("6. **모드에는 안전장치를 반드시 만들어줘:** 온라인 기능이 있는 게임이면 모드가 켜진 동안 점수·랭킹·기록·재화 등 서버로 가는 업로드를 스스로 차단하는 장치를 모드에 함께 넣어줘(업로드 호출만 건너뛰고 패킷은 건드리지 마). 점수·기록을 로컬에서 바꾸는 모드라면 업로드 차단이 먼저 동작하게 하고, 차단이 실패하거나 확인되지 않으면 변경도 적용되지 않게(fail-closed) 만들어줘. 안전장치를 넣을 수 없으면 그 변경은 하지 말고 내게 알려줘. 이 예외는 안티치트 우회를 허용하지 않아.");
        sb.AppendLine();
    }

    private static void AppendRules(StringBuilder sb, PromptContext ctx)
    {
        var w = ctx.WorkDir;
        sb.AppendLine("## 공통 규칙");
        sb.AppendLine($"- 원본 파일과 게임 설치 폴더는 수정하지 마. 필요하면 '{w}\\input'에 복사본을 만들어 작업해줘.");
        sb.AppendLine($"- 단계를 마칠 때마다 '{w}\\PROGRESS.md'에 `[완료|진행중|막힘] 단계명 — 산출물 경로 — 메모` 형식으로 기록해줘. 다음 세션이 이 파일만 보고 이어갈 수 있어야 해.");
        sb.AppendLine($"- 실행한 명령과 핵심 출력은 '{w}\\logs\\'에 남기고, 확인된 사실과 추정을 구분해서 써줘.");
        sb.AppendLine("- 도구가 없거나 같은 오류가 2번 이상 반복되면 우회책을 계속 시도하지 말고 원인과 필요한 조치를 정리한 뒤 멈춰줘.");
        var split = UsesSlicer(ctx) ? "큰 PE는 `pe_slice`로 함수 단위 조각을 만들어 REA로 열고, " : "";
        sb.AppendLine("- 단, **타임아웃은 위 규칙에 넣지 마.** 도구가 고장 난 게 아니라 한 번에 맡긴 일이 너무 크다는 뜻이야. " +
                      $"같은 호출을 그대로 반복하지 말고 더 작게 나눠서 바로 이어가줘 ({split}오래 걸리는 명령은 백그라운드로 돌리고 로그로 확인). " +
                      "내가 다시 하라고 하면 타임아웃을 이유로 거절하지 말고 이렇게 나눠서 다시 해줘.");
        sb.AppendLine("- 범위: 로컬·싱글플레이 분석과 모드 제작만. 온라인 게임의 안티치트/DRM/서버 검증 우회, 결제·재화 조작, 암호화된 바이너리나 메타데이터의 복호화는 하지 마.");
        sb.AppendLine();
        sb.AppendLine("## 최종 보고");
        sb.AppendLine("1. 이번에 완료한 단계  2. 산출물 경로  3. 핵심 발견 (진입점, 후킹 후보 등)  4. 다음 세션에서 이어서 할 일");
    }

    // ───────────────────────── 타겟별 단계 ─────────────────────────

    private static string Steps(PromptContext ctx) => ctx.Profile.Kind switch
    {
        TargetKind.Exe => ExeSteps(ctx),
        TargetKind.Dll => DllSteps(ctx),
        TargetKind.Apk => ApkSteps(ctx),
        TargetKind.App => AppSteps(ctx),
        TargetKind.UnityIl2Cpp => UnityIl2CppSteps(ctx),
        TargetKind.UnityMono => UnityMonoSteps(ctx),
        TargetKind.UnityApk => UnityApkSteps(ctx),
        TargetKind.UnityApp => UnityAppSteps(ctx),
        _ => ""
    };

    private static string ExeSteps(PromptContext ctx)
    {
        var w = ctx.WorkDir;
        return $"""
            1. 정적 분석 및 의사코드 덤프
               - REA(open_binary → binary_overview → list_procedures / procedure_pseudo_code)로 Import/Export, 문자열, 엔트리포인트를 정리해 '{w}\decompiled'에 저장해줘.
               - 대상이 10MB가 넘는 x64 PE면 통째로 열지 말고, Export 표 등에서 관심 함수의 RVA를 먼저 찾은 뒤 조각 PE로 열어줘:
                 `{Tool(ctx, ToolId.PeSlice)} --dll "{Target(ctx)}" --rva <함수 RVA> --out "{w}\slices\sub_<RVA>.dll"`
               - 메인 엔트리포인트와 핵심 로직의 제어 흐름을 '{w}\docs\analysis.md'에 명세서로 정리해줘.
            2. 스켈레톤 복원 및 실행 테스트
               - 핵심 동작을 재현/테스트할 수 있는 C/C++ 또는 C# 스켈레톤 프로젝트를 '{w}\skeleton'에 만들어줘.
               - 빌드 후 실행해 결과를 검증하고 로그를 남겨줘.
            """;
    }

    private static string DllSteps(PromptContext ctx)
    {
        var (t, w) = (Target(ctx), ctx.WorkDir);
        var refDir = File.Exists(t) ? Path.GetDirectoryName(t) : null;
        var refArg = refDir != null ? $" -r \"{refDir}\"" : "";
        return $"""
            1. C# 프로젝트 디컴파일
               `{Tool(ctx, ToolId.Ilspy)} -p -o "{w}\src"{refArg} "{t}"`
            2. 구조 파악
               - 네임스페이스/클래스 계층, 진입점, 외부 의존성을 '{w}\docs\analysis.md'에 정리해줘.
            3. 빌드 검증
               - `dotnet build "{w}\src"` 실행. 실패하면 오류를 (누락 참조 / 컴파일러 생성 코드 / unsafe·포인터) 유형별로 분류해 고쳐줘.
            4. 후킹 포인트 정리
               - 동작을 바꾸려면 어디를 패치해야 하는지 후보 메서드와 Harmony 패치 예시를 '{w}\docs\hooks.md'에 정리해줘.
            """;
    }

    private static string ApkSteps(PromptContext ctx)
    {
        var (t, w) = (Target(ctx), ctx.WorkDir);
        var adb = Tool(ctx, ToolId.Adb);
        return $"""
            1. 소스 및 리소스 디컴파일
               `{Tool(ctx, ToolId.Jadx)} -d "{w}\jadx_src" "{t}"`
               `{Tool(ctx, ToolId.Apktool)} d -f -o "{w}\apktool_out" "{t}"`
               - XAPK/APKS 같은 분할 패키지면 base.apk와 config.*.apk를 각각 처리해줘.
            2. 구조 분석
               - AndroidManifest의 메인 액티비티, 권한, lib\<abi>\*.so 네이티브 라이브러리 목록을 '{w}\docs\analysis.md'에 정리해줘.
               - libunity.so / libil2cpp.so가 보이면 Unity 앱이므로 'Unity APK' 프리셋으로 다시 진행하자고 알려줘.
            3. 에뮬레이터 실행 검증
               - 먼저 `bat\check_android_emulator_env.bat`로 VT·녹스·ADB 상태를 확인해줘.
               - `{adb} install -r "{t}"` → `{adb} shell monkey -p <패키지명> 1` → `{adb} logcat`으로 동작을 확인해줘.
            """;
    }

    private static string AppSteps(PromptContext ctx)
    {
        var w = ctx.WorkDir;
        var asar = Matching(ctx)?.AsarPath ?? Target(ctx);
        return $"""
            1. ASAR 언팩
               `{Tool(ctx, ToolId.Asar)} extract "{asar}" "{w}\extracted"`
               - 옆에 app.asar.unpacked 폴더가 있으면 '{w}\extracted'에 함께 복사해줘.
            2. 구조 분석
               - main / preload / renderer 진입점, IPC 채널 목록, 사용 중인 네이티브 모듈을 '{w}\docs\analysis.md'에 정리해줘.
            3. 로컬 실행 검증
               - '{w}\extracted'에서 `npm install` → `npx electron .`으로 실행하고, 주요 UI 기능과 IPC가 동작하는지 확인해줘.
            """;
    }

    private static string UnityIl2CppSteps(PromptContext ctx)
    {
        var w = ctx.WorkDir;
        var d = Matching(ctx);
        var root = d?.GameRoot ?? "<게임 폴더>";
        var gameAssembly = d?.GameAssemblyPath ?? Path.Combine(root, "GameAssembly.dll");
        var metadata = d?.MetadataPath ?? @"<게임>_Data\il2cpp_data\Metadata\global-metadata.dat";
        var version = d?.MetadataVersion is { } v ? $" (metadata v{v})" : "";
        var exeName = d?.DataDir != null ? Path.GetFileName(d.DataDir)[..^"_Data".Length] : "<게임 exe 이름>";

        return $"""
            1. IL2CPP 메타데이터 덤프{version}
               `{Tool(ctx, ToolId.Il2CppDumper)} "{gameAssembly}" "{metadata}" "{w}\il2cpp_dump"`
               - 산출물: DummyDll\, dump.cs, script.json, il2cpp.h
               - Il2CppDumper가 실패하거나 메타데이터 버전을 지원하지 않으면 Cpp2IL로 재시도해줘 (DummyDll만 생성. 오류 시 Enter를 기다리니 입력을 닫고 실행 — bash `< /dev/null`, cmd `< NUL`):
                 `{Tool(ctx, ToolId.Cpp2Il)} --game-path "{root}" --exe-name "{exeName}" --skip-analysis --disable-registration-prompts --output-root "{w}\cpp2il_out"`
               - 게임 폴더에 BepInEx\interop 또는 MelonLoader\Il2CppAssemblies가 이미 있으면 그 어셈블리를 우선 사용해줘.
               - global-metadata.dat 시그니처가 표준(0xFAB11BAF)이 아니면 보호된 메타데이터이므로 복호화하지 말고 보고 후 멈춰줘.
            2. 시그니처 탐색
               `{Tool(ctx, ToolId.Ilspy)} -p -o "{w}\src_dummy" "{w}\il2cpp_dump\DummyDll\Assembly-CSharp.dll"`
               - DummyDll은 메서드 본문이 비어 있으므로 클래스/필드/메서드 시그니처와 RVA 주석만 참고해줘.
            3. 네이티브 로직 분석 (GameAssembly.dll 원본은 REA로 통째로 열지 마 — 타임아웃 남)
               - 관심 클래스(또는 메서드)만 조각 PE로 만들어줘. script.json 이름이 export로 들어가서 의사코드에 메서드·문자열 이름이 그대로 보여:
                 `{Tool(ctx, ToolId.PeSlice)} --dll "{gameAssembly}" --script-json "{w}\il2cpp_dump\script.json" --class <클래스명> --out "{w}\slices\<클래스명>.dll"`
               - 메서드 하나만 볼 땐 `--class` 대신 `--method "<클래스명>::<메서드명>"`(셸이 `$$`를 변수로 바꾸니 `::`로 써줘), 주소로는 `--rva 0x<dump.cs의 RVA>`. 여러 번 써서 한 조각에 묶어도 돼.
               - 출력 끝의 'REA 다음 단계'대로 REA `open_binary`(조각 PE) → `procedure_pseudo_code`(출력된 VA)로 의사코드를 받아 '{w}\logs\<메서드명>.rea.c'에 저장해줘.
               - 조각 PE에는 호출 대상 함수의 앞부분만 들어 있어. 호출 대상이 궁금하면 그 함수로 조각을 새로 만들어줘.
               - 후킹 후보(메서드명, RVA, 역할)를 '{w}\docs\hooks.md'에 표로 정리해줘.
            4. 모드 스켈레톤
               - {LoaderGuidance(d, "BepInEx 6 (IL2CPP)")}
               - '{w}\mod'에 플러그인 프로젝트를 만들고 interop 어셈블리를 참조해 `dotnet build` 성공까지 확인해줘.
            5. 실행 검증
               - 게임 실행 후 BepInEx\LogOutput.log 또는 MelonLoader\Latest.log에서 플러그인이 로드되는지 확인해줘.
            """;
    }

    private static string UnityMonoSteps(PromptContext ctx)
    {
        var w = ctx.WorkDir;
        var d = Matching(ctx);
        var managed = d?.ManagedDir ?? @"<게임>_Data\Managed";
        return $"""
            1. C# 디컴파일 (읽기용)
               `{Tool(ctx, ToolId.Ilspy)} -p -o "{w}\src" -r "{managed}" "{managed}\Assembly-CSharp.dll"`
               - Assembly-CSharp-firstpass.dll이 있으면 '{w}\src_firstpass'로 따로 디컴파일해줘.
            2. 구조 파악
               - MonoBehaviour 진입점(Awake/Start/Update), 매니저 싱글톤, 세이브/설정 로직을 '{w}\docs\analysis.md'에 정리해줘.
               - 후킹 후보 메서드를 '{w}\docs\hooks.md'에 표로 정리해줘.
            3. 모드 스켈레톤 (빌드 검증은 여기서)
               - 디컴파일한 src는 그대로 빌드되지 않는 경우가 많으니 읽기용으로만 쓰고, 빌드는 별도 모드 프로젝트로 검증해줘.
               - {LoaderGuidance(d, "BepInEx 5 (Mono)")}
               - '{w}\mod'에 HarmonyX 패치 예제가 포함된 플러그인 프로젝트를 만들고, Managed DLL은 복사하지 말고 HintPath로 참조해 `dotnet build`까지 확인해줘.
            4. 실행 검증
               - 게임 실행 후 로더 로그에서 플러그인 로드와 패치 적용을 확인해줘.
            """;
    }

    private static string UnityApkSteps(PromptContext ctx)
    {
        var (t, w) = (Target(ctx), ctx.WorkDir);
        var d = Matching(ctx);
        var adb = Tool(ctx, ToolId.Adb);
        var abi = d?.Abis.FirstOrDefault(a => a == "arm64-v8a") ?? d?.Abis.FirstOrDefault() ?? "arm64-v8a";
        var unzipped = $@"{w}\apk_unzipped";
        var backend = d?.Backend;
        var split = d?.IsSplitPackage == true
            ? "\n   - 분할 패키지(XAPK/APKS)이므로 안에 있는 base.apk와 config.*.apk도 각각 같은 폴더에 풀어줘 (lib\\<abi>\\*.so는 config.<abi>.apk 쪽에 있음)."
            : "";

        var libil2cpp = $@"{unzipped}\lib\{abi}\libil2cpp.so";
        var metadata = $@"{unzipped}\assets\bin\Data\Managed\Metadata\global-metadata.dat";
        // Cpp2IL 2022.0.x는 버전을 숫자로만 파싱함: "2022.3.10f1" → FormatException, "2022.3.10" → OK
        var unityVersion = d?.UnityVersion is { } uv && Regex.Match(uv, @"^\d+\.\d+\.\d+") is { Success: true } numeric
            ? numeric.Value
            : "<Unity 버전, 예: 2022.3.10>";
        var il2cpp = $"""
            2-A. IL2CPP 덤프
               `{Tool(ctx, ToolId.Il2CppDumper)} "{libil2cpp}" "{metadata}" "{w}\il2cpp_dump"`
               - global-metadata.dat 시그니처가 표준이 아니면 복호화하지 말고 보고 후 멈춰줘.
               - Il2CppDumper가 실패하면 Cpp2IL로 재시도해줘 (APK는 force 옵션 3개를 함께 지정, 버전은 f1 같은 접미사 없이 숫자만. 오류 시 Enter를 기다리니 입력을 닫고 실행 — bash `< /dev/null`, cmd `< NUL`):
                 `{Tool(ctx, ToolId.Cpp2Il)} --game-path "{unzipped}" --force-binary-path "{libil2cpp}" --force-metadata-path "{metadata}" --force-unity-version "{unityVersion}" --skip-analysis --disable-registration-prompts --output-root "{w}\cpp2il_out"`
               `{Tool(ctx, ToolId.Ilspy)} -p -o "{w}\src_dummy" "{w}\il2cpp_dump\DummyDll\Assembly-CSharp.dll"`
            """;
        var mono = $"""
            2-B. Mono 디컴파일
               `{Tool(ctx, ToolId.Ilspy)} -p -o "{w}\src" -r "{unzipped}\assets\bin\Data\Managed" "{unzipped}\assets\bin\Data\Managed\Assembly-CSharp.dll"`
            """;
        var scriptStep = backend switch
        {
            "IL2CPP" => il2cpp.Replace("2-A.", "2."),
            "Mono" => mono.Replace("2-B.", "2."),
            _ => "(백엔드 미확정 — 압축을 푼 뒤 libil2cpp.so가 있으면 2-A, Managed\\Assembly-CSharp.dll이 있으면 2-B)\n" + il2cpp + "\n" + mono
        };

        return $"""
            0. APK 확보 (파일이 없을 때만)
               - `bat\check_android_emulator_env.bat`로 CPU 가상화(VT)·녹스 설치/실행·ADB 연결을 먼저 확인해줘.
               - 녹스에 직접 설치한 앱이라면 `{adb} shell pm list packages -3`로 패키지명을 찾고, `{adb} shell pm path <패키지명>`에 나온 base.apk와 split APK를 모두 `{adb} pull`로 '{w}\apk'에 가져와줘.
            1. 압축 해제
               `{Tar} -xf "{t}" -C "{unzipped}"` (폴더를 먼저 만들 것. Windows 내장 bsdtar — Git Bash의 GNU tar는 zip을 풀지 못함){split}
            {scriptStep}
            3. Java/네이티브 브리지 확인
               `{Tool(ctx, ToolId.Jadx)} -d "{w}\jadx_src" "{t}"`
               - UnityPlayerActivity 확장 여부, 네이티브 플러그인/SDK 브리지를 확인해 '{w}\docs\analysis.md'에 정리해줘.
            4. 녹스 실행 검증
               - `{adb} connect 127.0.0.1:62001` (녹스 기본 포트, 다중 인스턴스는 62025부터) 후
                 `{adb} install -r "{t}"` (분할 패키지는 `install-multiple`) → `{adb} logcat -s Unity`로 런타임 로그를 확인해줘.
            """;
    }

    private static string UnityAppSteps(PromptContext ctx)
    {
        var (t, w) = (Target(ctx), ctx.WorkDir);
        var ilspy = Tool(ctx, ToolId.Ilspy);
        var dumper = Tool(ctx, ToolId.Il2CppDumper);
        var isFolder = Directory.Exists(ctx.TargetPath);

        // 판별 결과의 메타데이터 경로에서 .app 번들 위치를 계산 (IPA는 압축을 풀 위치 기준)
        var app = "<앱>.app";
        bool? isIos = null;
        if (Matching(ctx)?.MetadataPath is { } metaPath)
        {
            var norm = metaPath.Replace('/', '\\');
            var idx = norm.IndexOf(@".app\", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                var bundle = norm[..(idx + ".app".Length)];
                app = Path.IsPathRooted(bundle) ? bundle : $@"{w}\unpacked\{bundle}";
                isIos = !norm.Contains(@"\Contents\", StringComparison.OrdinalIgnoreCase);
            }
        }

        var unpack = isFolder
            ? "1. 압축 해제 불필요 (.app 폴더를 직접 선택함)"
            : $"""
              1. 압축 해제 (IPA/ZIP)
                 `{Tar} -xf "{t}" -C "{w}\unpacked"` (폴더를 먼저 만들 것. Windows 내장 bsdtar 사용) → iOS는 Payload\<앱>.app, macOS는 <앱>.app\Contents
              """;
        var ios = $"- iOS IL2CPP: `{dumper} \"{app}\\Frameworks\\UnityFramework.framework\\UnityFramework\" \"{app}\\Data\\Managed\\Metadata\\global-metadata.dat\" \"{w}\\il2cpp_dump\"`";
        var mac = $"- macOS IL2CPP: `{dumper} \"{app}\\Contents\\Frameworks\\GameAssembly.dylib\" \"{app}\\Contents\\Resources\\Data\\il2cpp_data\\Metadata\\global-metadata.dat\" \"{w}\\il2cpp_dump\"`\n" +
                  $"   - macOS Mono: `{ilspy} -p -o \"{w}\\src\" \"{app}\\Contents\\Resources\\Data\\Managed\\Assembly-CSharp.dll\"`";
        var extract = isIos switch { true => ios, false => mac, null => ios + "\n   " + mac };

        return $"""
            {unpack}
            2. 암호화 여부 확인
               - App Store에서 받은 iOS 실행 파일은 FairPlay로 암호화되어 있을 수 있어. REA inspect_macho로 LC_ENCRYPTION_INFO의 cryptid를 확인하고,
                 cryptid=1이면 복호화하지 말고 보고 후 멈춰줘 (직접 빌드했거나 암호화되지 않은 빌드만 분석).
            3. 스크립트 코드 추출
               {extract}
               - IL2CPP 덤프 후에는 `{ilspy} -p -o "{w}\src_dummy" "{w}\il2cpp_dump\DummyDll\Assembly-CSharp.dll"`로 시그니처를 확인해줘.
            4. 로직 분석
               - REA로 UnityFramework / GameAssembly.dylib를 열어 script.json 주소와 대응시키고, 핵심 로직과 후킹 후보를 '{w}\docs\hooks.md'에 정리해줘.
            """;
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    private static string Target(PromptContext ctx) =>
        string.IsNullOrWhiteSpace(ctx.TargetPath) ? TargetPlaceholder : ctx.TargetPath;

    /// <summary>
    /// APK/IPA(zip) 압축 해제용 Windows 내장 bsdtar. Git Bash에서는 `tar`가 GNU tar라 zip을 풀지 못하므로 전체 경로를 씁니다.
    /// </summary>
    private static string Tar => $"\"{Path.Combine(Environment.SystemDirectory, "tar.exe")}\"";

    /// <summary>단계 안에서는 짧은 도구 이름을 쓰고, 실제 경로는 '사용할 도구' 섹션에 한 번만 적습니다.</summary>
    private static string Tool(PromptContext ctx, ToolId id) => ctx.Tools[id].CommandName;

    /// <summary>큰 PE를 조각 내서 REA로 여는 타겟인지 (IL2CPP의 GameAssembly.dll, 네이티브 EXE)</summary>
    private static bool UsesSlicer(PromptContext ctx) => ctx.Profile.RequiredTools.Contains(ToolId.PeSlice);

    /// <summary>현재 대상 파일에 대한 판별 결과만 사용 (다른 파일의 오래된 결과는 무시)</summary>
    private static DetectionResult? Matching(PromptContext ctx) =>
        ctx.Detection != null && string.Equals(ctx.Detection.FilePath, ctx.TargetPath, StringComparison.OrdinalIgnoreCase)
            ? ctx.Detection
            : null;

    private static string LoaderGuidance(DetectionResult? d, string recommended) =>
        d?.ModLoaders.Count > 0
            ? $"게임에 이미 설치된 로더({string.Join(", ", d.ModLoaders)}) 기준으로 플러그인을 만들어줘."
            : $"게임에 모드 로더가 없습니다. {recommended} 또는 MelonLoader 중 무엇을 쓸지 제안만 하고, 게임 폴더에 설치하는 건 내 확인을 받은 뒤 진행해줘.";
}
