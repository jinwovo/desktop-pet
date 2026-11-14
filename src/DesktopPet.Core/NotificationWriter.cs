using System.Text;
using System.Text.Json;

namespace DesktopPet.Core;

/// <summary>Publishes a local receipt atomically; the existing inbox owns delivery and acknowledgement.</summary>
public static class NotificationWriter
{
    public static string Publish(string inboxDirectory, PetNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (string.IsNullOrWhiteSpace(inboxDirectory) || !Path.IsPathFullyQualified(inboxDirectory))
            throw new ArgumentException("알림 보관 폴더의 절대 경로를 지정해주세요.", nameof(inboxDirectory));
        var folder = Path.GetFullPath(inboxDirectory);
        Directory.CreateDirectory(folder);
        var id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");
        var temporary = Path.Combine(folder, id + ".tmp");
        var destination = Path.Combine(folder, id + ".json");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(notification), new UTF8Encoding(false));
            File.Move(temporary, destination);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
