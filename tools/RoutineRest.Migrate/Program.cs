using System;
using System.IO;
using System.Linq;
using RoutineRest.Core;

if (args.Length < 2 || !Path.IsPathFullyQualified(args[0]) || args.Skip(1).Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path)))
{
    Console.Error.WriteLine("Usage: RoutineRest.Migrate <absolute data directory> <existing absolute snapshot path> [snapshot ...]");
    return 1;
}
try
{
    using FileStream writer = DataLocation.AcquireWriter(args[0]);
    JsonStateStore store = new(args[0]);
    if (!StateMigration.ImportIfEmpty(store, args.Skip(1).ToArray()))
    {
        Console.Error.WriteLine("Unified records already exist. No data was overwritten.");
        return 2;
    }
    Console.WriteLine("Migrated with raw backups: " + store.FilePath);
    return 0;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or OverflowException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine("Migration stopped without modifying the source snapshots: " + error.Message);
    return 1;
}
