using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using ReaToolMods.Models;

namespace ReaToolMods.Services;

/// <summary>작업 폴더에 남겨 두는 메타데이터 (.reatool/workspace.json)</summary>
public sealed record WorkspaceMetadata(
    string TargetPath,
    TargetKind Kind,
    long TargetSize,
    DateTime TargetLastWriteUtc,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>이미 있는 작업 폴더의 상태 (이어서 작업할 때 프롬프트에 반영)</summary>
public sealed record WorkspaceState(
    bool Exists,
    IReadOnlyList<string> Entries,
    int TotalEntries,
    string? ProgressFile,
    string? ProgressTail,
    WorkspaceMetadata? Metadata,
    bool TargetChanged,
    bool TargetDiffers)
{
    public bool HasContent => TotalEntries > 0;

    public static readonly WorkspaceState Missing = new(false, [], 0, null, null, null, false, false);
}

public static class WorkspaceInspector
{
    public const string MetaDirName = ".reatool";
    private const string MetaFileName = "workspace.json";
    private const int MaxListedEntries = 15;
    private const int MaxTailLines = 20;

    private static readonly string[] ProgressFileNames = ["PROGRESS.md", "progress.md", "NOTES.md", "TODO.md"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static WorkspaceState Inspect(string workDir, string? currentTarget)
    {
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir)) return WorkspaceState.Missing;

        var all = new DirectoryInfo(workDir).EnumerateFileSystemInfos()
            .Where(i => i.Name != MetaDirName)
            .OrderByDescending(i => i is DirectoryInfo)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entries = all.Take(MaxListedEntries)
            .Select(i => i is DirectoryInfo ? i.Name + "\\" : i.Name)
            .ToList();

        var progressFile = ProgressFileNames
            .Select(n => Path.Combine(workDir, n))
            .FirstOrDefault(File.Exists);

        string? tail = null;
        if (progressFile != null)
        {
            try
            {
                var lines = File.ReadAllLines(progressFile);
                tail = string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - MaxTailLines))).Trim();
                if (tail.Length == 0) tail = null;
            }
            catch { }
        }

        var meta = LoadMetadata(workDir);
        var differs = false;
        var changed = false;
        if (meta != null && !string.IsNullOrWhiteSpace(currentTarget))
        {
            try
            {
                differs = !string.Equals(Path.GetFullPath(meta.TargetPath), Path.GetFullPath(currentTarget),
                    StringComparison.OrdinalIgnoreCase);
                if (!differs && File.Exists(currentTarget))
                {
                    var info = new FileInfo(currentTarget);
                    changed = info.Length != meta.TargetSize || info.LastWriteTimeUtc != meta.TargetLastWriteUtc;
                }
            }
            catch { }
        }

        return new WorkspaceState(true, entries, all.Count, progressFile, tail, meta, changed, differs);
    }

    public static WorkspaceMetadata? LoadMetadata(string workDir)
    {
        try
        {
            var file = Path.Combine(workDir, MetaDirName, MetaFileName);
            return File.Exists(file)
                ? JsonSerializer.Deserialize<WorkspaceMetadata>(File.ReadAllText(file), JsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveMetadata(string workDir, string targetPath, TargetKind kind)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        var now = DateTime.Now;
        var previous = LoadMetadata(workDir);
        var info = File.Exists(targetPath) ? new FileInfo(targetPath) : null;
        var meta = new WorkspaceMetadata(
            targetPath,
            kind,
            info?.Length ?? 0,
            info?.LastWriteTimeUtc ?? default,
            previous?.CreatedAt ?? now,
            now);

        var dir = Directory.CreateDirectory(Path.Combine(workDir, MetaDirName));
        File.WriteAllText(Path.Combine(dir.FullName, MetaFileName), JsonSerializer.Serialize(meta, JsonOptions));
    }

    /// <summary>
    /// 전송한 프롬프트를 덮어쓰지 않고 시간순으로 보관합니다 (.reatool/prompts/yyyyMMdd-HHmmss-agent.md)
    /// </summary>
    public static string SavePromptSnapshot(string workDir, string agentTag, string prompt)
    {
        var dir = Directory.CreateDirectory(Path.Combine(workDir, MetaDirName, "prompts"));
        var file = Path.Combine(dir.FullName, $"{DateTime.Now:yyyyMMdd-HHmmss}-{agentTag}.md");
        File.WriteAllText(file, prompt);
        return file;
    }
}
