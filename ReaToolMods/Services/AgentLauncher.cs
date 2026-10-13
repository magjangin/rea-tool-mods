using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ReaToolMods.Models;

namespace ReaToolMods.Services;

/// <summary>프롬프트를 전송할 에이전트 앱 정의</summary>
public sealed record AgentApp(
    string DisplayName,
    string Emoji,
    string Tag,
    string ProcessKeyword,
    string TitleKeyword,
    string? ExecutablePath,
    bool OpenWorkDirOnLaunch);

public static class AgentApps
{
    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static readonly AgentApp Claude = new(
        "Claude 앱", "🤖", "claude", "claude", "Claude",
        Path.Combine(LocalAppData, @"Microsoft\WindowsApps\claude-desktop.exe"),
        OpenWorkDirOnLaunch: false);

    public static readonly AgentApp Antigravity = new(
        "Antigravity", "🌌", "antigravity", "Antigravity", "Antigravity",
        Path.Combine(LocalAppData, @"Programs\Antigravity\Antigravity.exe"),
        OpenWorkDirOnLaunch: true);

    public static IReadOnlyList<AgentApp> All { get; } = [Claude, Antigravity];
}

/// <summary>
/// 프롬프트 전송 공통 흐름:
/// 1) 작업 폴더에 프롬프트 스냅샷 + workspace.json 저장
/// 2) 클립보드 복사
/// 3) 열린 앱 창(없으면 실행 후)을 찾아 붙여넣기(Ctrl+V) + 전송(Enter)
/// </summary>
public static class AgentLauncher
{
    public static async Task<string> SendAsync(AgentApp app, string prompt, string workDir, string targetPath, TargetKind kind)
    {
        if (!string.IsNullOrWhiteSpace(workDir))
        {
            Directory.CreateDirectory(workDir);
            WorkspaceInspector.SavePromptSnapshot(workDir, app.Tag, prompt);
            WorkspaceInspector.SaveMetadata(workDir, targetPath, kind);
        }

        if (!await ClipboardService.SetTextAsync(prompt))
            return "클립보드 복사에 실패했습니다.";

        var hwnd = WindowsAutomation.FindOpenWindow(app.ProcessKeyword, app.TitleKeyword);
        if (hwnd != IntPtr.Zero)
        {
            await WindowsAutomation.SendPromptToWindowAsync(hwnd);
            return $"{app.Emoji} 열려 있는 {app.DisplayName}로 프롬프트를 자동 전송(붙여넣기+엔터)했습니다!";
        }

        if (app.ExecutablePath != null && File.Exists(app.ExecutablePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = app.ExecutablePath,
                Arguments = app.OpenWorkDirOnLaunch && !string.IsNullOrWhiteSpace(workDir) ? $"\"{workDir}\"" : "",
                UseShellExecute = true
            });

            // 앱 창이 뜰 때까지 최대 10초 대기
            for (var i = 0; i < 20 && hwnd == IntPtr.Zero; i++)
            {
                await Task.Delay(500);
                hwnd = WindowsAutomation.FindOpenWindow(app.ProcessKeyword, app.TitleKeyword);
            }

            if (hwnd != IntPtr.Zero)
            {
                await Task.Delay(1000); // 입력창 초기화 대기
                await WindowsAutomation.SendPromptToWindowAsync(hwnd);
                return $"{app.Emoji} {app.DisplayName}를 실행하고 프롬프트를 자동 전송했습니다!";
            }
        }

        return $"📋 클립보드에 프롬프트가 복사되었습니다. ({app.DisplayName}를 열고 붙여넣으세요)";
    }
}
