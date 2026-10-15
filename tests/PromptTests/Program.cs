using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReaToolMods.Models;
using ReaToolMods.Services;
using ReaToolMods.ViewModels;

// 프롬프트 테스트 — 생성된 프롬프트의 내용과, 프롬프트에 적힌 명령이 실제 도구에서 동작하는지 검증합니다.
//   A. 생성 매트릭스: 8종 타겟 × 5가지 작업 폴더 상태(new/empty/resume/changed/differs)
//   B. 실행: 프롬프트의 명령을 그대로 픽스처에 실행 (ilspycmd, bsdtar, Il2CppDumper, Cpp2IL, asar, jadx)
//   C. ViewModel 흐름: 자동 판별 → 작업 폴더 → 경고 → 이어하기 → 기존 폴더 재사용
// 사용법: dotnet run --project tests/PromptTests [-- <fixtureRoot> <outDir>]   (또는 bat\run_prompt_tests.bat)
// 픽스처는 가짜 바이너리라 덤프 자체는 실패하는 것이 정상이며, 인자 순서 · 옵션 파싱 · 단계 간 경로 일치만 확인합니다.
// C 단계는 ReposRoot(H:\source\repos)에 ReaToolPromptTest* 임시 폴더를 만들었다가 지웁니다.
var fixtureBase = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "reatool-prompttest", "fixtures");
var outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "reatool-prompttest", "out");
var results = new List<(string Area, string Name, string Status, string Detail)>();
void Check(string area, string name, bool ok, string detail = "") => results.Add((area, name, ok ? "PASS" : "FAIL", detail));
void Skip(string area, string name, string why) => results.Add((area, name, "SKIP", why));
bool Has(params ToolId[] ids) => ids.All(id => ToolLocator.LocateAll()[id].Found);
string Missing(params ToolId[] ids) =>
    "미설치: " + string.Join(", ", ids.Where(id => !ToolLocator.LocateAll()[id].Found).Select(id => ToolLocator.LocateAll()[id].DisplayName)) +
    @" → bat\download_analysis_tools.bat";

if (Directory.Exists(fixtureBase)) Directory.Delete(fixtureBase, true);
if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
Directory.CreateDirectory(outDir);
var fx = Path.Combine(fixtureBase, "Games With Space");   // 공백 경로로 따옴표 처리 검증
var work = Path.Combine(fixtureBase, "Work Dirs");
Directory.CreateDirectory(fx);
Directory.CreateDirectory(work);

var tools = ToolLocator.LocateAll();
var sysTar = "\"" + Path.Combine(Environment.SystemDirectory, "tar.exe") + "\"";
var nativeExe = @"C:\Windows\System32\where.exe";
var nativeDll = @"C:\Windows\System32\version.dll";
var managedDll = Path.Combine(AppContext.BaseDirectory, "CommunityToolkit.Mvvm.dll");

byte[] Meta(int v)
{
    var b = new byte[8192];
    new byte[] { 0xAF, 0x1B, 0xB1, 0xFA, (byte)v, 0, 0, 0 }.CopyTo(b, 0);
    return b;
}
void Put(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
void Copy(string src, string dst) { Directory.CreateDirectory(Path.GetDirectoryName(dst)!); File.Copy(src, dst, true); }
string Zip(string path, params (string Name, byte[] Data)[] entries)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var z = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var (n, d) in entries) { using var s = z.CreateEntry(n).Open(); s.Write(d); }
    return path;
}
var ggm = Encoding.ASCII.GetBytes("\0\0\0\0\0\0\0\x16\0\0\0\02022.3.10f1\0rest");

// ───────── fixtures ─────────
var il2Root = Path.Combine(fx, "IlGame");
var il2Exe = Path.Combine(il2Root, "IlGame.exe");
Copy(nativeExe, il2Exe);
Copy(nativeDll, Path.Combine(il2Root, "GameAssembly.dll"));
Copy(nativeDll, Path.Combine(il2Root, "UnityPlayer.dll"));
Put(Path.Combine(il2Root, "IlGame_Data", "il2cpp_data", "Metadata", "global-metadata.dat"), Meta(29));
Put(Path.Combine(il2Root, "IlGame_Data", "globalgamemanagers"), ggm);

