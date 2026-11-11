// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Calls the compiled full roster method. Proxies model a duty; no game function,
// hook, hardware, or arbitrary native actor memory is used.
internal static class IdleRosterChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var type = plugin.GetType("DualSenseLighting.Plugin", true)!;
        Type T(string name) => plugin.GetType("DualSenseLighting." + name, true)!;
        FieldInfo F(string name) => type.GetField(name, All)!;
        object Proxy(Type api, Func<MethodInfo, object?[]?, object?> handler) => typeof(OwnerLifecycleChecks)
            .GetMethod("Proxy", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { api, handler })!;
        object? Value(Type t, int n) => t.IsEnum ? Enum.ToObject(t, n) : Convert.ChangeType(n, t);
        var tableApi = F("objects").FieldType;
        var characterApi = tableApi.Assembly.GetType("Dalamud.Game.ClientState.Objects.Types.IBattleChara", true)!;
        var playerApi = tableApi.Assembly.GetType("Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter", true)!;
        var status = tableApi.Assembly.GetType("Dalamud.Game.ClientState.Objects.Enums.StatusFlags", true)!;
        var hostile = Convert.ToInt32(Enum.Parse(status, "Hostile"));
        var part = Convert.ToByte(Enum.Parse(tableApi.Assembly.GetType("Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind", true)!, "BNpcPart"));
        var block = Marshal.AllocHGlobal(0x1A0);
        try
        {
            Marshal.Copy(new byte[0x1A0], 0, block, 0x1A0); Marshal.WriteInt32(block + 0x78, 2000);
            foreach (var hasCapture in new[] { false, true })
            {
                var castingReads = 0; var indexReads = 0;
                var player = Proxy(playerApi, (m, a) => m.Name switch
                { "get_EntityId" => 1000u, "get_StatusFlags" => Enum.ToObject(status, 0), _ => m.ReturnType.IsValueType ? Activator.CreateInstance(m.ReturnType) : null });
                var actor = Proxy(characterApi, (m, a) => m.Name switch
                {
                    "get_Address" => block, "get_EntityId" => 2000u, "get_OwnerId" => 0u,
                    "get_ObjectIndex" => Value(m.ReturnType, 1), "get_NameId" or "get_BaseId" => 400000u,
                    "get_ObjectKind" => Enum.Parse(m.ReturnType, "BattleNpc"), "get_SubKind" => part,
                    "get_StatusFlags" => Enum.ToObject(status, hostile),
                    "get_IsCasting" => CountCasting(), "get_CastActionType" => (byte)1, "get_CastActionId" => 12u,
                    _ => m.ReturnType.IsValueType ? Activator.CreateInstance(m.ReturnType) : null,
                });
                bool CountCasting() { castingReads++; return true; }
                var table = Proxy(tableApi, (m, a) => m.Name switch
                { "get_Length" => 2, "get_LocalPlayer" => player, "get_Item" => Item((int)a![0]!), _ => null });
                object Item(int index) { indexReads++; return index == 0 ? player : actor; }
                var owner = RuntimeHelpers.GetUninitializedObject(type);
                F("objects").SetValue(owner, table);
                F("clientState").SetValue(owner, Proxy(F("clientState").FieldType, (m, a) => m.Name == "get_TerritoryType" ? Value(m.ReturnType, 1) : m.ReturnType.IsValueType ? Activator.CreateInstance(m.ReturnType) : null));
                F("condition").SetValue(owner, Proxy(F("condition").FieldType, (m, a) => false));
                F("enemyTerritory").SetValue(owner, 1u); F("enemyDuty").SetValue(owner, 700u);
                var settingsType = T("Core.HapticSettings"); var ctor = settingsType.GetConstructors().Single(c => c.GetParameters().Length > 1);
                F("hapticSettings").SetValue(owner, ctor.Invoke(ctor.GetParameters().Select(p => p.Name == "BossEnabled" ? (object)true : p.DefaultValue).ToArray()));
                var state = Activator.CreateInstance(T("Core.BossEncounterState"), 4096)!; F("bossEncounter").SetValue(owner, state);
                var catalogType = T("Core.EnemyEncounterCatalog"); var catalogCtor = catalogType.GetConstructors().Single();
                F("enemyEncounters").SetValue(owner, catalogCtor.Invoke(catalogCtor.GetParameters().Select(p => (object)Array.CreateInstance(p.ParameterType.GenericTypeArguments[0], 0)).ToArray()));
                var actors = Array.CreateInstance(F("enemyActors").FieldType.GetElementType()!, 2);
                F("enemyActors").SetValue(owner, actors); F("enemyCandidates").SetValue(owner, Array.CreateInstance(actors.GetType().GetElementType()!, 2));
                var capture = hasCapture ? RuntimeHelpers.GetUninitializedObject(T("Windows.SfxAudioCapture")) : null;
                type.GetMethod("UpdateEnemyActors", All)!.Invoke(owner, new[] { capture, (object)true });
                check((int)state.GetType().GetProperty("Count")!.GetValue(state)! == 1 &&
                    (uint)actors.GetValue(0)!.GetType().GetProperty("EntityId")!.GetValue(actors.GetValue(0))! == 2000,
                    "Idle capture gating changed remembered regular history or selected roster");
                check(castingReads == (hasCapture ? 1 : 0) && indexReads == (hasCapture ? 3 : 2),
                    "A null capture traversed dead cast properties, or a live capture lost cast publication");
            }
        }
        finally { Marshal.FreeHGlobal(block); }
    }
}
