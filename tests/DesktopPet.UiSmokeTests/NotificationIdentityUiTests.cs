using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core;

internal static class NotificationIdentityUiTests
{
    public static void Run(string root)
    {
        var folder = Path.Combine(root, "notification-identity-inbox");
        var pet = new MainWindow(new CharacterLibrary(MainWindow.CharactersRoot, Path.Combine(root, "identity-custom")), inboxDirectory: folder);
        var loaded = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), pet,
            typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!);
        pet.Loaded -= loaded;
        Set(pet, "_settings", new PetSettings { Visible = false, QuietMode = true, AutoDismissNotifications = false, AutoConnectAssistants = false });
        Set(pet, "_brain", new PetBrain(new PlatformLayout(new[] { new Platform(0, 1920, 1080) }), 128, 122, 500, 958));
        Invoke(pet, "ApplyPresentation", CharacterPresentation.Load(Path.Combine(MainWindow.CharactersRoot, "Companion")));
        Set(pet, "_suppressionReasons", PetSuppressionReason.SessionLocked); // Keep failed-ack review isolated from visible desktop windows.
        var save = (DispatcherTimer)Get(pet, "_settingsSaveTimer")!;
        save.Interval = TimeSpan.FromDays(1);
        var deliveries = new ConcurrentQueue<InboxNotification>();
        using var inbox = new NotificationInbox(folder);
        inbox.NotificationReceived += deliveries.Enqueue;
        inbox.Start();
        List<InboxNotification> Take(int count)
        {
            Check(SpinWait.SpinUntil(() => deliveries.Count >= count, TimeSpan.FromSeconds(15)), "Identity fixture receipts did not arrive");
            var result = new List<InboxNotification>();
            while (deliveries.TryDequeue(out var receipt)) result.Add(receipt);
            Check(result.Count == count, "Unexpected extra identity fixture delivery");
            return result;
        }
        void Deliver(InboxNotification receipt) => Invoke(pet, "OnNotification", receipt);
        string Publish(string source, string id, string message = "body") => NotificationWriter.Publish(folder,
            new PetNotification { Source = source, EventId = id, Title = "Same title", Message = message });
        void Duplicate(string source, string id)
        {
            var before = pet.PendingNotificationCount;
            var path = Publish(source, id, "duplicate");
            Deliver(Take(1).Single());
            Check(pet.PendingNotificationCount == before && !File.Exists(path), "Duplicate of an unread receipt reentered the queue");
        }
        try
        {
            var first = Publish("tool:turn", "one", "first identity");
            var second = Publish("tool", "turn:one", "second identity");
            var pair = Take(2);
            Deliver(pair.Single(r => r.Notification.Source == "tool:turn"));
            Deliver(pair.Single(r => r.Notification.Source == "tool"));
            var pairsPreserved = pet.PendingNotificationCount == 2 && File.Exists(first) && File.Exists(second);
            for (var i = 0; i < 513; i++) Publish("filler", "id-" + i);
            foreach (var receipt in Take(513)) Deliver(receipt);
            var beforeReplay = pet.PendingNotificationCount;
            var replay = Publish("tool:turn", "one", "late retransmission");
            Deliver(Take(1).Single());
            var unreadProtected = pet.PendingNotificationCount == beforeReplay && !File.Exists(replay);
            Check(pairsPreserved && unreadProtected,
                $"Notification identity failed: distinct delimiter-containing pairs preserved={pairsPreserved}, unread duplicates protected after history eviction={unreadProtected}");
            var beforeAck = pet.PendingNotificationCount;
            using (var locked = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Invoke(pet, "AcknowledgeNotification");
                Check(pet.PendingNotificationCount == beforeAck && File.Exists(first), "Failed acknowledgement removed the original receipt");
                Duplicate("tool:turn", "one");
            }
            Invoke(pet, "AcknowledgeNotification");
            Check(pet.PendingNotificationCount == beforeAck - 1 && !File.Exists(first) && File.Exists(second), "Successful acknowledgement removed the wrong identity");
            Duplicate("tool", "turn:one");
            while (pet.PendingNotificationCount > 0) Invoke(pet, "AcknowledgeNotification");
            Check(Directory.GetFiles(folder, "*.json").Length == 0, "Acknowledged fixture receipts remain");
            Publish("tool", "", "without event id one"); Publish("tool", "", "without event id two");
            foreach (var receipt in Take(2)) Deliver(receipt);
            Check(pet.PendingNotificationCount == 2, "Independent notifications without event IDs were deduplicated");
            while (pet.PendingNotificationCount > 0) Invoke(pet, "AcknowledgeNotification");
            Console.WriteLine("PASS Notification source/event pairs cannot collide, unread identities survive recent-history eviction and failed acknowledgements, and anonymous events stay distinct");
        }
        finally { save.Stop(); Set(pet, "_settingsDirty", false); pet.DisposeResources(); pet.Close(); }
    }
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
