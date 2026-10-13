using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaToolMods.Services;

namespace ReaToolMods.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Title { get; set; } = "REA Tool Mods";

    [ObservableProperty]
    public partial string ReaRepoUrl { get; set; } = "https://github.com/morluto/rea";

    [ObservableProperty]
    public partial string ReaReleasesUrl { get; set; } = "https://github.com/morluto/rea/releases";

    [ObservableProperty]
    public partial string TargetFilePath { get; set; } = "";

    [ObservableProperty]
    public partial string WorkDirPath { get; set; } = @"H:\source\repos\target-windows";

    [ObservableProperty]
    public partial string SelectedTargetCategory { get; set; } = "EXE";

    [ObservableProperty]
    public partial string PromptText { get; set; } = "";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "준비 완료";

    public MainViewModel()
    {
        ApplyPreset("exe");
    }

    [RelayCommand]
    public void OpenGitHubRepo()
    {
        OpenUrl(ReaRepoUrl);
        StatusMessage = "GitHub 저장소 페이지를 브라우저에서 열었습니다.";
    }

    [RelayCommand]
    public void OpenReleases()
    {
        OpenUrl(ReaReleasesUrl);
        StatusMessage = "GitHub Releases 다운로드 페이지를 브라우저에서 열었습니다.";
    }

    /// <summary>
    /// 대상 파일 선택 다이얼로그 (StorageProvider)
    /// </summary>
    [RelayCommand]
    public async Task BrowseFileAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null)
            {
                var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "분석 대상 파일 선택 (EXE, DLL, APK, APP/ASAR)",
                    AllowMultiple = false
                });

                if (files != null && files.Count > 0)
                {
                    TargetFilePath = files[0].Path.LocalPath;
                    AutoDetectTargetCategory(TargetFilePath);
                    UpdateWorkDirPath(SelectedTargetCategory);
                    ApplyPreset(SelectedTargetCategory.ToLowerInvariant());
                    StatusMessage = $"대상 파일 및 작업 폴더 설정 완료: {Path.GetFileName(TargetFilePath)}";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"파일 선택 중 오류 발생: {ex.Message}";
        }
    }

    /// <summary>
    /// 작업 폴더 수동 선택 다이얼로그 (StorageProvider)
    /// </summary>
    [RelayCommand]
    public async Task BrowseFolderAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null)
            {
                var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "디컴파일 및 작업 폴더 선택",
                    AllowMultiple = false
                });

                if (folders != null && folders.Count > 0)
                {
                    WorkDirPath = folders[0].Path.LocalPath;
                    ApplyPreset(SelectedTargetCategory.ToLowerInvariant());
                    StatusMessage = $"작업 폴더 설정 완료: {WorkDirPath}";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"폴더 선택 중 오류 발생: {ex.Message}";
        }
    }

    /// <summary>
    /// 타겟 확장자별 구분 접미사 반환 (예: -windows, -dotnet, -android, -electron)
    /// </summary>
    private static string GetSuffixForCategory(string category) => category.ToLowerInvariant() switch
    {
        "exe" => "-windows",
        "dll" => "-dotnet",
        "apk" => "-android",
        "app" => "-electron",
        _ => "-workspace"
    };

    /// <summary>
    /// 대상 파일의 기본 이름(확장자 제외)과 타겟 구분을 조합하여 작업 폴더 자동 생성
    /// (예: H:\source\repos\Assembly-CSharp-dotnet, H:\source\repos\MyGame-windows)
    /// </summary>
    private void UpdateWorkDirPath(string? category = null)
    {
        var cat = category ?? SelectedTargetCategory;
        var suffix = GetSuffixForCategory(cat);

        string baseName = "target";
        if (!string.IsNullOrWhiteSpace(TargetFilePath))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(TargetFilePath);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    baseName = name;
                }
            }
            catch { }
        }

        WorkDirPath = Path.Combine(@"H:\source\repos", $"{baseName}{suffix}");
    }

    /// <summary>
    /// 파일 확장자 기반 4대 타겟(EXE, DLL, APK, APP) 자동 추론
    /// </summary>
    private void AutoDetectTargetCategory(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ext == ".apk" || ext == ".aab")
        {
            SelectedTargetCategory = "APK";
        }
        else if (ext == ".dll")
        {
            SelectedTargetCategory = "DLL";
        }
        else if (ext == ".app" || ext == ".asar" || ext == ".zip")
        {
            SelectedTargetCategory = "APP";
        }
        else
        {
            SelectedTargetCategory = "EXE";
        }
    }

    /// <summary>
    /// 4대 타겟(EXE, DLL, APK, APP) 디컴파일 및 실행 워크플로우 프롬프트 프리셋
    /// </summary>
    [RelayCommand]
    public void ApplyPreset(string? category)
    {
        var cat = (category ?? "exe").ToLowerInvariant();
        SelectedTargetCategory = cat.ToUpperInvariant();
        
        UpdateWorkDirPath(SelectedTargetCategory);

        var targetInfo = string.IsNullOrWhiteSpace(TargetFilePath) 
            ? @"[분석 대상 파일 경로를 입력하거나 선택하세요]" 
            : TargetFilePath;
        var workDir = string.IsNullOrWhiteSpace(WorkDirPath)
            ? @"H:\source\repos\target-windows"
            : WorkDirPath;

        switch (cat)
        {
            case "exe":
                PromptText = 
$@"# [EXE 역공학 및 디컴파일/실행 워크플로우 요청]
- 대상 바이너리: {targetInfo}
- 작업 디렉터리: {workDir}

[수행 지침]
1. 작업 폴더 생성:
   - '{workDir}' 디렉터리가 없으면 새로 생성하고 작업 환경을 초기화해줘.
2. 디컴파일 및 의사코드 덤프:
   - REA 또는 정적 분석 도구를 사용하여 대상 바이너리의 함수 심볼, Import/Export 테이블, 의사코드(Pseudocode)를 '{workDir}\decompiled'에 추출해줘.
   - 메인 엔트리포인트와 핵심 로직 제어 흐름을 명세서로 정리해줘.
3. 스켈레톤 복원 및 실행 테스트:
   - 바이너리의 핵심 동작을 재현/테스트할 수 있는 C/C++ 또는 C# 실행 스켈레톤 프로젝트를 '{workDir}' 내에 생성해줘.
   - 빌드 후 직접 실행하여 결과를 검증하고 로그를 출력해줘.";
                break;

            case "dll":
                PromptText = 
$@"# [DLL 디컴파일 및 C# 프로젝트 복원/실행 워크플로우 요청]
- 대상 어셈블리: {targetInfo}
- 작업 디렉터리: {workDir}

[수행 지침]
1. 작업 폴더 생성:
   - '{workDir}' 디렉터리를 만들고 분석 환경을 준비해줘.
2. C# 전체 프로젝트 디컴파일 복원:
   - ilspycmd 도구를 사용하여 대상 DLL을 완전한 C# 프로젝트 소스 코드로 역개설 디컴파일해줘:
     `ilspycmd -p -o ""{workDir}\src"" ""{targetInfo}""`
   - 생성된 C# 소스 파일과 클래스 계층 구조를 점검해줘.
3. 빌드 및 테스트 실행:
   - 복원된 프로젝트 폴더(`{workDir}\src`)에서 `dotnet build`를 실행하여 빌드 성공 여부를 확인해줘.
   - MelonLoader 또는 테스트 런처를 통해 정상 구동되는지 실행 및 로그를 검증해줘.";
                break;

            case "apk":
                PromptText = 
$@"# [APK 디컴파일 및 안드로이드 프로젝트 복원/실행 워크플로우 요청]
- 대상 파일: {targetInfo}
- 작업 디렉터리: {workDir}

[수행 지침]
1. 작업 폴더 생성:
   - '{workDir}' 디렉터리를 생성해줘.
2. 소스 코드 및 리소스 전체 디컴파일:
   - JADX를 사용하여 Java 소스 코드를 디컴파일해줘:
     `jadx -d ""{workDir}\jadx_src"" ""{targetInfo}""`
   - Apktool을 사용하여 AndroidManifest.xml 및 리소스를 추출해줘:
     `apktool d -f -o ""{workDir}\apktool_out"" ""{targetInfo}""`
3. 구조 분석 및 에뮬레이터/ADB 실행 준비:
   - 메인 액티비티 진입점과 주요 네이티브(.so) 라이브러리 연동부를 파악해줘.
   - 필요 시 ADB 또는 에뮬레이터 환경에서 패키지를 설치 및 실행(`adb install`, `adb shell am start`)하여 동작을 검증해줘.";
                break;

            case "app":
                PromptText = 
$@"# [APP / Electron 디컴파일 및 소스 추출/실행 워크플로우 요청]
- 대상 파일: {targetInfo}
- 작업 디렉터리: {workDir}

[수행 지침]
1. 작업 폴더 생성:
   - '{workDir}' 디렉터리를 생성해줘.
2. ASAR 패키지 언팩 및 내부 소스 추출:
   - @electron/asar 도구를 사용하여 내부 패키지 내용을 전부 풀어줘:
     `npx asar extract ""{targetInfo}"" ""{workDir}\extracted""`
   - main.js, preload.js 및 렌더러 소스 파일 구조를 파악해줘.
3. 로컬 실행 및 동작 테스트:
   - 추출된 디렉터리(`{workDir}\extracted`)에서 필요한 의존성을 확인한 후:
     `npm install` 및 `npx electron .` 명령어로 앱을 직접 실행 구동해줘.
   - IPC 통신 채널과 주요 UI 기능이 정상 동작하는지 테스트해줘.";
                break;
        }

        StatusMessage = $"[{SelectedTargetCategory}] 디컴파일 및 실행 워크플로우 프롬프트가 적용되었습니다. (폴더: {Path.GetFileName(workDir)})";
    }

    /// <summary>
    /// 프롬프트 입력창 내용 초기화
    /// </summary>
    [RelayCommand]
    public void ClearPrompt()
    {
        PromptText = string.Empty;
        StatusMessage = "프롬프트 입력창이 초기화되었습니다.";
    }

    /// <summary>
    /// 프롬프트 클립보드 복사
    /// </summary>
    [RelayCommand]
    public async Task CopyPromptAsync()
    {
        if (string.IsNullOrWhiteSpace(PromptText))
        {
            StatusMessage = "복사할 프롬프트 내용이 없습니다.";
            return;
        }

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.Clipboard != null)
            {
                await desktop.MainWindow.Clipboard.SetTextAsync(PromptText);
                StatusMessage = "프롬프트가 클립보드에 복사되었습니다! (에이전트에 바로 붙여넣기 가능)";
                return;
            }
        }
        catch { }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new ProcessStartInfo("cmd", "/c clip")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.StandardInput.WriteAsync(PromptText);
                    proc.StandardInput.Close();
                    await proc.WaitForExitAsync();
                    StatusMessage = "프롬프트가 클립보드에 복사되었습니다 (Windows clip).";
                    return;
                }
            }
            catch { }
        }

        StatusMessage = "클립보드 복사 완료";
    }

    /// <summary>
    /// Claude 앱으로 프롬프트 실연동 전송
    /// 1) 작업 폴더에 prompt.md 저장
    /// 2) 클립보드에 프롬프트 복사
    /// 3) 열려있는 Claude 앱 창을 활성화하여 자동으로 붙여넣기(Ctrl+V) 및 전송(Enter) 실행
    /// </summary>
    [RelayCommand]
    public async Task SendToClaudeAsync()
    {
        if (string.IsNullOrWhiteSpace(PromptText))
        {
            StatusMessage = "전송할 프롬프트 내용이 비어 있습니다.";
            return;
        }

        try
        {
            // 1. 작업 폴더 생성 및 프롬프트 파일 저장
            if (!string.IsNullOrWhiteSpace(WorkDirPath))
            {
                Directory.CreateDirectory(WorkDirPath);
                var promptFile = Path.Combine(WorkDirPath, "claude_prompt.md");
                await File.WriteAllTextAsync(promptFile, PromptText);
            }

            // 2. 클립보드에 복사
            await CopyPromptAsync();

            // 3. 열려있는 Claude 창 검색
            var claudeHwnd = WindowsAutomation.FindOpenWindow("claude", "Claude");

            if (claudeHwnd != IntPtr.Zero)
            {
                // 열려있는 창으로 포커스하여 자동 붙여넣기 및 엔터 전송
                await WindowsAutomation.SendPromptToWindowAsync(claudeHwnd);
                StatusMessage = "🚀 열려있는 Claude 앱으로 프롬프트를 자동 전송(붙여넣기+엔터)했습니다!";
            }
            else
            {
                // 앱이 아직 안 켜져있으면 실행 후 전송 시도
                var claudeDesktopPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\WindowsApps\claude-desktop.exe");

                if (File.Exists(claudeDesktopPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = claudeDesktopPath,
                        UseShellExecute = true
                    });
                    await Task.Delay(2000);
                    claudeHwnd = WindowsAutomation.FindOpenWindow("claude", "Claude");
                    if (claudeHwnd != IntPtr.Zero)
                    {
                        await WindowsAutomation.SendPromptToWindowAsync(claudeHwnd);
                        StatusMessage = "🚀 Claude 앱을 실행하고 프롬프트를 자동 전송했습니다!";
                        return;
                    }
                }

                StatusMessage = "📋 클립보드에 프롬프트가 복사되었습니다. (Claude 앱을 켜고 붙여넣으세요)";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Claude 연동 오류: {ex.Message}";
        }
    }

    /// <summary>
    /// Antigravity로 프롬프트 실연동 전송
    /// 1) 작업 폴더에 antigravity_prompt.md 저장
    /// 2) 클립보드에 프롬프트 복사
    /// 3) 열려있는 Antigravity 창을 활성화하여 자동으로 붙여넣기(Ctrl+V) 및 전송(Enter) 실행
    /// </summary>
    [RelayCommand]
    public async Task SendToAntigravityAsync()
    {
        if (string.IsNullOrWhiteSpace(PromptText))
        {
            StatusMessage = "전송할 프롬프트 내용이 비어 있습니다.";
            return;
        }

        try
        {
            // 1. 작업 폴더 생성 및 프롬프트 파일 저장
            if (!string.IsNullOrWhiteSpace(WorkDirPath))
            {
                Directory.CreateDirectory(WorkDirPath);
                var promptFile = Path.Combine(WorkDirPath, "antigravity_prompt.md");
                await File.WriteAllTextAsync(promptFile, PromptText);
            }

            // 2. 클립보드에 복사
            await CopyPromptAsync();

            // 3. 열려있는 Antigravity 창 검색
            var agyHwnd = WindowsAutomation.FindOpenWindow("Antigravity", "Antigravity");

            if (agyHwnd != IntPtr.Zero)
            {
                // 열려있는 창으로 포커스하여 자동 붙여넣기 및 엔터 전송
                await WindowsAutomation.SendPromptToWindowAsync(agyHwnd);
                StatusMessage = "🌌 열려있는 Antigravity로 프롬프트를 자동 전송(붙여넣기+엔터)했습니다!";
            }
            else
            {
                // 앱이 아직 안 켜져있으면 실행
                var agyAppPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Antigravity\Antigravity.exe");

                if (File.Exists(agyAppPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = agyAppPath,
                        Arguments = !string.IsNullOrWhiteSpace(WorkDirPath) ? $"\"{WorkDirPath}\"" : "",
                        UseShellExecute = true
                    });
                    await Task.Delay(2000);
                    agyHwnd = WindowsAutomation.FindOpenWindow("Antigravity", "Antigravity");
                    if (agyHwnd != IntPtr.Zero)
                    {
                        await WindowsAutomation.SendPromptToWindowAsync(agyHwnd);
                        StatusMessage = "🌌 Antigravity를 실행하고 프롬프트를 자동 전송했습니다!";
                        return;
                    }
                }

                StatusMessage = "📋 클립보드에 프롬프트가 복사되었습니다. (Antigravity에서 붙여넣으세요)";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Antigravity 연동 오류: {ex.Message}";
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url.Replace("&", "^&")}") { CreateNoWindow = true });
            }
        }
    }
}
