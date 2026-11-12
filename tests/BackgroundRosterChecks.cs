// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Runs the full compiled roster adapter. The reference always builds output;
// the candidate receives the scripted focus state. Both observe identical native
// actor fixtures on every frame. No game hooks, functions or devices are used.
internal static class BackgroundRosterChecks
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private sealed record Frame(string Name, int[] Visible, bool Foreground = false,
        bool BossCombat = false, bool PlayerCombat = false, uint Territory = 100, uint Player = 1000, uint Duty = 700);

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        foreach (var capacity in new[] { 2, 4096 })
        {
            using var reference = new Harness(plugin, capacity);
            using var candidate = new Harness(plugin, capacity);
            var frames = new[]
            {
                new Frame("preboss foreground", new[] { 5, 6, 1, 2, 3, 4 }, true),
                new Frame("transient regular while inactive", new[] { 7, 3 }),
                new Frame("boss begins before player joins", new[] { 1, 2, 3, 8 }, BossCombat: true),
                new Frame("boss continues while player idle", new[] { 1, 2, 3 }, BossCombat: true),
                new Frame("first foreground boss frame", new[] { 1, 2, 3 }, true, true, true),
                new Frame("boss ends while inactive", new[] { 9, 3 }, PlayerCombat: true),
                new Frame("foreground after boss ends", new[] { 1, 2, 3, 9 }, true),
                new Frame("new territory during background boss", new[] { 1, 3 }, BossCombat: true, Territory: 101),
                new Frame("foreground of new territory", new[] { 1, 3 }, true, true, true, Territory: 101),
                new Frame("replacement player while inactive", new[] { 1, 2, 3 }, Territory: 101, Player: 1001),
                new Frame("first replacement-player foreground", new[] { 1, 2, 3 }, true, true, true, 101, 1001),
                new Frame("logout", Array.Empty<int>(), Territory: 101, Player: 0),
                new Frame("login into fighting boss", new[] { 1, 3, 8 }, BossCombat: true, Territory: 101, Player: 1001),
                new Frame("first foreground after login", new[] { 1, 3, 8 }, true, true, true, 101, 1001),
                new Frame("duty ends", new[] { 1, 3 }, Territory: 101, Player: 1001, Duty: 0),
                new Frame("duty returns with reused actor addresses", new[] { 1, 3 }, true, true, true, 101, 1001),
            };
            foreach (var frame in frames)
            {
                reference.Tick(frame, true); candidate.Tick(frame, frame.Foreground);
                check(reference.State("Count") == candidate.State("Count") &&
                    reference.State("Saturated") == candidate.State("Saturated") &&
                    reference.State("Active") == candidate.State("Active"),
                    "Compiled background history state differs: " + frame.Name);
                foreach (var id in Enumerable.Range(1, 9))
                    check(reference.WasRegular(id) == candidate.WasRegular(id),
                        "Compiled background remembered identity differs: " + frame.Name + "/" + id);
                if (frame.Foreground)
                    check(reference.Output().SequenceEqual(candidate.Output()),
                        "First foreground frame failed to rebuild the exact roster: " + frame.Name);
                if (!frame.Foreground && frame.BossCombat)
                    check(candidate.Output().Length == 0 && reference.Output().Length != 0,
                        "Background fighting-boss output selection was not skipped: " + frame.Name);
                if (frame.Name == "transient regular while inactive" && capacity == 4096)
                    check(candidate.WasRegular(7), "A transient preboss enemy was missed while inactive");
                if (frame.Name == "boss begins before player joins")
                    check(!candidate.WasRegular(8), "Boss combat reopened enrollment before player combat");
                if (frame.Name == "preboss foreground")
                    check(!candidate.WasRegular(4) && !candidate.WasRegular(5) && !candidate.WasRegular(6) &&
                        candidate.WasRegular(1) && candidate.WasRegular(2),
                        "Helper/owner promotion or player ownership exclusion changed");
                if (frame.Name == "transient regular while inactive" && capacity == 2)
                    check(candidate.State("Saturated") == "True", "Background saturation control did not enroll new evidence");
            }
        }
    }

    private sealed class Actor(int id, int index) : IDisposable
    {
        internal readonly nint Address = Marshal.AllocHGlobal(0x1A0);
        internal readonly int Id = id, Index = index;
        internal uint Entity => (uint)(2000 + Id);
        internal uint Name => Id is 1 or 2 ? 400001u : Id == 3 ? 90u : (uint)(400000 + Id);
        internal uint Base => (uint)(500000 + Id);
        internal uint Owner => Id == 4 ? 2003u : Id == 5 ? 2006u : Id == 6 ? 1000u : 0;
        internal bool Helper => Id == 2;
        public void Dispose() => Marshal.FreeHGlobal(Address);
    }

    private sealed class Harness : IDisposable
    {
        private readonly Type type, actorType;
        private readonly object owner, state;
        private readonly Actor[] actors;
        private readonly object[] actorApis;
        private readonly object player;
        private readonly nint playerAddress = Marshal.AllocHGlobal(0x1A0);
        private Frame frame = new("initial", Array.Empty<int>());
        private FieldInfo Field(string name) => type.GetField(name, All)!;
        private static object Proxy(Type api, Func<MethodInfo, object?[]?, object?> handler) =>
            typeof(OwnerLifecycleChecks).GetMethod("Proxy", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { api, handler })!;
        private static object Number(Type type, int value) => type.IsEnum ? Enum.ToObject(type, value) : Convert.ChangeType(value, type);
        private static object? Default(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

        internal Harness(Assembly plugin, int capacity)
        {
            type = plugin.GetType("DualSenseLighting.Plugin", true)!;
            owner = RuntimeHelpers.GetUninitializedObject(type);
            var api = Field("objects").FieldType;
            var character = api.Assembly.GetType("Dalamud.Game.ClientState.Objects.Types.IBattleChara", true)!;
            var playerApi = api.Assembly.GetType("Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter", true)!;
            var statusType = api.Assembly.GetType("Dalamud.Game.ClientState.Objects.Enums.StatusFlags", true)!;
            var hostile = Convert.ToInt32(Enum.Parse(statusType, "Hostile"));
            var combat = Convert.ToInt32(Enum.Parse(statusType, "InCombat"));
            var part = Convert.ToInt32(Enum.Parse(api.Assembly.GetType("Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind", true)!, "BNpcPart"));
            actors = Enumerable.Range(1, 9).Select(id => new Actor(id, id)).ToArray();
            foreach (var actor in actors)
            {
                Marshal.Copy(new byte[0x1A0], 0, actor.Address, 0x1A0);
                Marshal.WriteInt32(actor.Address + 0x78, (int)actor.Entity);
            }
            actorApis = actors.Select(actor => Proxy(character, (method, args) => method.Name switch
            {
                "get_Address" => actor.Address, "get_EntityId" => actor.Entity, "get_OwnerId" => actor.Owner,
                "get_ObjectIndex" => Number(method.ReturnType, actor.Index), "get_NameId" => actor.Name, "get_BaseId" => actor.Base,
                "get_ObjectKind" => Enum.Parse(method.ReturnType, "BattleNpc"), "get_SubKind" => (byte)(actor.Helper ? 11 : part),
                "get_StatusFlags" => Enum.ToObject(statusType, hostile | (actor.Id == 3 && frame.BossCombat ? combat : 0)),
                _ => Default(method.ReturnType),
            })).ToArray();
            player = Proxy(playerApi, (method, args) => method.Name switch
            {
                "get_Address" => playerAddress,
                "get_EntityId" => frame.Player,
                "get_StatusFlags" => Enum.ToObject(statusType, frame.PlayerCombat ? combat : 0),
                _ => Default(method.ReturnType),
            });
            var table = Proxy(api, (method, args) => method.Name switch
            {
                "get_Length" => 10, "get_LocalPlayer" => frame.Player == 0 ? null : player,
                "get_Item" => (int)args![0]! == 0 ? (frame.Player == 0 ? null : player) :
                    frame.Visible.Contains((int)args![0]!) ? actorApis[(int)args[0]! - 1] : null,
                _ => Default(method.ReturnType),
            });
            Field("objects").SetValue(owner, table);
            Field("clientState").SetValue(owner, Proxy(Field("clientState").FieldType, (method, args) =>
                method.Name == "get_TerritoryType" ? Number(method.ReturnType, (int)frame.Territory) : Default(method.ReturnType)));
            Field("condition").SetValue(owner, Proxy(Field("condition").FieldType, (method, args) => false));
            var recording = type.GetField("recording", All);
            if (recording != null) recording.SetValue(owner, RuntimeHelpers.GetUninitializedObject(recording.FieldType));
            var settings = plugin.GetType("DualSenseLighting.Core.HapticSettings", true)!.GetConstructors().Single(c => c.GetParameters().Length > 1);
            Field("hapticSettings").SetValue(owner, settings.Invoke(settings.GetParameters().Select(p =>
                p.Name == "BossEnabled" ? (object)true : p.DefaultValue).ToArray()));
            state = Activator.CreateInstance(plugin.GetType("DualSenseLighting.Core.BossEncounterState", true)!, capacity)!;
            Field("bossEncounter").SetValue(owner, state);
            var catalog = plugin.GetType("DualSenseLighting.Core.EnemyEncounterCatalog", true)!.GetConstructors().Single();
            var definitions = catalog.GetParameters().Select(p => Array.CreateInstance(p.ParameterType.GenericTypeArguments[0], 0)).ToArray();
            var hunt = catalog.GetParameters()[0].ParameterType.GenericTypeArguments[0];
            definitions[0] = Array.CreateInstance(hunt, 1);
            definitions[0].SetValue(Activator.CreateInstance(hunt, 90u, 500003u, (byte)2), 0);
            Field("enemyEncounters").SetValue(owner, catalog.Invoke(definitions));
            actorType = Field("enemyActors").FieldType.GetElementType()!;
            Field("enemyActors").SetValue(owner, Array.CreateInstance(actorType, 10));
            Field("enemyCandidates").SetValue(owner, Array.CreateInstance(actorType, 10));
        }
        internal void Tick(Frame value, bool foreground)
        {
            frame = value;
            // Context lookup is separately covered; this fixture supplies the
            // same resolved duty on both sides before running the full adapter.
            Field("enemyTerritory").SetValue(owner, frame.Territory); Field("enemyDuty").SetValue(owner, frame.Duty);
            Array.Clear((Array)Field("enemyActors").GetValue(owner)!);
            type.GetMethod("UpdateEnemyActors", All)!.Invoke(owner, new object?[] { null, frame.Player != 0, foreground });
        }
        internal string State(string property) => state.GetType().GetProperty(property)!.GetValue(state)!.ToString()!;
        internal bool WasRegular(int id)
        {
            var actor = actors[id - 1]; var constructor = actorType.GetConstructors().Single(c => c.GetParameters().Length > 1);
            var values = constructor.GetParameters().Select(p => p.Name switch
            {
                "Actor" => (object)actor.Address, "EntityId" => actor.Entity, "BaseId" => actor.Base,
                _ => p.HasDefaultValue ? p.DefaultValue : Default(p.ParameterType),
            }).ToArray();
            return (bool)state.GetType().GetMethod("WasRegular")!.Invoke(state, new[] { constructor.Invoke(values) })!;
        }
        internal string[] Output() => ((Array)Field("enemyActors").GetValue(owner)!).Cast<object>()
            .Where(actor => (uint)actorType.GetProperty("EntityId")!.GetValue(actor)! != 0)
            .Select(actor => string.Join("|", actorType.GetProperties().OrderBy(p => p.Name).Select(property =>
                property.Name + "=" + (property.Name == "Actor"
                    ? actors.Single(source => source.Address == (nint)property.GetValue(actor)!).Id
                    : property.GetValue(actor))))).ToArray();
        public void Dispose() { Marshal.FreeHGlobal(playerAddress); foreach (var actor in actors) actor.Dispose(); }
    }
}
