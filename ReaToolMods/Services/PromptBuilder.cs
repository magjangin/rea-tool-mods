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
/// Unity 프리셋도 별도 가드레일 섹션 없이 같은 공통 규칙만 받습니다.
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
        TargetKind.UnityApk => "Unity 안드로이드(APK) 분석 및 Frida Gadget 모드 제작",
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

        if (NativeIsNotPe(ctx))
            sb.AppendLine("- ⚠️ 이 PC(Windows)의 REA는 PE만 열 수 있어. libil2cpp.so(ELF) · UnityFramework · GameAssembly.dylib(Mach-O)는 REA `open_binary`로 열지 마 — 지원하지 않는 형식이라 실패만 해. " +
                          "함수 본문은 `il2cpp_asm`으로 봐줘 (dump.cs 오프셋으로 바로 읽어 호출 대상 · 문자열 · TypeInfo 이름을 주석으로 붙여 줘, 몇 초면 끝나).");
        else
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
        sb.AppendLine($"- 덤프 · 디컴파일 · 압축 해제(Il2CppDumper, Cpp2IL, `ilspycmd -p`, jadx, apktool 등)는 큰 게임에서 몇 분 걸릴 수 있어. " +
                      $"셸 도구 시간 제한에 걸리지 않게 출력을 '{w}\\logs\\' 아래 로그 파일로 남기면서 백그라운드로 실행하고, 끝났는지는 로그 마지막 줄과 산출물로 확인해줘.");
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
        var apktool = Tool(ctx, ToolId.Apktool);
        var signer = Tool(ctx, ToolId.UberApkSigner);
        const string gadgetConfig = """{"interaction":{"type":"script","path":"/storage/emulated/0/Android/data/<패키지명>.mod/files/mod.js","on_change":"reload"}}""";
        return $"""
            1. 소스 및 리소스 디컴파일
               `{Tool(ctx, ToolId.Jadx)} -d "{w}\jadx_src" "{t}"`
               `{apktool} d -f -o "{w}\apktool_out" "{t}"`
               - XAPK/APKS 같은 분할 패키지면 base.apk와 config.*.apk를 각각 처리해줘.
            2. 구조 분석
               - AndroidManifest의 패키지명, 메인 액티비티, ContentProvider(`android:authorities`), 권한, lib\<abi>\*.so 네이티브 라이브러리 목록을 '{w}\docs\analysis.md'에 정리해줘.
               - libunity.so / libil2cpp.so가 보이면 Unity 앱이므로 'Unity APK' 프리셋으로 다시 진행하자고 알려줘.
            3. 실행 검증 (USB 연결 스마트폰 우선, 없으면 녹스 에뮬레이터)
               - 먼저 `{adb} devices`를 실행해 USB로 연결된 실제 스마트폰 기기(device)가 있는지 확인해줘 (지정된 `{adb}`만 사용하고 다른 버전의 adb나 REA 내장 ADB 도구를 섞어 쓰지 마 — 서버 재시작으로 연결이 끊김).
               - 실제 스마트폰이 연결되어 있다면 가급적 그 실기기를 우선 사용하고, 연결된 기기가 없다면 `bat\check_android_emulator_env.bat`로 VT·녹스·ADB 상태를 확인하고 녹스 에뮬레이터로 진행해줘.
               - 기기에 아직 앱이 없을 때만 `{adb} install -r "{t}"`로 설치하고(이미 Play 스토어 등에서 같은 버전이 깔려 있으면 덮어쓰지 마), `{adb} shell monkey -p <패키지명> 1` → `{adb} logcat`으로 동작을 확인해줘 (연결된 기기가 여러 대면 `-s <기기ID>` 지정).
            4. 리패키징 및 Frida Gadget 복제 앱 후킹 (선택)
               - ⚠️ **서명이 달라진 수정 APK를 원본 패키지명 그대로 `install -r`하지 마**: 원본과 서명이 다르면 `INSTALL_FAILED_UPDATE_INCOMPATIBLE`로 실패하며, 원본을 지우고 깔면 로컬 데이터(세이브)와 Google 로그인이 날아가. 원본과 나란히 설치할 수 있도록 `AndroidManifest.xml`의 `package`, 모든 `android:authorities`, `android:taskAffinity`, 자체 정의 `.permission.` 이름을 `<패키지명>.mod`로 바꾼 **복제 패키지**로 만들어줘 (액티비티/서비스 클래스명과 인텐트 액션 이름은 바꾸지 마).
               - Smali 코드나 네이티브 라이브러리를 수정한 뒤 APK를 다시 패키징할 때는 apktool로 빌드하고 uber-apk-signer로 정렬/서명해줘:
                 `{apktool} b "{w}\apktool_out" -o "{w}\dist\rebuilt.unsigned.apk"`
                 `{signer} --apks "{w}\dist\rebuilt.unsigned.apk" -o "{w}\dist"`
               - 루팅되지 않은 기기에서 Frida 후킹이 필요하면 `tools\frida-gadget\libfrida-gadget-arm64.so`를 `{w}\apktool_out\lib\<abi>\libfrida-gadget.so`로 복사하고, 진입점 액티비티 Smali의 `onCreate` 맨 앞(annotation 직후)에 `System.loadLibrary("frida-gadget")`를 삽입해줘.
               - 상시 동작 모드는 같은 폴더에 `libfrida-gadget.config.so`를 `{gadgetConfig}`로 넣어 PC 없이 스크립트가 로드되게 하고, 고정 키로 서명한 복제 APK를 `{adb} install -r "{w}\dist\rebuilt-aligned-debugSigned.apk"`로 설치해줘 (동일 테스트 키로 서명한 `.mod` 복제본끼리는 `install -r`로 데이터를 유지한 채 업데이트 가능).
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
            2. 시그니처 탐색
               `{Tool(ctx, ToolId.Ilspy)} -p -o "{w}\src_dummy" "{w}\il2cpp_dump\DummyDll\Assembly-CSharp.dll"`
               - DummyDll은 메서드 본문이 비어 있으므로 클래스/필드/메서드 시그니처와 RVA 주석만 참고해줘.
            3. 네이티브 로직 분석 (GameAssembly.dll 원본은 REA로 통째로 열지 마 — 타임아웃 남)
               - 관심 클래스(또는 메서드)만 조각 PE로 만들어줘. script.json 이름이 export로 들어가서 의사코드에 메서드·문자열 이름이 그대로 보여:
                 `{Tool(ctx, ToolId.PeSlice)} --dll "{gameAssembly}" --script-json "{w}\il2cpp_dump\script.json" --class <클래스명> --out "{w}\slices\<클래스명>.dll"`
               - 메서드 하나만 볼 땐 `--class` 대신 `--method "<클래스명>::<메서드명>"`(셸이 `$$`를 변수로 바꾸니 `::`로 써줘), 주소로는 `--rva 0x<dump.cs의 RVA>`. 여러 번 써서 한 조각에 묶어도 돼.
               - 출력 끝의 'REA 다음 단계'대로 REA `open_binary`(조각 PE) → `procedure_pseudo_code`(출력된 VA)로 의사코드를 받아 '{w}\logs\<메서드명>.rea.c'에 저장해줘.
               - 조각 PE에는 호출 대상 함수의 앞부분만 들어 있어. 호출 대상이 궁금하면 그 함수로 조각을 새로 만들어줘.
               - REA가 연결되어 있지 않으면 같은 클래스를 주석 달린 어셈블리로 봐줘 (몇 초, 의사코드 대신 어셈블리):
                 `{Tool(ctx, ToolId.Il2CppAsm)} --binary "{gameAssembly}" --dump "{w}\il2cpp_dump\dump.cs" --script-json "{w}\il2cpp_dump\script.json" --class <클래스명> --out "{w}\logs\<클래스명>.asm"`
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
               - Assembly-CSharp.dll이 크면 전체 디컴파일에 몇 분 걸려. 백그라운드로 돌려 두고, 그동안 관심 클래스는 하나씩 바로 볼 수 있어:
                 `{Tool(ctx, ToolId.Ilspy)} -l c "{managed}\Assembly-CSharp.dll"` (클래스 목록) → `{Tool(ctx, ToolId.Ilspy)} -t <네임스페이스.클래스명> -r "{managed}" "{managed}\Assembly-CSharp.dll"` (그 클래스만 C#으로 출력)
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
        var apktool = Tool(ctx, ToolId.Apktool);
        var signer = Tool(ctx, ToolId.UberApkSigner);
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
               - Il2CppDumper가 실패하면 Cpp2IL로 재시도해줘 (APK는 force 옵션 3개를 함께 지정, 버전은 f1 같은 접미사 없이 숫자만. 오류 시 Enter를 기다리니 입력을 닫고 실행 — bash `< /dev/null`, cmd `< NUL`):
                 `{Tool(ctx, ToolId.Cpp2Il)} --game-path "{unzipped}" --force-binary-path "{libil2cpp}" --force-metadata-path "{metadata}" --force-unity-version "{unityVersion}" --skip-analysis --disable-registration-prompts --output-root "{w}\cpp2il_out"`
               `{Tool(ctx, ToolId.Ilspy)} -p -o "{w}\src_dummy" "{w}\il2cpp_dump\DummyDll\Assembly-CSharp.dll"`
               - libil2cpp.so는 ARM64 ELF라 REA로 열 수 없어. 관심 클래스의 함수 본문은 주석 달린 어셈블리로 봐줘 (몇 초면 끝나):
                 `{Tool(ctx, ToolId.Il2CppAsm)} --binary "{libil2cpp}" --dump "{w}\il2cpp_dump\dump.cs" --script-json "{w}\il2cpp_dump\script.json" --class <클래스명> --out "{w}\logs\<클래스명>.asm"`
               - 네임스페이스가 있는 클래스는 `네임스페이스.클래스명` 전체 이름을 주고, 메서드가 많아(80개 초과) 거절되거나 하나만 볼 땐 `--class` 대신 `--method "<클래스명>::<메서드명>"`을 여러 번 써줘. 후킹 후보(메서드명, RVA, 역할)는 '{w}\docs\hooks.md'에 표로 정리해줘.
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

        const string gadgetConfig = """{"interaction":{"type":"script","path":"/storage/emulated/0/Android/data/<패키지명>.mod/files/mod.js","on_change":"reload"}}""";

        return $"""
            0. APK 확보 및 버전 고정 확인 (파일이 없을 때만 추출)
               - 먼저 `{adb} devices`로 기기 목록을 확인해줘. USB로 연결된 실제 스마트폰(실기기)이 있으면 가급적 그 기기를 우선 사용하고, 연결된 실기기가 없다면 `bat\check_android_emulator_env.bat`로 CPU 가상화(VT)·녹스 설치/실행·ADB 연결을 확인해줘.
               - ⚠️ **ADB 단일 버전 유지**: 지정된 `{adb}`(`nox_adb` 등)와 다른 버전의 `adb`(PATH의 platform-tools, REA 내장 ADB 도구 등)를 섞어 쓰면 ADB 서버가 재시작되면서 연결이 끊기고 기기가 `offline`이 돼. 반드시 위에 지정된 `{adb}`만 쓰고 REA의 `install_adb_package` 등은 호출하지 마.
               - ⚠️ **버전 고정 안내**: Play 스토어에서 이 게임의 **자동 업데이트를 반드시 꺼 두라고** 먼저 안내해줘. 앱이 업데이트되면 `libil2cpp.so`가 바뀌어 분석한 RVA가 전부 무효가 돼.
               - 스마트폰(또는 녹스)에 직접 설치한 앱이라면 `{adb} shell pm list packages -3`로 패키지명을 찾고, `{adb} shell pm path <패키지명>`에 나온 base.apk와 split APK를 모두 `{adb} pull`로 '{w}\apk'에 가져와 원본 사본으로 보존해줘 (기기가 여러 대면 `-s <기기ID>` 지정, `/sdcard/Android/obb/<패키지명>`에 OBB 파일이 있는지도 함께 확인).
            1. 압축 해제
               `{Tar} -xf "{t}" -C "{unzipped}"` (폴더를 먼저 만들 것. Windows 내장 bsdtar — Git Bash의 GNU tar는 zip을 풀지 못함){split}
            {scriptStep}
            3. Java/네이티브 브리지 확인
               `{Tool(ctx, ToolId.Jadx)} -d "{w}\jadx_src" "{t}"`
               - AndroidManifest.xml의 패키지명, `versionCode`, UnityPlayerActivity 확장 여부, ContentProvider(`android:authorities`), 네이티브 플러그인/SDK 브리지를 확인해 '{w}\docs\analysis.md'에 정리해줘.
            4. 원본 실행 검증 (USB 연결 스마트폰 우선, 없으면 녹스 에뮬레이터)
               - `{adb} devices`로 확인하여 USB 연결된 실제 안드로이드 스마트폰이 있으면 해당 실기기를 우선 사용해줘.
               - 실기기가 연결되어 있지 않은 경우에만 녹스 에뮬레이터 포트(`{adb} connect 127.0.0.1:62001`, 다중 인스턴스는 62025부터)로 연결해줘.
               - 기기에 아직 설치되어 있지 않을 때만 `{adb} install -r "{t}"` (분할 패키지는 `install-multiple`)로 설치하고, 이미 Play 스토어 등에서 같은 버전이 깔려 있으면 덮어쓰지 마 (설치 출처만 바뀜). `{adb} logcat -s Unity`로 원본 런타임 로그를 확인해줘 (기기가 여러 대면 `-s <기기ID>` 지정).
               - ⚠️ **원본 패키지 위에 수정 APK를 `install -r`로 덮어쓰지 마**: 재서명한 APK는 원본과 서명이 달라 설치가 거부(`INSTALL_FAILED_UPDATE_INCOMPATIBLE`)되며, 원본을 지우고 깔면 로컬 세이브 데이터와 Google 로그인이 날아가. 모드는 반드시 아래 5단계의 **`.mod` 복제 앱**으로 만들어 원본 옆에 나란히 설치해줘.
            5. Frida Gadget 복제 앱(`.mod`) + `script` 모드 제작 및 배포
               - Android IL2CPP에서는 PC용 C# 모드 로더(MelonLoader·LemonLoader 등)가 동작하지 않는 경우가 많으므로, 루팅 없이도 PC 연결 없이 기기 단독으로 상시 동작하는 **Frida Gadget 복제 패키지(`<패키지명>.mod`) + `frida-il2cpp-bridge` 스크립트(`mod.js`)** 방식으로 모드를 제작해줘 (루팅 기기에서는 `frida-server`도 쓸 수 있지만, 세션 종료 시 훅이 풀리지 않게 하려면 Gadget `script` 모드가 표준이야):
               - **5-1. 복제 APK 디코드 및 매니페스트 `.mod` 분리 (원본 공존 · 세이브 보호)**
                 `{apktool} d -f -o "{w}\build\clone\decoded" "{t}"`
                 - `{w}\build\clone\decoded\AndroidManifest.xml`에서 `package` 속성, 모든 `android:authorities`, `android:taskAffinity`, 자체 정의 `.permission.` 이름, 푸시/SDK 스킴 host만 `<패키지명>.mod` 기준으로 바꿔 정품 앱과 ContentProvider·권한 충돌 없이 나란히 설치되게 해줘 (**주의: 액티비티/서비스 클래스명과 인텐트 액션 이름은 절대 바꾸지 마**).
               - **5-2. Frida Gadget 및 `script` 모드 설정 주입**
                 - 진입점 액티비티 Smali(예: `UnityPlayerActivity.smali`)의 `onCreate` 맨 앞(annotation 직후)에 `System.loadLibrary("frida-gadget")` 호출을 삽입해줘.
                 - `tools\frida-gadget\libfrida-gadget-arm64.so`를 `{w}\build\clone\decoded\lib\{abi}\libfrida-gadget.so`로 복사하고, 같은 폴더에 설정 파일 `libfrida-gadget.config.so`를 아래 JSON으로 작성해줘 (실행할 때마다 `mod.js`를 자동 로드하고, 파일을 다시 push하면 즉시 리로드됨):
                   `{gadgetConfig}`
                   (초기 탐색에서 PC 원격 콘솔 attach가 필요할 때만 일시적으로 `"type":"listen","address":"127.0.0.1","port":27042,"on_load":"wait"`를 쓰고 `{adb} forward tcp:27042 tcp:27042`로 붙어 확인해줘. 단, 앱이 백그라운드로 가면 세션이 끊기므로 본 모드는 `script` 모드로 전환할 것.)
               - **5-3. 리패키징 · 고정 키 서명 · 복제본 설치 · OBB 복사**
                 `{apktool} b "{w}\build\clone\decoded" -o "{w}\build\clone\out\rebuilt.unsigned.apk"`
                 `{signer} --apks "{w}\build\clone\out\rebuilt.unsigned.apk" -o "{w}\build\clone\out"`
                 - 항상 같은 키(uber-apk-signer 기본 디버그 키 또는 `{w}\build\clone\modtest.keystore`)로 서명해야 이후 재빌드 시 세이브를 유지한 채 `{adb} install -r "{w}\build\clone\out\rebuilt-aligned-debugSigned.apk"`로 덮어쓸 수 있어.
                 - **OBB 복사 (필수 확인)**: 원본 게임이 `/sdcard/Android/obb/<패키지명>/main.<versionCode>.<패키지명>.obb`를 사용한다면, 기기 안에서 복제 앱 경로로 복사해줘 (PC로 왕복 전송하지 말고 기기 내부에서 복사):
                   `{adb} shell "mkdir -p /sdcard/Android/obb/<패키지명>.mod && cp /sdcard/Android/obb/<패키지명>/main.<versionCode>.<패키지명>.obb /sdcard/Android/obb/<패키지명>.mod/main.<versionCode>.<패키지명>.mod.obb"`
               - **5-4. `mod.js` 프로젝트 작성 및 실기 배포 (`{w}\mod`)**
                 - '{w}\mod'에 `frida-compile` + `frida-il2cpp-bridge` 기반 TypeScript(또는 JS) 프로젝트를 구성해 `mod.js`로 번들링하고 기기에 배포해줘:
                   `{adb} push "{w}\mod\dist\mod.js" /sdcard/Android/data/<패키지명>.mod/files/mod.js`
                 - 커스텀 에셋 폴더를 넣을 때도 기기 내부 `cp -r`(권한 `2770` 문제로 앱에서 읽기 실패 가능) 대신 PC에서 `{adb} push`로 넣어줘.
                 - **스크립트 작성 시 실기 주의사항**:
                   1. `Process.attachModuleObserver`는 Android 링커(`linker_phdr.cpp`) SIGABRT 크래시를 유발할 수 있으므로 쓰지 말고, `setInterval`(5ms)로 `Process.findModuleByName("libil2cpp.so")`를 폴링해 대기할 것.
                   2. 원본을 호출한 뒤 반환값만 바꾸는 `Interceptor.attach` 훅에서 원본이 C# 예외(`Il2CppExceptionWrapper`)를 던지면 Frida 프레임을 넘지 못해 프로세스가 즉시 abort되므로, 예외 가능성이 있는 경로는 원본을 부르지 않는 `Interceptor.replace`를 쓸 것.
                   3. `script` 모드에서는 `console.log`가 `logcat`에 나오지 않으므로, `{adb} shell "mkdir -p /sdcard/Android/data/<패키지명>.mod/files && touch /sdcard/Android/data/<패키지명>.mod/files/mod.log && chmod 666 /sdcard/Android/data/<패키지명>.mod/files/mod.log"`로 로그 파일을 미리 만들어 두고(앱이 직접 생성하면 권한 `0600`이라 `adb shell`에서 못 읽음) 파일에 이어 쓰는 로거로 동작을 확인할 것 (`/proc/self/cmdline`에서 패키지명을 동적으로 읽을 때는 끝의 NUL 바이트 때문에 `File.readAllText`가 예외를 던지니 `File.readAllBytes` 사용).
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
        var asm = Tool(ctx, ToolId.Il2CppAsm);
        var nativeBin = isIos == false ? $@"{app}\Contents\Frameworks\GameAssembly.dylib" : $@"{app}\Frameworks\UnityFramework.framework\UnityFramework";
        var macNote = isIos == null ? $"\n   - macOS 앱이면 위 경로 대신 \"{app}\\Contents\\Frameworks\\GameAssembly.dylib\"를 써줘." : "";

        return $"""
            {unpack}
            2. 암호화 여부 확인 (REA `inspect_macho`는 macOS 전용이라 이 PC에서는 못 써)
               `{asm} --check-encryption "{nativeBin}"`
               - App Store에서 받은 iOS 실행 파일의 FairPlay 암호화 여부(cryptid)를 확인해줘.{macNote}
            3. 스크립트 코드 추출
               {extract}
               - IL2CPP 덤프 후에는 `{ilspy} -p -o "{w}\src_dummy" "{w}\il2cpp_dump\DummyDll\Assembly-CSharp.dll"`로 시그니처를 확인해줘.
            4. 로직 분석 (Mach-O라 REA로는 열 수 없어 — 주석 달린 어셈블리로 봐줘)
               `{asm} --binary "{nativeBin}" --dump "{w}\il2cpp_dump\dump.cs" --script-json "{w}\il2cpp_dump\script.json" --class <클래스명> --out "{w}\logs\<클래스명>.asm"`
               - 메서드 하나만 볼 땐 `--method "<클래스명>::<메서드명>"`. 유니버설(fat) 바이너리면 Il2CppDumper에서 고른 것과 같은 아키텍처를 `--arch arm64`(기본) / `--arch x64`로 지정해줘.
               - 핵심 로직과 후킹 후보(메서드명, RVA, 역할)를 '{w}\docs\hooks.md'에 정리해줘.
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

    /// <summary>네이티브 코드가 ELF(libil2cpp.so) · Mach-O(UnityFramework)라 Windows REA로 열 수 없는 타겟인지</summary>
    private static bool NativeIsNotPe(PromptContext ctx) => ctx.Profile.Kind is TargetKind.UnityApk or TargetKind.UnityApp;

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
