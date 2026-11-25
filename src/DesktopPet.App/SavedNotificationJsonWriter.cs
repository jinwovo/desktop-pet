namespace DesktopPet.App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public static class SavedNotificationJsonWriter
{
    public static void Save(IReadOnlyList<SavedNotificationItem> snapshot, string destination, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        token.ThrowIfCancellationRequested();

        var payload = new
        {
            schemaVersion = 1,
            notifications = snapshot.Select(item => new
            {
                title = item.Title,
                message = item.Message,
                source = item.Source,
                createdAtUtc = item.CreatedAtUtc
            })
        };

        destination = Path.GetFullPath(destination);
        string directory = Path.GetDirectoryName(destination)!;
        string tempPath = Path.Combine(directory, $"DesktopPet-export-{Guid.NewGuid():N}.tmp");
        bool tempCreated = false;

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                tempCreated = true;
                JsonSerializer.Serialize(stream, payload, new JsonSerializerOptions { WriteIndented = true });
            }

            token.ThrowIfCancellationRequested();

            File.Move(tempPath, destination, overwrite: false);
        }
        finally
        {
            if (tempCreated && File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
