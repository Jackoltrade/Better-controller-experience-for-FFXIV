// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DualSenseLighting.Core;

/// <summary>Publishes completed JSON captures and retains the newest owned archives.</summary>
public static class TraceArchive
{
    public static string ScreenTimingPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualSenseLighting", "screen-haptics-timing.json");
    public static string ArchiveDirectory(string latestPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(latestPath))!, "sfx-traces");
    private const int Keep = 20;
    private const string Prefix = "sfx-trace-";
    private const string TimestampFormat = "yyyyMMdd'T'HHmmss.fffffff'Z'";
    private static readonly Regex OwnedName = new(@"^sfx-trace-(\d{8}T\d{6}\.\d{7}Z)(?:-(\d{4,19}))?\.json$",
        RegexOptions.CultureInvariant);
    private sealed record Capture(string Path, DateTimeOffset Started, long Sequence);

    /// <summary>Existing recording files only; never traverses archive subdirectories or links.</summary>
    public static IEnumerable<string> RecordingFiles(string latestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(latestPath);
        var latest = Path.GetFullPath(latestPath);
        if (Path.GetFileName(latest) is not ("sfx-trace.json" or "screen-haptics-timing.json"))
            throw new ArgumentException("The latest recording filename is not recognized.", nameof(latestPath));
        ValidateRecordingDirectory(Path.GetDirectoryName(latest)!);
        if (ValidateRecordingFile(latest)) yield return latest;
        var directory = ArchiveDirectory(latest);
        ValidateRecordingDirectory(directory);
        if (!ExistingAttributes(directory).HasValue) yield break;
        Capture[] captures;
        try { captures = Captures(directory).ToArray(); }
        catch (DirectoryNotFoundException) { yield break; }
        foreach (var capture in captures)
            if (ValidateRecordingFile(capture.Path)) yield return capture.Path;
    }

    /// <summary>Deletes a validated snapshot of owned files. IO failures remain visible to the caller.</summary>
    public static int DeleteRecordings(params string[] latestPaths)
    {
        ArgumentNullException.ThrowIfNull(latestPaths);
        var targets = latestPaths.SelectMany(RecordingFiles)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        // Finish validating every requested location before deleting anything.
        // Recheck each parent/leaf before deletion too; do not follow links that
        // appeared while collecting the other location's files.
        foreach (var path in targets)
        {
            ValidateRecordingDirectory(Path.GetDirectoryName(path)!);
            ValidateRecordingFile(path);
        }
        var deleted = 0;
        foreach (var path in targets)
        {
            ValidateRecordingDirectory(Path.GetDirectoryName(path)!);
            if (!ValidateRecordingFile(path)) continue;
            File.Delete(path);
            deleted++;
        }
        return deleted;
    }

    private static FileAttributes? ExistingAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static bool ValidateRecordingFile(string path)
    {
        var attributes = ExistingAttributes(path);
        if (!attributes.HasValue) return false;
        if ((attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException($"Recording file is a directory or link; refusing to delete: '{path}'.");
        return true;
    }

    private static void ValidateRecordingDirectory(string path)
    {
        var ancestors = new Stack<DirectoryInfo>();
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            ancestors.Push(directory);
        // Check parents first, before asking the filesystem about a child that
        // might otherwise be reached through a reparse-point ancestor.
        while (ancestors.TryPop(out var directory))
        {
            var attributes = ExistingAttributes(directory.FullName);
            if (!attributes.HasValue) continue;
            if ((attributes.Value & FileAttributes.Directory) == 0 || (attributes.Value & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Recording location is not a regular directory; refusing to follow it: '{directory.FullName}'.");
        }
    }

    public static void Save(string latestPath, DateTimeOffset started, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(latestPath);
        ArgumentNullException.ThrowIfNull(json);
        var latest = Path.GetFullPath(latestPath);
        var directory = Path.GetDirectoryName(latest)!;
        var archiveDirectory = ArchiveDirectory(latest);
        Directory.CreateDirectory(archiveDirectory);
        var utc = started.ToUniversalTime();
        var stem = Prefix + utc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        // Continue the sequence even after retention removed the original base
        // filename. Back-to-back segments may intentionally share one start time.
        var sequence = checked(Captures(archiveDirectory).Where(x => x.Started == utc)
            .Select(x => x.Sequence).DefaultIfEmpty(-1).Max() + 1);
        string? archiveTemporary = null, latestTemporary = null;
        try
        {
            archiveTemporary = WriteTemporary(archiveDirectory, json);
            while (true)
            {
                var archive = Path.Combine(archiveDirectory,
                    stem + (sequence == 0 ? "" : "-" + sequence.ToString("D4", CultureInfo.InvariantCulture)) + ".json");
                try { File.Move(archiveTemporary, archive); break; }
                catch (IOException) when (File.Exists(archive)) { sequence = checked(sequence + 1); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { throw PublicationError("archive rename", archiveTemporary, archive, error); }
            }
            archiveTemporary = null;
            // Both files contain the caller's already-serialized JSON. Same-
            // directory replacement publishes the latest file as one rename.
            latestTemporary = WriteTemporary(directory, json);
            PublishLatest(latestTemporary, latest);
            latestTemporary = null;
            Prune(archiveDirectory);
        }
        finally
        {
            DeleteTemporary(archiveTemporary);
            DeleteTemporary(latestTemporary);
        }
    }

    private static IOException PublicationError(string stage, string source, string destination, Exception error) =>
        new($"Trace {stage} failed: '{source}' -> '{destination}' (HRESULT 0x{error.HResult:X8}).", error);

    private static void PublishLatest(string source, string destination)
    {
        var elapsed = Stopwatch.StartNew();
        Exception? firstError = null;
        while (true)
        {
            try { File.Move(source, destination, true); return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                firstError ??= error;
                // Windows can briefly deny replacing an otherwise writable file
                // while a reader holds it. Retain the same complete temporary
                // file and retry only this atomic rename, on the save worker.
                if (elapsed.ElapsedMilliseconds >= 500 || !CanRetryLatest(error, source, destination))
                    throw PublicationError("latest replacement", source, destination, firstError);
                Thread.Sleep(10);
            }
        }
    }

    private static bool CanRetryLatest(Exception error, string source, string destination)
    {
        if (!OperatingSystem.IsWindows() || (error.HResult & 0xffff) is not (5 or 32 or 33) || !File.Exists(source))
            return false;
        try
        {
            return (File.GetAttributes(destination) &
                (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory)) == 0;
        }
        catch (FileNotFoundException) { return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string WriteTemporary(string directory, string json)
    {
        var path = Path.Combine(directory, ".sfx-trace-write-" + Guid.NewGuid().ToString("N") + ".tmp");
        var created = false;
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(json);
            return path;
        }
        catch
        {
            if (created) DeleteTemporary(path);
            throw;
        }
    }

    private static System.Collections.Generic.IEnumerable<Capture> Captures(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, Prefix + "*.json", SearchOption.TopDirectoryOnly))
        {
            var match = OwnedName.Match(Path.GetFileName(path));
            if (!match.Success || !DateTimeOffset.TryParseExact(match.Groups[1].Value, TimestampFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var started)) continue;
            long sequence = 0;
            if (match.Groups[2].Success && (!long.TryParse(match.Groups[2].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out sequence) || sequence <= 0 ||
                match.Groups[2].Value != sequence.ToString("D4", CultureInfo.InvariantCulture))) continue;
            yield return new(path, started, sequence);
        }
    }

    private static void Prune(string directory)
    {
        try
        {
            foreach (var capture in Captures(directory).OrderByDescending(x => x.Started).ThenByDescending(x => x.Sequence).Skip(Keep))
                DeleteTemporary(capture.Path);
        }
        // Capture publication succeeded. Locked files or unavailable housekeeping
        // must not turn it into a failed recording or remove unrelated files.
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteTemporary(string? path)
    {
        if (path == null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