var monoRoot = Path.Combine(fx, "MonoGame");
var monoExe = Path.Combine(monoRoot, "MonoGame.exe");
Copy(nativeExe, monoExe);
Copy(managedDll, Path.Combine(monoRoot, "MonoGame_Data", "Managed", "Assembly-CSharp.dll"));
Directory.CreateDirectory(Path.Combine(monoRoot, "MonoBleedingEdge"));

var libDll = Path.Combine(fx, "Libs", "Sample.Library.dll");
Copy(managedDll, libDll);

var unityApk = Zip(Path.Combine(fx, "unity game.apk"),
    ("classes.dex", [1]), ("lib/arm64-v8a/libil2cpp.so", [0x7F, 0x45, 0x4C, 0x46, 2, 1, 1, 0]), ("lib/arm64-v8a/libunity.so", [1]),
    ("assets/bin/Data/Managed/Metadata/global-metadata.dat", Meta(31)), ("assets/bin/Data/globalgamemanagers", ggm));

var ipa = Zip(Path.Combine(fx, "unity game.ipa"),
    ("Payload/Game.app/Frameworks/UnityFramework.framework/UnityFramework", [0xCF, 0xFA, 0xED, 0xFE]),
    ("Payload/Game.app/Data/Managed/Metadata/global-metadata.dat", Meta(29)));

var plainApk = Zip(Path.Combine(fx, "plain app.apk"), ("classes.dex", [1]), ("AndroidManifest.xml", [3, 0, 8, 0]));

// Electron: real asar built with the installed asar CLI
var electronRoot = Path.Combine(fx, "ElectronApp");
Copy(nativeExe, Path.Combine(electronRoot, "ElectronApp.exe"));
var asarSrc = Path.Combine(fixtureBase, "asar-src");
Put(Path.Combine(asarSrc, "package.json"), Encoding.UTF8.GetBytes("{\"name\":\"fixture\",\"main\":\"main.js\"}"));
Put(Path.Combine(asarSrc, "main.js"), Encoding.UTF8.GetBytes("console.log('fixture');"));
var asarFile = Path.Combine(electronRoot, "resources", "app.asar");
Directory.CreateDirectory(Path.GetDirectoryName(asarFile)!);
if (tools[ToolId.Asar].Found) Run($"\"{tools[ToolId.Asar].Path}\" pack \"{asarSrc}\" \"{asarFile}\"", 60);

var fixtures = new Dictionary<TargetKind, string>
{
    [TargetKind.Exe] = nativeExe,
    [TargetKind.Dll] = libDll,
    [TargetKind.Apk] = plainApk,
    [TargetKind.App] = Path.Combine(electronRoot, "ElectronApp.exe"),
    [TargetKind.UnityIl2Cpp] = il2Exe,
    [TargetKind.UnityMono] = monoExe,
    [TargetKind.UnityApk] = unityApk,
    [TargetKind.UnityApp] = ipa,
};

// ───────── A. 생성 매트릭스: 8종 × 5가지 작업 폴더 상태 ─────────
string[] states = ["new", "empty", "resume", "changed", "differs"];
foreach (var (kind, target) in fixtures)
{
    var profile = TargetCatalog.Get(kind);
    var detection = TargetDetector.Detect(target);
    Check("A.판별", $"{profile.ShortName} 픽스처 → {kind}", detection.Kind == kind, string.Join(" · ", detection.Evidence));

    foreach (var state in states)
    {
        var w = Path.Combine(work, $"{kind}-{state}");
        PrepareWorkDir(w, state, target);
        var ws = WorkspaceInspector.Inspect(w, target);
        var prompt = PromptBuilder.Build(new PromptContext(profile, target, w, detection, ws, tools));
        var name = $"{profile.ShortName} / {state}";
        var problems = ValidatePrompt(prompt, profile, target, w, state, detection);
        Check("A.생성", name, problems.Count == 0, string.Join("; ", problems));
        if (state is "new" or "resume") File.WriteAllText(Path.Combine(outDir, $"prompt-{kind}-{state}.md"), prompt);
    }
}

