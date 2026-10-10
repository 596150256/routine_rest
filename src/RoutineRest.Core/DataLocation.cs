using System;
using System.IO;

namespace RoutineRest.Core;

/// <summary>Every launch of the same executable uses its adjacent data directory, independent of AppData and the working directory.</summary>
public static class DataLocation
{
    public static string ForExecutable(string executableDirectory)
    {
        if (!Path.IsPathFullyQualified(executableDirectory))
            throw new ArgumentException("程序目录必须是绝对路径。", nameof(executableDirectory));
        return Path.Combine(Path.GetFullPath(executableDirectory), "data");
    }

    /// <summary>Keep this handle open for the process lifetime. A file lock also covers launches in different Windows package namespaces.</summary>
    public static FileStream AcquireWriter(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        return new FileStream(Path.Combine(dataDirectory, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
