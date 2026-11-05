// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DualSenseLighting.Core;

internal static class TraceArchiveChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckThreeCaptures(check);
        CheckTimestampCollisions(check);
        CheckRetention(check);
        CheckFailedPublication(check);
        CheckLockedLatest(check);
        CheckRecordingDeletionScope(check);
        CheckSharedRecordingDirectory(check);
        CheckDeletionValidation(check);
        CheckDeletionLinks(check);
        CheckLockedDeletion(check);
    }

    private static readonly DateTimeOffset Started = new DateTimeOffset(2026, 11, 4, 1, 2, 3, TimeSpan.FromHours(8)).AddTicks(1234567);
    private static string MakeDirectory() => Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,
        "trace-archive-checks-" + Guid.NewGuid().ToString("N"))).FullName;
    private static string[] Archives(string root) => Directory.GetFiles(Path.Combine(root, "sfx-traces"), "*.json");
    private static bool HasTemporaryFiles(string root) => Directory.EnumerateFiles(root, ".sfx-trace-write-*.tmp", SearchOption.AllDirectories).Any();

    private static void CheckThreeCaptures(Action<bool, string> check)
    {
        var root = MakeDirectory();
        try
        {
            var latest = Path.Combine(root, "sfx-trace.json");
            string[] captures = ["{\"capture\":1,\"text\":\"中文・日本語\"}", "{\"capture\":2}", "{\"capture\":3}"];
            for (var i = 0; i < captures.Length; i++)
            {
                TraceArchive.Save(latest, Started.AddSeconds(i), captures[i]);
                check(File.ReadAllText(latest) == captures[i], "Latest trace did not atomically publish the complete supplied JSON");
            }
            var archives = Archives(root); var actual = archives.Select(File.ReadAllText).ToHashSet(StringComparer.Ordinal);
            check(archives.Length == 3 && captures.All(actual.Contains), "Consecutive captures overwrote an earlier recording");
            check(archives.Any(x => Path.GetFileName(x) == "sfx-trace-20261103T170203.1234567Z.json"),
                "Archive naming did not normalize UTC or retain seven fractional timestamp digits");
            check(!HasTemporaryFiles(root), "Completed trace publication left temporary files");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckTimestampCollisions(Action<bool, string> check)
    {
        var root = MakeDirectory();
        try
        {
            var latest = Path.Combine(root, "sfx-trace.json");
            TraceArchive.Save(latest, Started, "capture-0");
            TraceArchive.Save(latest, Started.ToOffset(TimeSpan.FromHours(-4)), "capture-1");
            var first = Archives(root);
            check(first.Length == 2 && first.Select(File.ReadAllText).ToHashSet().SetEquals(new[] { "capture-0", "capture-1" }),
                "Equal UTC timestamps replaced an earlier capture instead of using a unique archive name");
            check(first.Any(x => Path.GetFileName(x) == "sfx-trace-20261103T170203.1234567Z-0001.json"),
                "Timestamp collision suffix did not remain deterministic");
            // Retention eventually removes the base name. Later segments must
            // continue numbering rather than reusing zero and immediately pruning it.
            for (var i = 2; i < 25; i++) TraceArchive.Save(latest, Started, "capture-" + i);
            var retained = Archives(root); var content = retained.Select(File.ReadAllText).ToHashSet(StringComparer.Ordinal);
            check(retained.Length == 20 && Enumerable.Range(5, 20).All(i => content.Contains("capture-" + i)) &&
                !Enumerable.Range(0, 5).Any(i => content.Contains("capture-" + i)),
                "Same-timestamp segments did not retain the newest twenty after early names were pruned");
            check(File.ReadAllText(latest) == "capture-24" && !HasTemporaryFiles(root),
                "Back-to-back trace segments lost the latest payload or leaked temporary files");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckRetention(Action<bool, string> check)
    {
        var root = MakeDirectory();
        try
        {
            var latest = Path.Combine(root, "sfx-trace.json");
            var directory = Directory.CreateDirectory(Path.Combine(root, "sfx-traces")).FullName;
            var unrelated = new Dictionary<string, string> {
                ["notes.json"] = "user notes", ["do-not-delete.tmp"] = "user temporary file",
                ["sfx-trace-not-a-timestamp.json"] = "invalid timestamp",
                ["sfx-trace-20261332T170203.1234567Z.json"] = "invalid date",
                ["sfx-trace-20261103T170203.1234567Z-00001.json"] = "noncanonical suffix",
                ["sfx-trace-20261103T170203.1234567Z-0000.json"] = "noncanonical zero suffix",
                ["sfx-trace-20261103T170203.1234567Z.other.json"] = "unrelated extension"
            };
            foreach (var pair in unrelated) File.WriteAllText(Path.Combine(directory, pair.Key), pair.Value);
            var nested = Directory.CreateDirectory(Path.Combine(directory, "nested")).FullName;
            var nestedFile = Path.Combine(nested, "sfx-trace-20200101T000000.0000000Z.json"); File.WriteAllText(nestedFile, "nested user file");
            for (var i = 0; i < 25; i++) TraceArchive.Save(latest, Started.AddSeconds(i), "capture-" + i);
            TraceArchive.Save(latest, Started.AddDays(-1), "older-capture-written-later");
            var owned = Directory.GetFiles(directory).Where(x => !unrelated.ContainsKey(Path.GetFileName(x))).ToArray();
            var actual = owned.Select(File.ReadAllText).ToHashSet(StringComparer.Ordinal);
            check(owned.Length == 20 && Enumerable.Range(5, 20).All(i => actual.Contains("capture-" + i)) &&
                !actual.Contains("older-capture-written-later"),
                "Retention used write order rather than capture timestamps or retained more than twenty owned archives");
            check(unrelated.All(x => File.ReadAllText(Path.Combine(directory, x.Key)) == x.Value) &&
                File.ReadAllText(nestedFile) == "nested user file",
                "Retention modified an unrelated file or descended into a user subdirectory");
            check(File.ReadAllText(latest) == "older-capture-written-later" && !HasTemporaryFiles(root),
                "Archive retention changed the last published latest trace or leaked temporary files");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckFailedPublication(Action<bool, string> check)
    {
        var root = MakeDirectory();
        try
        {
            var latest = Path.Combine(root, "sfx-trace.json");
            for (var i = 0; i < 20; i++)
            {
                try { TraceArchive.Save(latest, Started.AddSeconds(i), "before-" + i); }
                catch (Exception error) { throw new InvalidOperationException("Failed to seed archive fixture " + i, error); }
            }
            var previous = Archives(root).ToDictionary(x => x, File.ReadAllText, StringComparer.Ordinal);
            var blocked = Directory.CreateDirectory(Path.Combine(root, "blocked.json")).FullName;
            File.WriteAllText(Path.Combine(blocked, "user.txt"), "preserve directory");
            var failed = false;
            try { TraceArchive.Save(blocked, Started.AddSeconds(20), "failed-latest-publication"); }
            catch (IOException) { failed = true; }
            catch (UnauthorizedAccessException) { failed = true; }
            check(failed && previous.All(x => File.Exists(x.Key) && File.ReadAllText(x.Key) == x.Value) && Archives(root).Length == 21,
                "A failed latest publication pruned or overwrote an existing archive");
            check(File.ReadAllText(latest) == "before-19" && File.ReadAllText(Path.Combine(blocked, "user.txt")) == "preserve directory" &&
                !HasTemporaryFiles(root),
                "A failed latest publication changed existing data or left an owned temporary file");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckLockedLatest(Action<bool, string> check)
    {
        // Windows denies replacing a target opened without FileShare.Delete.
        // Keep that real OS lock for the entire call: failure must remain bounded
        // and preserve every archive, even when the retention limit was reached.
        if (!OperatingSystem.IsWindows()) return;
        var root = MakeDirectory();
        try
        {
            var latest = Path.Combine(root, "sfx-trace.json");
            for (var i = 0; i < 20; i++) TraceArchive.Save(latest, Started.AddSeconds(i), "before-lock-" + i);
            var previous = Archives(root).ToDictionary(x => x, File.ReadAllText, StringComparer.Ordinal);
            IOException? failure = null;
            var elapsed = Stopwatch.StartNew();
            using (var reader = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { TraceArchive.Save(latest, Started.AddSeconds(20), "locked-publication"); }
                catch (IOException error) { failure = error; }
                elapsed.Stop();
                check(File.ReadAllText(latest) == "before-lock-19",
                    "A failed replacement changed the locked latest recording");
            }
            check(failure != null && elapsed.Elapsed < TimeSpan.FromSeconds(5),
                "A persistent Windows reader lock was ignored or caused unbounded publication retries");
            check(failure!.InnerException is IOException or UnauthorizedAccessException &&
                (failure.InnerException.HResult & 0xffff) is 5 or 32 or 33 &&
                failure.Message.Contains("latest replacement", StringComparison.Ordinal) &&
                failure.Message.Contains(latest, StringComparison.Ordinal) &&
                failure.Message.Contains(".sfx-trace-write-", StringComparison.Ordinal) &&
                failure.Message.Contains($"0x{failure.InnerException.HResult:X8}", StringComparison.Ordinal),
                "Publication failure lost its stage, paths, or original native Windows error");
            check(previous.All(x => File.Exists(x.Key) && File.ReadAllText(x.Key) == x.Value) &&
                Archives(root).Length == 21 && Archives(root).Any(x => File.ReadAllText(x) == "locked-publication"),
                "A locked latest publication pruned old recordings or discarded its completed archive");
            check(!HasTemporaryFiles(root), "An exhausted locked-file publication left temporary files");

            TraceArchive.Save(latest, Started.AddSeconds(21), "after-unlock");
            check(File.ReadAllText(latest) == "after-unlock" && Archives(root).Length == 20 && !HasTemporaryFiles(root),
                "Normal recording publication or retention did not recover after the reader released its lock");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckRecordingDeletionScope(Action<bool, string> check)
    {
        var root = MakeDirectory();
        try
        {
            var sfx = Path.Combine(root, "plugin", "sfx-trace.json");
            var camera = Path.Combine(root, "camera", "screen-haptics-timing.json");
            TraceArchive.Save(sfx, Started, "sfx-1"); TraceArchive.Save(sfx, Started.AddSeconds(1), "sfx-2");
            TraceArchive.Save(camera, Started, "camera-1");
            var expected = new[] { sfx, camera }.Concat(Archives(Path.GetDirectoryName(sfx)!))
                .Concat(Archives(Path.GetDirectoryName(camera)!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var archive = TraceArchive.ArchiveDirectory(sfx);
            var preserved = new Dictionary<string, string>
            {
                [Path.Combine(archive, "notes.json")] = "user notes",
                [Path.Combine(archive, "sfx-trace-20261332T170203.1234567Z.json")] = "invalid date",
                [Path.Combine(archive, "sfx-trace-20261103T170203.1234567Z-00001.json")] = "noncanonical suffix",
                [Path.Combine(archive, "sfx-trace-20261103T170203.1234567Z-0000.json")] = "noncanonical zero",
                [Path.Combine(archive, ".sfx-trace-write-owned.tmp")] = "unfinished temporary file",
                [Path.Combine(root, "plugin", "other.json")] = "unrelated latest sibling",
            };
            var nested = Directory.CreateDirectory(Path.Combine(archive, "nested")).FullName;
            preserved[Path.Combine(nested, "sfx-trace-20200101T000000.0000000Z.json")] = "nested recording-shaped user file";
            var recordingShapedDirectory = Directory.CreateDirectory(Path.Combine(archive, "sfx-trace-20200102T000000.0000000Z.json")).FullName;
            preserved[Path.Combine(recordingShapedDirectory, "user.txt")] = "directory contents";
            foreach (var entry in preserved) File.WriteAllText(entry.Key, entry.Value);
            check(TraceArchive.RecordingFiles(sfx).Concat(TraceArchive.RecordingFiles(camera)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(expected), "Recording discovery included unrelated, noncanonical, nested, temporary or directory entries");
            check(TraceArchive.DeleteRecordings(sfx, camera, sfx) == expected.Count && expected.All(path => !File.Exists(path)),
                "Recording deletion missed a location or counted repeated targets more than once");
            check(preserved.All(entry => File.ReadAllText(entry.Key) == entry.Value) &&
                Directory.Exists(archive) && Directory.Exists(TraceArchive.ArchiveDirectory(camera)) && Directory.Exists(recordingShapedDirectory),
                "Recording deletion removed unrelated files or a directory");
            check(TraceArchive.DeleteRecordings(sfx, camera) == 0 && !TraceArchive.RecordingFiles(sfx).Any() &&
                !TraceArchive.RecordingFiles(camera).Any(), "Deleting recordings twice was not harmless");
            var missing = Path.Combine(root, "missing", "sfx-trace.json");
            check(!TraceArchive.RecordingFiles(missing).Any() && TraceArchive.DeleteRecordings(missing) == 0 &&
                !Directory.Exists(Path.GetDirectoryName(missing)), "Missing recording paths created directories or failed deletion");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckSharedRecordingDirectory(Action<bool, string> check)
    {
        var root = MakeDirectory();
        try
        {
            var sfx = Path.Combine(root, "sfx-trace.json");
            var camera = Path.Combine(root, "screen-haptics-timing.json");
            TraceArchive.Save(sfx, Started, "sfx"); TraceArchive.Save(camera, Started.AddSeconds(1), "camera");
            check(TraceArchive.RecordingFiles(sfx).Count() == 3 && TraceArchive.RecordingFiles(camera).Count() == 3,
                "A shared archive directory was not discovered with either latest file");
            check(TraceArchive.DeleteRecordings(sfx, camera, Path.Combine(root, ".", "sfx-trace.json"), camera) == 4 &&
                Directory.Exists(TraceArchive.ArchiveDirectory(sfx)) && !Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(),
                "Shared archives or equivalent paths were deleted or counted twice");
            check(TraceArchive.DeleteRecordings() == 0, "An empty recording deletion request did not remain empty");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckDeletionValidation(Action<bool, string> check)
    {
        var root = MakeDirectory();
        try
        {
            var latest = Path.Combine(root, "plugin", "sfx-trace.json");
            TraceArchive.Save(latest, Started, "keep until all locations validate");
            var existing = TraceArchive.RecordingFiles(latest).ToArray();
            var unrelated = Path.Combine(root, "notes.json"); File.WriteAllText(unrelated, "user notes");
            var refused = false;
            try { TraceArchive.DeleteRecordings(latest, unrelated); } catch (ArgumentException) { refused = true; }
            check(refused && existing.All(File.Exists) && File.ReadAllText(unrelated) == "user notes",
                "Deletion started before rejecting an unrecognized latest filename");

            var directory = Directory.CreateDirectory(Path.Combine(root, "screen-haptics-timing.json")).FullName;
            File.WriteAllText(Path.Combine(directory, "user.txt"), "preserve directory");
            refused = false;
            try { TraceArchive.DeleteRecordings(latest, directory); } catch (IOException) { refused = true; }
            check(refused && existing.All(File.Exists) && File.ReadAllText(Path.Combine(directory, "user.txt")) == "preserve directory",
                "A directory at a latest path caused partial deletion or directory removal");

            var badRoot = Directory.CreateDirectory(Path.Combine(root, "blocked-archive")).FullName;
            var badLatest = Path.Combine(badRoot, "sfx-trace.json"); File.WriteAllText(badLatest, "latest");
            var archiveFile = Path.Combine(badRoot, "sfx-traces"); File.WriteAllText(archiveFile, "user file, not a directory");
            refused = false;
            try { TraceArchive.DeleteRecordings(latest, badLatest); } catch (IOException) { refused = true; }
            check(refused && existing.All(File.Exists) && File.ReadAllText(badLatest) == "latest" &&
                File.ReadAllText(archiveFile) == "user file, not a directory",
                "A bad later archive location was ignored or deletion began before all target validation");
        }
        finally { Directory.Delete(root, true); }
    }

    private static bool TryLink(Action create)
    {
        try { create(); return true; }
        catch (PlatformNotSupportedException) { return false; }
        catch (Exception error) when (OperatingSystem.IsWindows() && error is IOException or UnauthorizedAccessException &&
            (error.HResult & 0xffff) is 1 or 5 or 50 or 1314)
        {
            Console.WriteLine("Recording link checks skipped: the filesystem denied creating a fixture link.");
            return false;
        }
    }

    private static void CheckDeletionLinks(Action<bool, string> check)
    {
        var root = MakeDirectory();
        try
        {
            var latest = Path.Combine(root, "plugin", "sfx-trace.json");
            TraceArchive.Save(latest, Started, "preserve if any link is found");
            var existing = TraceArchive.RecordingFiles(latest).ToArray();
            var foreign = Directory.CreateDirectory(Path.Combine(root, "foreign")).FullName;
            var target = Path.Combine(foreign, "user.txt"); File.WriteAllText(target, "foreign file");
            var link = Path.Combine(TraceArchive.ArchiveDirectory(latest), "sfx-trace-20260101T000000.0000000Z.json");
            if (!TryLink(() => File.CreateSymbolicLink(link, target))) return;
            var refused = false;
            try { TraceArchive.DeleteRecordings(latest); } catch (IOException) { refused = true; }
            check(refused && existing.All(File.Exists) && File.ReadAllText(target) == "foreign file" &&
                (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0,
                "An archive file link was followed, removed, or discovered after deletion had begun");
            File.Delete(target);
            refused = false;
            try { TraceArchive.DeleteRecordings(latest); } catch (IOException) { refused = true; }
            check(refused && existing.All(File.Exists) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0,
                "A dangling archive link was treated as a harmless missing recording or allowed partial deletion");
            File.WriteAllText(target, "foreign file");
            File.Delete(link);

            var cameraRoot = Directory.CreateDirectory(Path.Combine(root, "camera")).FullName;
            var camera = Path.Combine(cameraRoot, "screen-haptics-timing.json"); File.WriteAllText(camera, "camera");
            var foreignArchive = Path.Combine(foreign, "sfx-trace-20260102T000000.0000000Z.json"); File.WriteAllText(foreignArchive, "foreign archive");
            var archiveLink = TraceArchive.ArchiveDirectory(camera);
            if (!TryLink(() => Directory.CreateSymbolicLink(archiveLink, foreign))) return;
            refused = false;
            try { TraceArchive.DeleteRecordings(latest, camera); } catch (IOException) { refused = true; }
            check(refused && existing.All(File.Exists) && File.ReadAllText(camera) == "camera" &&
                File.ReadAllText(foreignArchive) == "foreign archive",
                "An archive directory link escaped scope or allowed earlier valid targets to be deleted");
            Directory.Delete(archiveLink);

            File.Delete(camera); File.CreateSymbolicLink(camera, target);
            refused = false;
            try { TraceArchive.DeleteRecordings(latest, camera); } catch (IOException) { refused = true; }
            check(refused && existing.All(File.Exists) && File.ReadAllText(target) == "foreign file",
                "A latest-file link was followed or allowed partial deletion");
            File.Delete(camera);

            var ancestor = Path.Combine(root, "alias");
            if (!TryLink(() => Directory.CreateSymbolicLink(ancestor, foreign))) return;
            var foreignLatest = Path.Combine(foreign, "screen-haptics-timing.json"); File.WriteAllText(foreignLatest, "foreign latest");
            refused = false;
            try { TraceArchive.DeleteRecordings(latest, Path.Combine(ancestor, "screen-haptics-timing.json")); } catch (IOException) { refused = true; }
            check(refused && existing.All(File.Exists) && File.ReadAllText(foreignLatest) == "foreign latest",
                "A recording parent link was followed or deletion started before its validation");
            Directory.Delete(ancestor);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckLockedDeletion(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = MakeDirectory();
        try
        {
            var latest = Path.Combine(root, "sfx-trace.json"); TraceArchive.Save(latest, Started, "locked recording");
            var existing = TraceArchive.RecordingFiles(latest).ToArray();
            Exception? failure = null;
            using (var locked = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { TraceArchive.DeleteRecordings(latest); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failure = error; }
                check(failure != null && existing.All(File.Exists),
                    "A real Windows deletion lock was ignored or erased other recordings before the first failure");
            }
            check(TraceArchive.DeleteRecordings(latest) == existing.Length && existing.All(path => !File.Exists(path)),
                "Recording deletion did not recover after the file lock was released");
        }
        finally { Directory.Delete(root, true); }
    }
}
