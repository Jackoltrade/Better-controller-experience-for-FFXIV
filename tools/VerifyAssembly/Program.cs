// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Xml.Linq;

internal static class VerifyAssembly
{
    public static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Usage: VerifyAssembly <plugin.dll> <manifest.json> <Dalamud directory>");
        var plugin = Path.GetFullPath(args[0]);
        using var manifest = JsonDocument.Parse(File.ReadAllText(args[1]));
        var root = manifest.RootElement;
        var name = AssemblyName.GetAssemblyName(plugin);
        var version = root.GetProperty("AssemblyVersion").GetString();
        var internalName = root.GetProperty("InternalName").GetString();
        var api = root.GetProperty("DalamudApiLevel").GetInt32();
        if (name.Name != internalName || name.Version?.ToString() != version)
            throw new InvalidDataException("DLL identity/version differs from the manifest.");
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
        var project = XDocument.Load(Directory.GetFiles(projectDirectory, "*.csproj").Single());
        var projectVersion = new Version(project.Descendants("Version").Single().Value);
        var expected = new Version(projectVersion.Major, projectVersion.Minor,
            Math.Max(0, projectVersion.Build), Math.Max(0, projectVersion.Revision));
        if (expected != name.Version) throw new InvalidDataException("Project and assembly versions differ.");
        using var image = new PEReader(File.OpenRead(plugin));
        if (image.PEHeaders.CoffHeader.Machine != Machine.Amd64)
            throw new InvalidDataException("Public test builds must target x64.");
        var assembly = Assembly.LoadFrom(plugin);
        if (assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product != root.GetProperty("Name").GetString())
            throw new InvalidDataException("Assembly product and manifest display name differ.");
        if (assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true)
            throw new InvalidDataException("Public test builds must enable compiler optimization.");
        var dalamud = AssemblyName.GetAssemblyName(Path.Combine(args[2], "Dalamud.dll"));
        if (dalamud.Version?.Major != api)
            throw new InvalidDataException($"Manifest targets API {api}, but build libraries report {dalamud.Version}.");
        var data = new
        {
            InternalName = internalName, AssemblyVersion = version, DalamudApiLevel = api,
            Architecture = "x64", Optimized = true,
            DllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(plugin))).ToLowerInvariant(),
            SourceSha256 = SourceFingerprint(Path.GetDirectoryName(Path.GetFullPath(args[1]))!),
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(plugin)!, "build-verification.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Verified {name.Name} {version}: optimized x64, Dalamud API {api}.");
        return 0;
    }

    private static string SourceFingerprint(string directory)
    {
        var extensions = new[] { ".cs", ".csproj", ".props", ".targets", ".json", ".png", ".svg" };
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(directory, path).Replace('\\', '/')))
            .Where(file => extensions.Contains(Path.GetExtension(file.Path)) &&
                !file.Relative.Split('/').Any(part => part is "bin" or "obj"))
            .OrderBy(file => file.Relative, StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.Relative + "\0"));
            hash.AppendData(File.ReadAllBytes(file.Path));
            hash.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
