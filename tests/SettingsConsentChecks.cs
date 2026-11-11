// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class SettingsConsentChecks
{
    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var type = plugin.GetType("DualSenseLighting.Ui.SettingsUi", true)!;
        var configType = plugin.GetType("DualSenseLighting.Configuration", true)!;
        var schedulerType = plugin.GetType("DualSenseLighting.Core.ConfigurationSaveScheduler", true)!;
        var config = Activator.CreateInstance(configType)!;
        void Flag(string name, bool value) => configType.GetProperty(name)!.SetValue(config, value);
        bool FlagValue(string name) => (bool)configType.GetProperty(name)!.GetValue(config)!;
        Flag("Enabled", false); Flag("HapticEnabled", false);
        var fail = true; var persisted = false; var publications = 0;
        Action save = () => { if (fail) throw new InvalidOperationException("disk denied"); persisted = true; };
        var scheduler = schedulerType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0]
            .Invoke(new object[] { save, (Action<Exception>)(_ => { }) });
        var ui = RuntimeHelpers.GetUninitializedObject(type);
        void Set(string name, object value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ui, value);
        Set("configuration", config); Set("saves", scheduler); Set("Save", save);
        Set("publishSavedSetup", (Action)(() => { check(persisted, "Setup published enabled output before saving consent"); publications++; }));
        Set("setupLighting", true); Set("setupHaptics", true);
        void Invoke(string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui, null);
        void ExpectFailure(string name)
        {
            try { Invoke(name); check(false, "Explicit consent save swallowed a storage failure"); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
        }
        ExpectFailure("PersistSetupChoice");
        check(!FlagValue("HasCompletedSetup") && !FlagValue("HasSeenGeneralInfo") && !FlagValue("Enabled") &&
            !FlagValue("HapticEnabled") && publications == 0, "Failed setup save did not roll back every modified consent/effect flag");
        var diagnostics = ProductionBoundaryChecks.Diagnostics(plugin);
        if (diagnostics)
        {
            ExpectFailure("PersistRecordingConsent");
            check(!FlagValue("HasAcknowledgedRecording"), "Failed recording consent save granted recording permission");
        }
        else check(type.GetMethod("PersistRecordingConsent", BindingFlags.Instance | BindingFlags.NonPublic) == null,
            "Production includes recording consent workflow");
        fail = false; Invoke("PersistSetupChoice");
        check(FlagValue("HasCompletedSetup") && FlagValue("Enabled") && FlagValue("HapticEnabled") && publications == 1,
            "Successful setup consent did not publish the selected effects exactly once");
        if (!diagnostics) return;
        Invoke("PersistRecordingConsent");
        check(FlagValue("HasAcknowledgedRecording"), "Successful recording consent was not retained");
        fail = true; ExpectFailure("PersistRecordingConsent");
        check(FlagValue("HasAcknowledgedRecording"), "Failed re-save cleared previously persisted recording consent");
    }
}
