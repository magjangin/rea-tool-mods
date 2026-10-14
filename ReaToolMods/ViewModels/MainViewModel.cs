using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaToolMods.Models;
using ReaToolMods.Services;

namespace ReaToolMods.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const string PreferredReposRoot = @"H:\source\repos";

    private readonly List<TargetButtonViewModel> _targetButtons;
    private TargetProfile _profile = TargetCatalog.Get(TargetKind.Exe);
    private DetectionResult? _detection;
    private int _detectVersion;
    private string _lastGeneratedPrompt = "";

    // 사용자가 직접 고르거나 입력한 작업 폴더는 자동 네이밍으로 덮어쓰지 않음
    private bool _workDirPinned;
    // 코드에서 경로를 바꾸는 중인지 (사용자 입력과 구분)
    private bool _settingWorkDir;
    private bool _settingTarget;

    /// <summary>작업 폴더 상위 경로 (H 드라이브가 없으면 %USERPROFILE%\source\repos)</summary>
    public static string ReposRoot { get; } =
        Directory.Exists(Path.GetPathRoot(PreferredReposRoot))
            ? PreferredReposRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos");

    [ObservableProperty]
    public partial string Title { get; set; } = "REA Tool Mods";

    [ObservableProperty]
    public partial string ReaRepoUrl { get; set; } = "https://github.com/morluto/rea";

    [ObservableProperty]
    public partial string ReaReleasesUrl { get; set; } = "https://github.com/morluto/rea/releases";

    [ObservableProperty]
    public partial string TargetFilePath { get; set; } = "";

    [ObservableProperty]
    public partial string WorkDirPath { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedTargetCategory { get; set; } = "EXE";

    [ObservableProperty]
    public partial string PromptText { get; set; } = "";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "준비 완료";

    /// <summary>파일 내용 기반 자동 판별 결과 요약</summary>
    [ObservableProperty]
    public partial string DetectionSummary { get; set; } = "";

    /// <summary>선택한 타겟과 파일이 맞지 않을 때의 경고</summary>
    [ObservableProperty]
    public partial string ValidationWarning { get; set; } = "";

    /// <summary>작업 폴더 상태 (새 폴더 / 이어서 진행)</summary>
    [ObservableProperty]
    public partial string WorkspaceSummary { get; set; } = "";

    [ObservableProperty]
    public partial string EnvironmentSummary { get; set; } = "환경 점검을 실행하세요.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckEnvironmentCommand))]
    public partial bool IsCheckingEnvironment { get; set; }

    public IReadOnlyList<TargetButtonViewModel> GeneralTargets { get; }
    public IReadOnlyList<TargetButtonViewModel> UnityTargets { get; }
    public ObservableCollection<EnvironmentCheck> EnvironmentChecks { get; } = [];

    public MainViewModel()
    {
        _targetButtons = TargetCatalog.All.Select(p => new TargetButtonViewModel(p, SelectTarget)).ToList();
        GeneralTargets = _targetButtons.Where(b => !b.Profile.IsUnity).ToList();
        UnityTargets = _targetButtons.Where(b => b.Profile.IsUnity).ToList();

        SelectTarget(_profile);

        if (!Design.IsDesignMode)
        {
            _ = CheckEnvironmentAsync();
        }
    }

    // ───────────────────────── 링크 ─────────────────────────

    [RelayCommand]
    public void OpenGitHubRepo()
    {
        OpenShell(ReaRepoUrl);
        StatusMessage = "GitHub 저장소 페이지를 브라우저에서 열었습니다.";
    }

    [RelayCommand]
    public void OpenReleases()
    {
        OpenShell(ReaReleasesUrl);
        StatusMessage = "GitHub Releases 다운로드 페이지를 브라우저에서 열었습니다.";
    }

    // ───────────────────────── 대상 파일 / 작업 폴더 ─────────────────────────

    [RelayCommand]
    public async Task BrowseFileAsync()
    {
        try
        {
            if (MainWindow is not { } window) return;

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "분석 대상 파일 선택 (EXE, DLL, APK, APP/ASAR, Unity 게임)",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("분석 대상")
                    {
                        Patterns = ["*.exe", "*.dll", "*.apk", "*.xapk", "*.apks", "*.aab", "*.ipa", "*.asar", "*.zip", "*.dat"]
                    },
                    FilePickerFileTypes.All
                ]
            });

            if (files.Count > 0)
            {
                var path = files[0].Path.LocalPath;
                SetTarget(path);
                await LoadTargetAsync(path);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"파일 선택 중 오류 발생: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task BrowseFolderAsync()
    {
        try
        {
            if (MainWindow is not { } window) return;

            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "디컴파일 및 작업 폴더 선택",
                AllowMultiple = false
            });
            if (folders.Count == 0) return;

            var folder = folders[0].Path.LocalPath;
            SetWorkDir(folder);
            _workDirPinned = true;

            // 이전에 이 도구로 만든 작업 폴더면 대상 파일과 타겟 종류를 복원
            var meta = WorkspaceInspector.LoadMetadata(folder);
            if (meta != null && File.Exists(meta.TargetPath) && !SamePath(meta.TargetPath, CleanPath(TargetFilePath)))
            {
                SetTarget(meta.TargetPath);
                await LoadTargetAsync(meta.TargetPath, keepWorkDir: true, preferredKind: meta.Kind);
                StatusMessage = $"♻️ 이전 작업 정보를 복원했습니다: {Path.GetFileName(meta.TargetPath)} [{TargetCatalog.Get(meta.Kind).ShortName}]";
                return;
            }

            RefreshValidation();
            RegeneratePrompt();
            StatusMessage = $"작업 폴더 설정 완료: {WorkDirPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"폴더 선택 중 오류 발생: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenWorkDir()
    {
        if (Directory.Exists(WorkDirPath))
        {
            OpenShell(WorkDirPath);
            StatusMessage = $"작업 폴더를 열었습니다: {WorkDirPath}";
        }
        else
        {
            StatusMessage = "작업 폴더가 아직 없습니다. 프롬프트를 전송하면 생성됩니다.";
        }
    }

    partial void OnTargetFilePathChanged(string value)
    {
        if (_settingTarget) return;

        // 직접 입력/붙여넣기한 경로가 실제 파일이면 바로 판별
        var path = CleanPath(value);
        if (File.Exists(path) || (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path)))
        {
            _ = LoadTargetAsync(path);
        }
    }

    partial void OnWorkDirPathChanged(string value)
    {
        if (_settingWorkDir) return;
        _workDirPinned = true;
        RegeneratePrompt();
    }

    /// <summary>
    /// 대상 파일 내용을 판별해 타겟 버튼, 작업 폴더, 프롬프트를 갱신합니다.
    /// </summary>
    private async Task LoadTargetAsync(string path, bool keepWorkDir = false, TargetKind? preferredKind = null)
    {
        var version = ++_detectVersion;
        StatusMessage = $"🔎 대상 파일 분석 중: {Path.GetFileName(path)}";

        var detection = await Task.Run(() => TargetDetector.Detect(path));
        if (version != _detectVersion) return; // 그 사이 다른 파일이 선택됨

        if (!SamePath(detection.FilePath, path))
        {
            // 앱 실행 별칭처럼 실제 파일이 다른 곳에 있으면 대상 경로를 실제 파일로 바꿈
            path = detection.FilePath;
            SetTarget(path);
        }

        _detection = detection;
        if (!keepWorkDir) _workDirPinned = false;

        foreach (var b in _targetButtons) b.IsDetected = b.Profile.Kind == detection.Kind;

        var kindName = TargetCatalog.Get(detection.Kind).ShortName;
        DetectionSummary = $"🔎 자동 판별: {kindName}" +
                           (detection.Evidence.Count > 0 ? " — " + string.Join(" · ", detection.Evidence) : "");

        SelectTarget(TargetCatalog.Get(preferredKind ?? detection.Kind));
        ReplaceGameChecks(detection);

        var ws = WorkspaceInspector.Inspect(WorkDirPath, path);
        StatusMessage = ws.HasContent
            ? $"♻️ [{kindName}] 판별 완료 — 기존 작업 폴더를 찾았습니다. 이어서 진행하는 프롬프트를 만들었습니다: {WorkDirPath}"
            : $"[{kindName}] 판별 완료 — 작업 폴더: {WorkDirPath}";
    }

    // ───────────────────────── 타겟 선택 / 프롬프트 ─────────────────────────

    private void SelectTarget(TargetProfile profile)
    {
        _profile = profile;
        SelectedTargetCategory = profile.ShortName;
        foreach (var b in _targetButtons) b.IsSelected = b.Profile == profile;

        UpdateWorkDirPath();
        RefreshValidation();
        RegeneratePrompt();
        UpdateEnvironmentSummary();

        StatusMessage = string.IsNullOrEmpty(ValidationWarning)
            ? $"[{profile.ShortName}] 워크플로우 프롬프트가 적용되었습니다. (폴더: {Path.GetFileName(WorkDirPath)})"
            : $"[{profile.ShortName}] 프롬프트 적용 — 대상 파일 확인 필요 (아래 경고 참고)";
    }

    [RelayCommand]
    public void RegeneratePromptManually()
    {
        RefreshValidation();
        RegeneratePrompt();
        StatusMessage = "현재 대상/작업 폴더 상태로 프롬프트를 다시 생성했습니다.";
    }

    /// <param name="onlyIfUnmodified">true면 사용자가 프롬프트를 직접 고친 경우 덮어쓰지 않음</param>
    private void RegeneratePrompt(bool onlyIfUnmodified = false)
    {
        var target = CleanPath(TargetFilePath);
        var ws = WorkspaceInspector.Inspect(WorkDirPath, target);
        WorkspaceSummary = DescribeWorkspace(ws);

        if (onlyIfUnmodified && PromptText != _lastGeneratedPrompt) return;

        var ctx = new PromptContext(_profile, target, WorkDirPath, _detection, ws, ToolLocator.LocateAll());
        PromptText = _lastGeneratedPrompt = PromptBuilder.Build(ctx);
    }

    private void RefreshValidation()
    {
        var warnings = TargetDetector.Validate(_profile, CleanPath(TargetFilePath), _detection);
        ValidationWarning = string.Join(Environment.NewLine, warnings.Select(w => "⚠️ " + w));
    }

    /// <summary>
    /// 대상 파일 이름 + 타겟 접미사로 작업 폴더를 정합니다. 같은 대상의 기존 작업 폴더가 있으면 그 폴더를 이어서 씁니다.
    /// </summary>
    private void UpdateWorkDirPath()
    {
        if (_workDirPinned) return;

        var target = CleanPath(TargetFilePath);
        var baseName = "target";
        try
        {
            var name = Path.GetFileNameWithoutExtension(target.TrimEnd('\\', '/'));
            if (!string.IsNullOrWhiteSpace(name)) baseName = name;
        }
        catch { }

        var auto = Path.Combine(ReposRoot, baseName + _profile.FolderSuffix);
        SetWorkDir(Directory.Exists(auto) ? auto : FindExistingWorkspace(baseName, target) ?? auto);
    }

    /// <summary>
    /// 접미사가 다른 기존 작업 폴더(예: 예전에 -windows로 만든 Unity 게임 폴더) 중 같은 대상을 다루던 폴더를 찾습니다.
    /// </summary>
    private static string? FindExistingWorkspace(string baseName, string target)
    {
        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(ReposRoot)) return null;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(ReposRoot, baseName + "-*"))
            {
                var meta = WorkspaceInspector.LoadMetadata(dir);
                if (meta != null)
                {
                    if (SamePath(meta.TargetPath, target)) return dir;
                    continue;
                }

                // v1.3 이전 버전이 남긴 프롬프트 파일
                foreach (var legacy in new[] { "claude_prompt.md", "antigravity_prompt.md" })
                {
                    var file = Path.Combine(dir, legacy);
                    if (File.Exists(file) && File.ReadAllText(file).Contains(target, StringComparison.OrdinalIgnoreCase))
                        return dir;
                }
            }
        }
        catch { }

        return null;
    }

    private static string DescribeWorkspace(WorkspaceState ws)
    {
        if (!ws.Exists) return "🆕 새 작업 폴더 — 프롬프트 전송 시 생성되고, 에이전트가 PROGRESS.md로 진행 상황을 기록합니다.";
        if (!ws.HasContent) return "📂 빈 작업 폴더 — 여기서 새로 시작합니다.";

        var progress = ws.ProgressFile != null ? $", {Path.GetFileName(ws.ProgressFile)} 있음" : ", PROGRESS.md 없음";
        var summary = $"♻️ 기존 작업 폴더 (항목 {ws.TotalEntries}개{progress}) — '이어서 진행' 프롬프트로 생성됩니다.";
        if (ws.TargetDiffers) summary += " ⚠️ 다른 대상 파일로 만든 폴더입니다.";
        else if (ws.TargetChanged) summary += " ⚠️ 이전 작업 이후 대상 파일이 바뀌었습니다.";
        return summary;
    }

    // ───────────────────────── 환경 점검 ─────────────────────────

    [RelayCommand(CanExecute = nameof(CanCheckEnvironment))]
    public async Task CheckEnvironmentAsync()
    {
        IsCheckingEnvironment = true;
        EnvironmentSummary = "🔍 도구 · 플러그인 · 시스템 설정을 점검하는 중...";
        try
        {
            var results = await EnvironmentChecker.RunAsync(_detection);
            EnvironmentChecks.Clear();
            foreach (var r in results) EnvironmentChecks.Add(r);

            UpdateEnvironmentSummary();
            RegeneratePrompt(onlyIfUnmodified: true); // 새로 설치된 도구 경로 반영
        }
        catch (Exception ex)
        {
            EnvironmentSummary = $"환경 점검 중 오류: {ex.Message}";
        }
        finally
        {
            IsCheckingEnvironment = false;
        }
    }

    private bool CanCheckEnvironment() => !IsCheckingEnvironment;

    private void UpdateEnvironmentSummary()
    {
        if (EnvironmentChecks.Count == 0) return;

        var ok = EnvironmentChecks.Count(c => c.Status == CheckStatus.Ok);
        var warn = EnvironmentChecks.Count(c => c.Status == CheckStatus.Warning);
        var missing = EnvironmentChecks.Count(c => c.Status == CheckStatus.Missing);

        var tools = ToolLocator.LocateAll();
        var missingForTarget = _profile.RequiredTools.Select(id => tools[id]).Where(t => !t.Found).Select(t => t.DisplayName).ToList();
        var targetPart = missingForTarget.Count == 0
            ? $"[{_profile.ShortName}]에 필요한 도구 모두 준비됨"
            : $"[{_profile.ShortName}]에 필요한 도구 누락: {string.Join(", ", missingForTarget)}";

        EnvironmentSummary = $"✅ {ok}  ⚠️ {warn}  ❌ {missing}  ·  {targetPart}";
    }

    private void ReplaceGameChecks(DetectionResult detection)
    {
        for (var i = EnvironmentChecks.Count - 1; i >= 0; i--)
        {
            if (EnvironmentChecks[i].Group == EnvironmentChecker.GroupGame) EnvironmentChecks.RemoveAt(i);
        }

        if (EnvironmentChecks.Count == 0) return; // 아직 점검 전이면 점검 완료 시 함께 표시됨
        foreach (var c in EnvironmentChecker.GameChecks(detection)) EnvironmentChecks.Add(c);
        UpdateEnvironmentSummary();
    }

    [RelayCommand]
    public void RunToolDownloader() => RunBat("download_analysis_tools.bat");

    [RelayCommand]
    public void RunEmulatorCheck() => RunBat("check_android_emulator_env.bat");

    [RelayCommand]
    public void RunReaSetup() => RunBat("setup_rea_workflows.bat");

    private void RunBat(string fileName)
    {
        var path = ToolLocator.BatPath(fileName);
        if (path == null || !File.Exists(path))
        {
            StatusMessage = $"{fileName}을(를) 찾을 수 없습니다. (저장소의 bat 폴더 확인)";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path)!
            });
            StatusMessage = $"{fileName} 실행 — 끝나면 '환경 점검'을 다시 눌러 결과를 갱신하세요.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{fileName} 실행 실패: {ex.Message}";
        }
    }

    // ───────────────────────── 프롬프트 편집 / 전송 ─────────────────────────

    [RelayCommand]
    public void ClearPrompt()
    {
        PromptText = string.Empty;
        StatusMessage = "프롬프트 입력창이 초기화되었습니다.";
    }

    [RelayCommand]
    public async Task CopyPromptAsync()
    {
        if (string.IsNullOrWhiteSpace(PromptText))
        {
            StatusMessage = "복사할 프롬프트 내용이 없습니다.";
            return;
        }

        StatusMessage = await ClipboardService.SetTextAsync(PromptText)
            ? "프롬프트가 클립보드에 복사되었습니다! (에이전트에 바로 붙여넣기 가능)"
            : "클립보드 복사에 실패했습니다.";
    }

    [RelayCommand]
    public Task SendToClaudeAsync() => SendToAgentAsync(AgentApps.Claude);

    [RelayCommand]
    public Task SendToAntigravityAsync() => SendToAgentAsync(AgentApps.Antigravity);

    private async Task SendToAgentAsync(AgentApp app)
    {
        if (string.IsNullOrWhiteSpace(PromptText))
        {
            StatusMessage = "전송할 프롬프트 내용이 비어 있습니다.";
            return;
        }

        try
        {
            StatusMessage = await AgentLauncher.SendAsync(app, PromptText, WorkDirPath, CleanPath(TargetFilePath), _profile.Kind);
            WorkspaceSummary = DescribeWorkspace(WorkspaceInspector.Inspect(WorkDirPath, CleanPath(TargetFilePath)));
        }
        catch (Exception ex)
        {
            StatusMessage = $"{app.DisplayName} 연동 오류: {ex.Message}";
        }
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    private static Window? MainWindow =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

    private void SetTarget(string path)
    {
        _settingTarget = true;
        TargetFilePath = path;
        _settingTarget = false;
    }

    private void SetWorkDir(string path)
    {
        _settingWorkDir = true;
        WorkDirPath = path;
        _settingWorkDir = false;
    }

    private static string CleanPath(string? path) => (path ?? "").Trim().Trim('"');

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void OpenShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{target.Replace("&", "^&")}\"") { CreateNoWindow = true });
            }
        }
    }
}