// 앱 실행 별칭(WindowsApps\*.exe, 0바이트 재분석 지점)은 열 수 없으므로 실제 exe로 바꿔 판별
var aliasDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
var alias = Directory.Exists(aliasDir)
    ? Directory.EnumerateFiles(aliasDir, "*.exe").FirstOrDefault(f => AppExecutionAlias.TryResolve(f, out var t) && File.Exists(t))
    : null;
if (alias != null)
{
    var ad = TargetDetector.Detect(alias);
    Check("A.판별", $"앱 실행 별칭 {Path.GetFileName(alias)} → 실제 exe",
        !ad.FilePath.Equals(alias, StringComparison.OrdinalIgnoreCase) && File.Exists(ad.FilePath) && ad.Warnings.Count == 0,
        $"{ad.FilePath} | {string.Join(" · ", ad.Evidence)} | {string.Join(" · ", ad.Warnings)}");
}
else
{
    Skip("A.판별", "앱 실행 별칭 → 실제 exe", @"%LOCALAPPDATA%\Microsoft\WindowsApps에 별칭 없음");
}

List<string> ValidatePrompt(string p, TargetProfile profile, string target, string w, string state, DetectionResult d)
{
    var errs = new List<string>();
    void Need(bool cond, string msg) { if (!cond) errs.Add(msg); }

    Need(p.StartsWith($"# [{profile.ShortName}]"), "머리말 타겟명");
    Need(p.Contains($"- 대상 파일: {target}"), "대상 파일 경로");
    Need(p.Contains($"- 작업 디렉터리: {w}"), "작업 디렉터리");
    Need(p.Contains("## 0. 작업 폴더 상태") && p.Contains("## 사용할 도구") && p.Contains("## 수행 지침") &&
         p.Contains("## 공통 규칙") && p.Contains("## 최종 보고"), "필수 섹션");
    Need(p.Contains("범위: 로컬·싱글플레이"), "범위 규칙");
    var hasGuardrails = p.Contains("## 시작 전 확인 (Unity 게임 가드레일)");
    if (profile.IsUnity)
    {
        Need(hasGuardrails && p.IndexOf("## 시작 전 확인", StringComparison.Ordinal) < p.IndexOf("## 수행 지침", StringComparison.Ordinal),
            "Unity 가드레일이 수행 지침보다 앞에 있어야 함");
        Need(p.Contains("구매 내역") && p.Contains("구조 → 덤프 → 웹 검색") && p.Contains("클로닝 금지"), "Unity 가드레일 핵심 항목");
    }
    else
        Need(!hasGuardrails, "Unity가 아닌 타겟에 Unity 가드레일 노출");
    Need(!Regex.IsMatch(p, @"\{[A-Za-z_]+\}"), "치환 안 된 {변수}");
    Need(!p.Contains("ReaToolMods.") && !p.Contains("System.Collections"), "객체 ToString 노출");

    switch (state)
    {
        case "new": Need(p.Contains("폴더가 아직 없습니다"), "새 폴더 문구"); Need(!p.Contains("기존 작업에 이어서"), "새 폴더인데 이어하기 문구"); break;
        case "empty": Need(p.Contains("폴더가 비어 있습니다"), "빈 폴더 문구"); break;
        case "resume":
            Need(p.Contains("**기존 작업에 이어서**"), "이어하기 문구");
            Need(p.Contains("`PROGRESS.md`과 기존 산출물을 읽고"), "PROGRESS.md 먼저 읽기");
            Need(p.Contains("[진행중] 2. 시그니처 탐색"), "PROGRESS 최근 기록 첨부");
            Need(p.Contains("src_v2"), "덮어쓰기 금지 지시");
            Need(!p.Contains("대상 파일이 바뀌었습니다") && !p.Contains("분석용으로 만들어졌습니다"), "변경 경고 오탐");
            break;
        case "changed": Need(p.Contains("대상 파일이 바뀌었습니다"), "대상 변경 경고"); break;
        case "differs": Need(p.Contains("분석용으로 만들어졌습니다"), "다른 대상 경고"); break;
    }

    // 도구 섹션: 필요한 도구가 모두 나오고, 단계에서 쓰는 도구는 필요한 도구 목록에 있어야 함
    var stepsPart = p[p.IndexOf("## 수행 지침", StringComparison.Ordinal)..p.IndexOf("## 공통 규칙", StringComparison.Ordinal)];
    foreach (var id in profile.RequiredTools)
        Need(p.Contains($"- {tools[id].DisplayName} (`{tools[id].CommandName}`)"), $"도구 목록에 {tools[id].DisplayName} 없음");
    foreach (var t in tools.Values)
    {
        if (stepsPart.Contains($"`{t.CommandName} ") && !profile.RequiredTools.Contains(t.Id))
            errs.Add($"단계에서 {t.CommandName} 사용하지만 필요 도구 목록에 없음");
    }

    // 판별 정보가 있으면 자리표시자가 남지 않아야 함
    if (profile.Kind is TargetKind.UnityIl2Cpp or TargetKind.UnityMono)
        Need(!p.Contains("<게임"), "게임 경로 자리표시자 남음");
    if (profile.Kind == TargetKind.UnityApp)
        Need(!Regex.IsMatch(stepsPart, "Il2CppDumper \"<앱>"), ".app 경로 자리표시자 남음");
    if (profile.Kind == TargetKind.UnityApk && d.UnityVersion != null)
        Need(stepsPart.Contains($"--force-unity-version \"{Regex.Match(d.UnityVersion, @"^\d+\.\d+\.\d+").Value}\""), "Cpp2IL에 숫자 형식 Unity 버전 전달");
    return errs;
}

