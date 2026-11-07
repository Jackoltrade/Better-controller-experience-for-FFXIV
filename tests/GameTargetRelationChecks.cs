// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Reflection;

internal static class GameTargetRelationChecks
{
    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var type = plugin.GetType("DualSenseLighting.Windows.GameTargetRelation", true)!;
        var method = type.GetMethod("IsEnemy", BindingFlags.Public | BindingFlags.Static)!;
        var read = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!;
        var available = type.GetProperty("Available", BindingFlags.Public | BindingFlags.Static)!;
        // Resolve through the compiled plugin's referenced SDK, rather than a test-owned enum.
        var sdkReference = Array.Find(plugin.GetReferencedAssemblies(), a => a.Name == "FFXIVClientStructs")!;
        var sdk = Assembly.Load(sdkReference);
        var categoryType = sdk.GetType("FFXIVClientStructs.FFXIV.Client.Game.ActionManager+TargetCategory", true)!;
        var names = new[] { "Self", "Party", "Alliance", "Enemy", "Friendly", "OwnPet", "PartyPet", "UnkPVP" };
        for (var i = 0; i < names.Length; i++)
        {
            check(Convert.ToInt32(Enum.Parse(categoryType, names[i])) == i,
                $"Installed target category {names[i]} no longer matches the validated SDK contract");
            check((bool)method.Invoke(null, new object?[] { i })! == (i == 3),
                $"The actual target adapter misclassified category {names[i]} as an enemy");
        }
        foreach (var category in new int?[] { null, -1, 8, int.MinValue, int.MaxValue })
            check(!(bool)method.Invoke(null, new object?[] { category })!,
                "An unknown/invalid target category was admitted as an enemy");
        check(read.Invoke(null, new object[] { (nint)0 }) == null,
            "A null actor escaped the target adapter's pre-native guard");
        // The standalone check process normally has no resolved game function.
        // Never invoke native code on a fake pointer if its binding is available.
        if (!(bool)available.GetValue(null)!)
            check(read.Invoke(null, new object[] { (nint)1 }) == null,
                "An unavailable target binding did not stop before native invocation");
    }
}
