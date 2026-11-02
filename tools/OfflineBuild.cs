// SPDX-License-Identifier: GPL-3.0-only
// Reuses the local prototype compiler bootstrap; standard SDK builds are preferred.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

public static class OfflineBuild
{
    private static string Compiler;
    private static string Dalamud;
    private static object[] Arguments(ParameterInfo[] parameters, params object[] prefix)
    {
        var result = new object[parameters.Length];
        Array.Copy(prefix, result, prefix.Length);
        for (int i = prefix.Length; i < parameters.Length; i++)
        {
            var p = parameters[i];
            result[i] = p.HasDefaultValue && p.DefaultValue != DBNull.Value && p.DefaultValue != Missing.Value
                ? p.DefaultValue : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }
        return result;
    }
    public static int Main(string[] args)
    {
        if (args.Length < 5) throw new ArgumentException("Expected: output library|console DalamudLibPath CompilerPath source...");
        Dalamud = args[2];
        Compiler = args[3];
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string directory in new[] { Compiler, Dalamud }) {
                string path = Path.Combine(directory, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        var core = Assembly.LoadFrom(Path.Combine(Compiler, "Microsoft.CodeAnalysis.dll"));
        var csharp = Assembly.LoadFrom(Path.Combine(Compiler, "Microsoft.CodeAnalysis.CSharp.dll"));
        var treeType = core.GetType("Microsoft.CodeAnalysis.SyntaxTree");
        var referenceType = core.GetType("Microsoft.CodeAnalysis.MetadataReference");
        var parse = csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree").GetMethods()
            .First(m => m.Name == "ParseText" && m.GetParameters()[0].ParameterType == typeof(string));
        var trees = Array.CreateInstance(treeType, args.Length - 4);
        for (int i = 4; i < args.Length; i++)
            trees.SetValue(parse.Invoke(null, Arguments(parse.GetParameters(), File.ReadAllText(args[i]))), i - 4);
        var runtimePath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var paths = new HashSet<string>(((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Where(p => p.StartsWith(runtimePath, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        if (args[1] == "library") {
            paths.Add(Path.Combine(Dalamud, "Dalamud.dll"));
            paths.Add(Path.Combine(Dalamud, "Dalamud.Bindings.ImGui.dll"));
        }
        var references = Array.CreateInstance(referenceType, paths.Count);
        var createReference = referenceType.GetMethods().First(m => m.Name == "CreateFromFile" && m.GetParameters()[0].ParameterType == typeof(string));
        int n = 0;
        foreach (string path in paths)
            references.SetValue(createReference.Invoke(null, Arguments(createReference.GetParameters(), path)), n++);
        var outputKindType = core.GetType("Microsoft.CodeAnalysis.OutputKind");
        var outputKind = Enum.Parse(outputKindType, args[1] == "library" ? "DynamicallyLinkedLibrary" : "ConsoleApplication");
        var optionsType = csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions");
        var ctor = optionsType.GetConstructors().First(c => c.GetParameters()[0].ParameterType == outputKindType);
        var options = ctor.Invoke(Arguments(ctor.GetParameters(), outputKind));
        options = optionsType.GetMethod("WithAllowUnsafe", new[] { typeof(bool) }).Invoke(options, new object[] { true });
        var compilationType = csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilation");
        var create = compilationType.GetMethods().First(m => m.Name == "Create" && m.GetParameters().Length == 4);
        var compilation = create.Invoke(null, new[] { (object)Path.GetFileNameWithoutExtension(args[0]), trees, references, options });
        using (var stream = File.Create(args[0]))
        {
            var emit = compilationType.GetMethods().First(m => m.Name == "Emit" && m.GetParameters()[0].ParameterType == typeof(Stream) && m.GetParameters().Skip(1).All(p => p.IsOptional));
            var result = emit.Invoke(compilation, Arguments(emit.GetParameters(), stream));
            foreach (var diagnostic in (IEnumerable)result.GetType().GetProperty("Diagnostics").GetValue(result)) Console.WriteLine(diagnostic);
            bool success = (bool)result.GetType().GetProperty("Success").GetValue(result);
            Console.WriteLine(success ? "Build succeeded: " + args[0] : "Build failed");
            return success ? 0 : 1;
        }
    }
}
