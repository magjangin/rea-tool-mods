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

/// <summary>작업 폴더 내 이미 생성된 주요 산출물 정보 (불필요한 반복 덤프/디컴파일 및 ls 탐색 방지)</summary>
public sealed record WorkspaceArtifacts(
    bool HasIl2CppDump,
    string? DumpCsSize,
    string? ScriptJsonSize,
    int DummyDllCount,
    bool HasCpp2IlOut,
    IReadOnlyList<string> Slices,
    bool HasSrc,
    bool HasJadxSrc,
    bool HasApktoolOut,
    bool HasModProject,
    IReadOnlyList<string> Docs,
    bool HasArchive);

/// <summary>이미 있는 작업 폴더의 상태 (이어서 작업할 때 프롬프트에 반영)</summary>
public sealed record WorkspaceState(
    bool Exists,
    IReadOnlyList<string> Entries,
    int TotalEntries,
    string? ProgressFile,
    string? ProgressTail,
    WorkspaceMetadata? Metadata,
    bool TargetChanged,
    bool TargetDiffers,
    WorkspaceArtifacts? Artifacts = null)
{
    public bool HasContent => TotalEntries > 0;

    public static readonly WorkspaceState Missing = new(false, [], 0, null, null, null, false, false, null);
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
            tail = ReadTailLines(progressFile, MaxTailLines);
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

        var artifacts = ScanArtifacts(workDir);

        return new WorkspaceState(true, entries, all.Count, progressFile, tail, meta, changed, differs, artifacts);
    }

    /// <summary>
    /// 작업 디렉터리를 스캔하여 이미 생성된 주요 산출물 현황을 파악합니다.
    /// </summary>
    public static WorkspaceArtifacts ScanArtifacts(string workDir)
    {
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
        {
            return new WorkspaceArtifacts(false, null, null, 0, false, [], false, false, false, false, [], false);
        }

        var dumpDir = Path.Combine(workDir, "il2cpp_dump");
        var hasIl2CppDump = Directory.Exists(dumpDir);
        string? dumpCsSize = null;
        string? scriptJsonSize = null;
        var dummyDllCount = 0;

        if (hasIl2CppDump)
        {
            var dumpCs = Path.Combine(dumpDir, "dump.cs");
            if (File.Exists(dumpCs))
            {
                var len = new FileInfo(dumpCs).Length;
                dumpCsSize = FormatFileSize(len);
            }

            var scriptJson = Path.Combine(dumpDir, "script.json");
            if (File.Exists(scriptJson))
            {
                var len = new FileInfo(scriptJson).Length;
                scriptJsonSize = FormatFileSize(len);
            }

            var dummyDir = Path.Combine(dumpDir, "DummyDll");
            if (Directory.Exists(dummyDir))
            {
                dummyDllCount = Directory.EnumerateFiles(dummyDir, "*.dll").Count();
            }
        }

        var hasCpp2IlOut = Directory.Exists(Path.Combine(workDir, "cpp2il_out"));

        var slicesDir = Path.Combine(workDir, "slices");
        var slices = Directory.Exists(slicesDir)
            ? Directory.EnumerateFiles(slicesDir, "*.dll").Select(Path.GetFileName).Where(n => n != null).Cast<string>().ToList()
            : (IReadOnlyList<string>)[];

        var hasSrc = Directory.Exists(Path.Combine(workDir, "src"));
        var hasJadxSrc = Directory.Exists(Path.Combine(workDir, "jadx_src"));
        var hasApktoolOut = Directory.Exists(Path.Combine(workDir, "apktool_out"));

        var modDir = Path.Combine(workDir, "mod");
        var hasModProject = Directory.Exists(modDir) && (
            Directory.EnumerateFiles(modDir, "*.*proj").Any() ||
            File.Exists(Path.Combine(modDir, "mod.js")) ||
            File.Exists(Path.Combine(modDir, "package.json")));

        var docsDir = Path.Combine(workDir, "docs");
        var docs = Directory.Exists(docsDir)
            ? Directory.EnumerateFiles(docsDir, "*.md").Select(Path.GetFileName).Where(n => n != null).Cast<string>().ToList()
            : (IReadOnlyList<string>)[];

        var hasArchive = Directory.Exists(Path.Combine(workDir, "archive"));

        return new WorkspaceArtifacts(
            hasIl2CppDump,
            dumpCsSize,
            scriptJsonSize,
            dummyDllCount,
            hasCpp2IlOut,
            slices,
            hasSrc,
            hasJadxSrc,
            hasApktoolOut,
            hasModProject,
            docs,
            hasArchive);
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{(double)bytes / (1024 * 1024 * 1024):F1} GB",
            >= 1024 * 1024 => $"{(double)bytes / (1024 * 1024):F1} MB",
            >= 1024 => $"{(double)bytes / 1024:F1} KB",
            _ => $"{bytes} B"
        };
    }

    public static WorkspaceMetadata? LoadMetadata(string workDir)
    {
        try
        {
            var file = Path.Combine(workDir, MetaDirName, MetaFileName);
            if (!File.Exists(file)) return null;
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<WorkspaceMetadata>(fs, JsonOptions);
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

    /// <summary>
    /// 대용량 파일에서도 전체를 메모리에 올리지 않고 끝 부분에서 최대 maxLines 줄을 효율적으로 읽습니다.
    /// </summary>
    private static string? ReadTailLines(string path, int maxLines)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length == 0) return null;

            // 파일이 작으면 전체를 줄 단위로 읽음
            const int bufferSize = 64 * 1024;
            if (fs.Length <= bufferSize)
            {
                using var reader = new StreamReader(fs);
                var allLines = new List<string>();
                while (reader.ReadLine() is { } line) allLines.Add(line);
                var tail = string.Join(Environment.NewLine, allLines.Skip(Math.Max(0, allLines.Count - maxLines))).Trim();
                return tail.Length > 0 ? tail : null;
            }

            // 큰 파일: 끝에서 64KB만 버퍼로 읽어서 줄 분리
            fs.Seek(-bufferSize, SeekOrigin.End);
            using var tailReader = new StreamReader(fs);
            var content = tailReader.ReadToEnd();
            var lines = content.Split('\n');
            var selected = lines.Skip(Math.Max(0, lines.Length - maxLines)).Select(l => l.TrimEnd('\r'));
            var result = string.Join(Environment.NewLine, selected).Trim();
            return result.Length > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }
}
