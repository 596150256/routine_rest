using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RoutineRest.Core;

/// <summary>Atomic JSON replacement with one last-good backup; failures are surfaced to the application.</summary>
public sealed class JsonStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string FilePath { get; }
    public string RecoveryNotice { get; private set; } = "";
    public JsonStateStore(string directory) { Directory.CreateDirectory(directory); FilePath = Path.Combine(directory, "state.json"); }
    public AppState Load()
    {
        if (!File.Exists(FilePath))
        {
            if (!File.Exists(FilePath + ".bak")) return new AppState();
            AppState previous = ReadSnapshot(FilePath + ".bak");
            Save(previous);
            RecoveryNotice = "主记录文件缺失，已恢复上一份有效备份。";
            return previous;
        }
        try { return ReadSnapshot(FilePath); }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or ArgumentException)
        {
            string backup = FilePath + ".bak";
            if (File.Exists(backup))
            {
                try {
                    AppState state = ReadSnapshot(backup);
                    ArchiveBroken();
                    RecoveryNotice = "状态文件损坏，已恢复上一份有效备份。";
                    return state;
                } catch (Exception backupError) when (backupError is JsonException or IOException or InvalidDataException or ArgumentException) { }
            }
            ArchiveBroken();
            RecoveryNotice = "状态文件损坏，已保留原文件并建立新记录。";
            return new AppState();
        }
    }
    public void Save(AppState state)
    {
        StateValidation.Validate(state);
        string temp = FilePath + ".tmp";
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, Options));
        using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
        else File.Move(temp, FilePath);
    }
    /// <summary>Reads and validates a snapshot without repairing, archiving or otherwise modifying the source.</summary>
    public static AppState ReadSnapshot(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("状态文件过大。");
        AppState state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("状态文件为空。");
        StateValidation.Validate(state);
        return state;
    }
    private void ArchiveBroken() => File.Move(FilePath, FilePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), true);
}