void PrepareWorkDir(string w, string state, string target)
{
    if (state == "new") return;
    Directory.CreateDirectory(w);
    if (state == "empty") return;
    Directory.CreateDirectory(Path.Combine(w, "il2cpp_dump"));
    File.WriteAllText(Path.Combine(w, "PROGRESS.md"), "# Progress\n- [완료] 1. 덤프 — il2cpp_dump\\\n- [진행중] 2. 시그니처 탐색\n");
    var info = new FileInfo(target);
    var meta = new WorkspaceMetadata(
        state == "differs" ? Path.Combine(fx, "Other", "Other.exe") : target,
        TargetKind.Exe,
        state == "changed" ? info.Length + 1 : info.Length,
        info.LastWriteTimeUtc, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(-1));
    Directory.CreateDirectory(Path.Combine(w, ".reatool"));
    File.WriteAllText(Path.Combine(w, ".reatool", "workspace.json"), JsonSerializer.Serialize(meta));
}

// ───────── B. 프롬프트의 명령을 그대로 실행 ─────────
string Prompt(TargetKind kind, string target, string w) =>
    PromptBuilder.Build(new PromptContext(TargetCatalog.Get(kind), target, w, TargetDetector.Detect(target), WorkspaceState.Missing, tools));

// 프롬프트에서 `도구이름 ...` 형태의 명령을 찾아 도구 이름을 실제 경로로 바꿔 줌
string? Command(string prompt, ToolId? id, string commandName)
{
    foreach (Match m in Regex.Matches(prompt, "`([^`]+)`"))
    {
        var cmd = m.Groups[1].Value;
        if (!cmd.StartsWith(commandName + " ")) continue;
        return id is { } t ? $"\"{tools[t].Path}\"" + cmd[commandName.Length..] : cmd;
    }
    return null;
}
List<string> QuotedArgs(string cmd) => Regex.Matches(cmd, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

// B1. DLL — ilspycmd 프로젝트 복원
if (Has(ToolId.Ilspy))
{
    var w = Path.Combine(work, "B1 dll");
    var cmd = Command(Prompt(TargetKind.Dll, libDll, w), ToolId.Ilspy, "ilspycmd");
    var (code, output) = Run(cmd!, 180);
    var csproj = Directory.Exists(Path.Combine(w, "src")) ? Directory.GetFiles(Path.Combine(w, "src"), "*.csproj", SearchOption.AllDirectories) : [];
    var cs = Directory.Exists(Path.Combine(w, "src")) ? Directory.GetFiles(Path.Combine(w, "src"), "*.cs", SearchOption.AllDirectories).Length : 0;
    Check("B.실행", "DLL: ilspycmd -p -o src -r <dir> <dll>", code == 0 && csproj.Length > 0 && cs > 0, $"exit={code}, csproj={csproj.Length}, .cs={cs} {Tail(output)}");
}
else Skip("B.실행", "DLL: ilspycmd", Missing(ToolId.Ilspy));

// B2. Unity Mono — Managed 참조 경로 포함 디컴파일
if (Has(ToolId.Ilspy))
{
    var w = Path.Combine(work, "B2 mono");
    var cmd = Command(Prompt(TargetKind.UnityMono, monoExe, w), ToolId.Ilspy, "ilspycmd");
    var (code, output) = Run(cmd!, 180);
    var cs = Directory.Exists(Path.Combine(w, "src")) ? Directory.GetFiles(Path.Combine(w, "src"), "*.cs", SearchOption.AllDirectories).Length : 0;
    Check("B.실행", "Unity Mono: ilspycmd (Managed\\Assembly-CSharp.dll)", code == 0 && cs > 0, $"exit={code}, .cs={cs} {Tail(output)}");
}
else Skip("B.실행", "Unity Mono: ilspycmd", Missing(ToolId.Ilspy));

// B3. Unity APK — tar 압축 해제 후, 다음 단계(Il2CppDumper)가 가리키는 경로가 실제로 존재하는지
if (Has(ToolId.Il2CppDumper, ToolId.Cpp2Il))
{
    var w = Path.Combine(work, "B3 unity apk");
    var prompt = Prompt(TargetKind.UnityApk, unityApk, w);
    Directory.CreateDirectory(Path.Combine(w, "apk_unzipped"));
    var tar = Command(prompt, null, sysTar)!;
    var (code, output) = Run(tar, 60);
    Check("B.실행", "Unity APK: tar.exe(bsdtar) -xf <apk> -C apk_unzipped", code == 0, $"exit={code} {Tail(output)}");

    var dumpCmd = Command(prompt, ToolId.Il2CppDumper, "Il2CppDumper")!;
    var a = QuotedArgs(dumpCmd);   // [tool, libil2cpp.so, metadata, out]
    Check("B.경로", "Unity APK: 덤프 입력 경로 = 압축 해제 결과", File.Exists(a[1]) && File.Exists(a[2]), $"{File.Exists(a[1])}/{File.Exists(a[2])}");

    var (dcode, dout) = Run(dumpCmd, 60);
    Check("B.실행", "Unity APK: Il2CppDumper 인자 순서 (가짜 바이너리라 덤프 자체는 실패가 정상)",
        dout.Contains("Initializing metadata") && dout.Contains("Metadata Version: 31"), Tail(dout, 160));

    var cppCmd = Command(prompt, ToolId.Cpp2Il, "Cpp2IL")!;
    var (ccode, cout) = Run(cppCmd, 90);
    Check("B.실행", "Unity APK: Cpp2IL 옵션 파싱 통과 (가짜 바이너리라 이후 단계 실패는 정상)",
        cout.Contains("Using force options") && !cout.Contains("GetRuntimeOptionsFromCommandLine") && !cout.Contains("ERROR(S)"), FirstError(cout));
}
else Skip("B.실행", "Unity APK: bsdtar / Il2CppDumper / Cpp2IL", Missing(ToolId.Il2CppDumper, ToolId.Cpp2Il));

// B4. Unity IL2CPP PC — Il2CppDumper / Cpp2IL 명령
if (Has(ToolId.Il2CppDumper, ToolId.Cpp2Il))
{
    var w = Path.Combine(work, "B4 il2cpp pc");
    var prompt = Prompt(TargetKind.UnityIl2Cpp, il2Exe, w);
    var dumpCmd = Command(prompt, ToolId.Il2CppDumper, "Il2CppDumper")!;
    var a = QuotedArgs(dumpCmd);
    Check("B.경로", "IL2CPP: GameAssembly.dll / global-metadata.dat 경로 존재", File.Exists(a[1]) && File.Exists(a[2]), $"{a[1]} | {a[2]}");
    var (dcode, dout) = Run(dumpCmd, 60);
    Check("B.실행", "IL2CPP: Il2CppDumper 인자 순서 (가짜 바이너리라 덤프 자체는 실패가 정상)",
        dout.Contains("Initializing metadata") && dout.Contains("Metadata Version: 29") && dout.Contains("Initializing il2cpp file"), Tail(dout, 160));

    var cppCmd = Command(prompt, ToolId.Cpp2Il, "Cpp2IL")!;
    var (ccode, cout) = Run(cppCmd, 90);
    Check("B.실행", "IL2CPP: Cpp2IL 옵션 파싱 통과 (가짜 바이너리라 이후 단계 실패는 정상)",
        !cout.Contains("GetRuntimeOptionsFromCommandLine") && !cout.Contains("ERROR(S)") && cout.Contains("MainWithArgs"), FirstError(cout));
}
else Skip("B.실행", "IL2CPP: Il2CppDumper / Cpp2IL", Missing(ToolId.Il2CppDumper, ToolId.Cpp2Il));

// B5. APP — asar extract
{
    var w = Path.Combine(work, "B5 electron");
    var cmd = Command(Prompt(TargetKind.App, fixtures[TargetKind.App], w), ToolId.Asar, "asar");
    if (cmd == null || !tools[ToolId.Asar].Found) Skip("B.실행", "APP: asar extract", "asar 미설치");
    else
    {
        var (code, output) = Run(cmd, 60);
        Check("B.실행", "APP: asar extract <resources\\app.asar> extracted", code == 0 && File.Exists(Path.Combine(w, "extracted", "main.js")), $"exit={code} {Tail(output)}");
    }
}

// B6. Unity APP (IPA) — 압축 해제 후 iOS 덤프 명령의 경로가 맞는지
if (Has(ToolId.Il2CppDumper))
{
    var w = Path.Combine(work, "B6 ipa");
    var prompt = Prompt(TargetKind.UnityApp, ipa, w);
    Directory.CreateDirectory(Path.Combine(w, "unpacked"));
    var (code, output) = Run(Command(prompt, null, sysTar)!, 60);
    var dumpCmd = Command(prompt, ToolId.Il2CppDumper, "Il2CppDumper")!;
    var a = QuotedArgs(dumpCmd);
    Check("B.경로", "Unity APP: IPA 압축 해제 후 UnityFramework / metadata 경로 존재", code == 0 && File.Exists(a[1]) && File.Exists(a[2]), $"{a[1]}");
}
else Skip("B.실행", "Unity APP: IPA 압축 해제 경로", Missing(ToolId.Il2CppDumper));

// B7. APK — JADX / Apktool (Java 필요). 가짜 APK라 디컴파일 자체는 실패가 정상이고, 인자 파싱만 확인
if (Has(ToolId.Java, ToolId.Jadx, ToolId.Apktool))
{
    var w = Path.Combine(work, "B7 apk");
    var prompt = Prompt(TargetKind.Apk, plainApk, w);
    foreach (var (id, name) in new[] { (ToolId.Jadx, "jadx"), (ToolId.Apktool, "apktool") })
    {
        var (c, o) = Run(Command(prompt, id, name)!, 180);
        var argsOk = !Regex.IsMatch(o, "unknown option|unrecognized option|usage:|is not recognized|JAVA_HOME|TIMEOUT", RegexOptions.IgnoreCase);
        Check("B.실행", $"APK: {name} 인자 파싱 (가짜 APK라 디컴파일 자체는 실패가 정상)", argsOk, Tail(o, 160));
    }
}
else
{
    Skip("B.실행", "APK: jadx -d / apktool d", tools[ToolId.Java].Found
        ? Missing(ToolId.Jadx, ToolId.Apktool)
        : "Java 미설치 — JDK 17+ 설치 후 JAVA_HOME 확인, 새 터미널에서 다시 실행");
}
Skip("B.실행", "APK/Unity APK: adb install · pm path · logcat", @"녹스·기기 연결이 필요해 자동 테스트하지 않음 (bat\check_android_emulator_env.bat로 확인)");

// ───────── C. ViewModel 흐름 ─────────
var vm = new MainViewModel();
await Until(() => !vm.IsCheckingEnvironment && vm.EnvironmentChecks.Count > 0, 60);

vm.TargetFilePath = il2Exe;   // 직접 입력과 같은 경로
await Until(() => vm.SelectedTargetCategory == "Unity IL2CPP" && vm.StatusMessage.Contains("판별 완료"), 20);
Check("C.VM", "경로 입력 → 자동 판별 Unity IL2CPP + 버튼 선택/🔎 표시",
    vm.SelectedTargetCategory == "Unity IL2CPP" && vm.UnityTargets[0].IsSelected && vm.UnityTargets[0].IsDetected, vm.DetectionSummary);
Check("C.VM", "작업 폴더 자동 이름 = <ReposRoot>\\IlGame-unity-il2cpp",
    vm.WorkDirPath == Path.Combine(MainViewModel.ReposRoot, "IlGame-unity-il2cpp"), vm.WorkDirPath);
Check("C.VM", "프롬프트에 판별된 GameAssembly 경로", vm.PromptText.Contains(Path.Combine(il2Root, "GameAssembly.dll")), "");

vm.UnityTargets[1].SelectCommand.Execute(null);   // Mono 버튼
Check("C.VM", "Mono 버튼 → 판별 결과 불일치 경고 + 폴더 접미사 변경",
    vm.ValidationWarning.Contains("자동 판별 결과는 [Unity IL2CPP]") && vm.WorkDirPath.EndsWith("-unity-mono"), vm.ValidationWarning);

vm.UnityTargets[2].SelectCommand.Execute(null);   // Unity APK 버튼
Check("C.VM", "Unity APK 버튼 + .exe → 확장자 경고", vm.ValidationWarning.Contains("'.exe' 파일이 맞지 않습니다"), vm.ValidationWarning);

var resumeDir = Path.Combine(work, "UnityIl2Cpp-resume");
vm.UnityTargets[0].SelectCommand.Execute(null);
vm.WorkDirPath = resumeDir;   // 사용자가 직접 입력
Check("C.VM", "작업 폴더 직접 입력 → 이어하기 프롬프트", vm.PromptText.Contains("**기존 작업에 이어서**") && vm.WorkspaceSummary.Contains("기존 작업 폴더"), vm.WorkspaceSummary);
vm.GeneralTargets[0].SelectCommand.Execute(null);  // EXE 버튼
Check("C.VM", "직접 고른 작업 폴더는 타겟을 바꿔도 유지", vm.WorkDirPath == resumeDir, vm.WorkDirPath);

vm.PromptText = "사용자가 직접 고친 프롬프트";
await vm.CheckEnvironmentAsync();
Check("C.VM", "환경 재점검이 사용자가 고친 프롬프트를 덮어쓰지 않음", vm.PromptText == "사용자가 직접 고친 프롬프트", "");
vm.RegeneratePromptManually();
Check("C.VM", "🔄 다시 생성은 덮어씀", vm.PromptText.StartsWith("# [EXE]"), "");

if (alias != null)
{
    var vmAlias = new MainViewModel();
    vmAlias.TargetFilePath = alias;
    await Until(() => vmAlias.StatusMessage.Contains("판별 완료"), 20);
    Check("C.VM", "앱 실행 별칭 입력 → 대상 경로가 실제 exe로 바뀌고 오류 경고 없음",
        File.Exists(vmAlias.TargetFilePath) && !vmAlias.TargetFilePath.Equals(alias, StringComparison.OrdinalIgnoreCase) &&
        !vmAlias.ValidationWarning.Contains("판별 중 오류") && vmAlias.PromptText.Contains($"- 대상 파일: {vmAlias.TargetFilePath}"),
        $"{vmAlias.TargetFilePath} | {vmAlias.ValidationWarning}");
}

// 접미사가 다른 기존 폴더 재사용 (v1.3 폴더 / workspace.json 폴더) — ReposRoot에 임시 폴더를 만들고 정리
var unique = "ReaToolPromptTest" + Guid.NewGuid().ToString("N")[..6];
var legacyExeDir = Path.Combine(fx, unique);
var legacyExe = Path.Combine(legacyExeDir, unique + ".exe");
Copy(nativeExe, legacyExe);
Copy(nativeDll, Path.Combine(legacyExeDir, "GameAssembly.dll"));
Put(Path.Combine(legacyExeDir, unique + "_Data", "il2cpp_data", "Metadata", "global-metadata.dat"), Meta(29));
var legacyWork = Path.Combine(MainViewModel.ReposRoot, unique + "-windows");
var metaExe = Path.Combine(fx, "Libs", unique + "Lib.dll");
Copy(managedDll, metaExe);
var metaWork = Path.Combine(MainViewModel.ReposRoot, unique + "Lib-oldsuffix");
try
{
    Directory.CreateDirectory(legacyWork);
    File.WriteAllText(Path.Combine(legacyWork, "claude_prompt.md"), $"- 대상 바이너리: {legacyExe}\n");
    var vm2 = new MainViewModel();
    vm2.TargetFilePath = legacyExe;
    await Until(() => vm2.StatusMessage.Contains("판별 완료"), 20);
    Check("C.VM", "v1.3 폴더(<이름>-windows, claude_prompt.md) 재사용", vm2.WorkDirPath == legacyWork, vm2.WorkDirPath);

    WorkspaceInspector.SaveMetadata(metaWork, metaExe, TargetKind.Dll);
    vm2.TargetFilePath = metaExe;
    await Until(() => vm2.StatusMessage.Contains("판별 완료") && vm2.SelectedTargetCategory == "DLL", 20);
    Check("C.VM", ".reatool/workspace.json이 같은 대상을 가리키는 폴더 재사용", vm2.WorkDirPath == metaWork, vm2.WorkDirPath);
}
finally
{
    foreach (var d in new[] { legacyWork, metaWork }) if (Directory.Exists(d)) Directory.Delete(d, true);
}
Skip("C.VM", "작업 폴더 선택 시 이전 대상 복원", "폴더 선택 대화상자(UI) 필요 — 같은 로직(LoadMetadata)은 위 재사용 테스트로 확인");

// ───────── 결과 ─────────
var report = new StringBuilder();
report.AppendLine("| 영역 | 항목 | 결과 | 비고 |");
report.AppendLine("|---|---|---|---|");
foreach (var r in results)
    report.AppendLine($"| {r.Area} | {r.Name} | {(r.Status == "PASS" ? "✅" : r.Status == "FAIL" ? "❌" : "⏭️")} {r.Status} | {r.Detail.Replace("|", "/").Replace("\n", " ")} |");
File.WriteAllText(Path.Combine(outDir, "report.md"), report.ToString());

foreach (var r in results.Where(r => r.Status != "PASS" || r.Area != "A.생성"))
    Console.WriteLine($"[{r.Status}] {r.Area} | {r.Name} | {(r.Detail.Length > 220 ? r.Detail[..220] + "…" : r.Detail)}");
Console.WriteLine($"\n상세 결과 · 샘플 프롬프트: {outDir}");
Console.WriteLine($"TOTAL: {results.Count(r => r.Status == "PASS")} pass, {results.Count(r => r.Status == "FAIL")} fail, {results.Count(r => r.Status == "SKIP")} skip");
return results.Any(r => r.Status == "FAIL") ? 1 : 0;

// ───────── helpers ─────────
static (int Code, string Output) Run(string commandLine, int timeoutSec)
{
    var psi = new ProcessStartInfo("cmd.exe", $"/d /s /c \"{commandLine}\"")
    {
        RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
        UseShellExecute = false, CreateNoWindow = true
    };
    psi.Environment["NO_COLOR"] = "1";
    using var p = Process.Start(psi)!;
    p.StandardInput.Close();
    var o = p.StandardOutput.ReadToEndAsync();
    var e = p.StandardError.ReadToEndAsync();
    if (!p.WaitForExit(timeoutSec * 1000)) { p.Kill(true); return (-1, "TIMEOUT " + o.Result + e.Result); }
    return (p.ExitCode, o.Result + e.Result);
}

static string FirstError(string s) =>
    s.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("Exception") || l.StartsWith("[Error]") || l.StartsWith("ERROR")) ?? "(오류 없음)";

static string Tail(string s, int n = 120)
{
    s = Regex.Replace(s, @"\s+", " ").Trim();
    return s.Length > n ? "…" + s[^n..] : s;
}

static async Task Until(Func<bool> cond, int timeoutSec)
{
    var end = DateTime.Now.AddSeconds(timeoutSec);
    while (!cond() && DateTime.Now < end) await Task.Delay(100);
}
