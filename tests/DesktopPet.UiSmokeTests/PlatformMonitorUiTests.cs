using System.Reflection;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class PlatformMonitorUiTests
{
    public static void Run(MainWindow pet)
    {
        var wasVisible = pet.IsPetVisible;
        var wasRendering = (bool)Get(pet, "_renderLoopRunning")!;
        var previous = Get(pet, "_windowPlatformMonitor");
        var clock = new Clock();
        var scans = 0;
        var publications = 0;
        using var monitor = new WindowPlatformMonitor(() => { scans++; return Array.Empty<Platform>(); }, _ => publications++, clock);
        Set(pet, "_windowPlatformMonitor", monitor);
        try
        {
            pet.SetVisible(true);
            pet.SetVisible(false);
            clock.Timer.FireQueued();
            Check(scans == 0 && clock.Timer.Due == Timeout.InfiniteTimeSpan, "Hidden pet left scanning enabled");
            pet.SetVisible(true);
            Check(clock.Timer.Due == TimeSpan.Zero && scans == 0, "Show failed to request a background refresh");
            clock.Timer.FireQueued();
            Check(scans == 1 && publications == 1 && clock.Timer.Due == TimeSpan.FromMilliseconds(300), "Visibility did not resume the monitor cadence");
            var changes = clock.Timer.Changes;
            pet.SetVisible(true);
            Check(clock.Timer.Changes == changes, "Repeated show rescheduled the monitor");
            pet.SetVisible(false);
            clock.Timer.FireQueued();
            Check(scans == 1 && publications == 1, "Hiding did not suppress an already queued callback");
            Console.WriteLine("PASS Actual pet visibility pauses window scanning, requests immediate background refresh and preserves the single scan cadence");
        }
        finally
        {
            monitor.Pause();
            Set(pet, "_windowPlatformMonitor", previous);
            pet.SetVisible(wasVisible);
            if (!wasRendering) Invoke(pet, "StopRenderLoop");
            ((DispatcherTimer)Get(pet, "_settingsSaveTimer")!).Stop();
            Set(pet, "_settingsDirty", false);
        }
    }

    private static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class Clock : TimeProvider
    {
        public ManualTimer Timer = null!;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => Timer = new ManualTimer(callback, state, dueTime);
    }
    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        public TimeSpan Due { get; private set; } = due;
        public int Changes { get; private set; }
        public void FireQueued() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; Changes++; return true; }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
