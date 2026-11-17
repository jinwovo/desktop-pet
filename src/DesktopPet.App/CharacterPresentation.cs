using System.IO;
using System.Windows.Media.Imaging;
using DesktopPet.Core;

namespace DesktopPet.App;

/// <summary>Decode a complete character before replacing any part of the active presentation.</summary>
public sealed class CharacterPresentation
{
    private CharacterPresentation(CharacterDefinition definition, IReadOnlyDictionary<string, BitmapImage> frames)
    {
        Definition = definition;
        Frames = frames;
    }

    public CharacterDefinition Definition { get; }
    public IReadOnlyDictionary<string, BitmapImage> Frames { get; }

    /// <summary>Decode only the idle PNG for a static gallery card, with dimensions checked before WIC.</summary>
    public static BitmapImage LoadThumbnail(string folder, int maximumPixelSize = 128)
    {
        if (maximumPixelSize is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumPixelSize));
        var definition = CharacterLoader.Load(folder);
        var path = definition.GetFrames(PetState.Idle)[0];
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("갤러리 미리보기에는 PNG 그림이 필요합니다.");
        try
        {
            using var stream = File.OpenRead(path);
            ValidatePngHeader(stream, definition, path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            if (definition.FrameWidth >= definition.FrameHeight) image.DecodePixelWidth = Math.Min(maximumPixelSize, definition.FrameWidth);
            else image.DecodePixelHeight = Math.Min(maximumPixelSize, definition.FrameHeight);
            image.EndInit();
            if (image.PixelWidth > maximumPixelSize || image.PixelHeight > maximumPixelSize)
                throw new InvalidDataException("미리보기 그림의 크기가 올바르지 않습니다.");
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException)
        {
            throw new InvalidDataException($"캐릭터 미리보기를 읽을 수 없습니다. {ex.Message}", ex);
        }
    }

    public static CharacterPresentation Load(string folder, bool includeRasterGait = false, CancellationToken cancellationToken = default)
    {
        var definition = CharacterLoader.Load(folder, cancellationToken);
        var allPaths = definition.AllFramePaths().ToArray();
        var decodedBytes = checked((long)definition.FrameWidth * definition.FrameHeight * 4 * allPaths.Length);
        if (decodedBytes > CharacterLibrary.MaxDecodedBytes)
            throw new InvalidDataException("캐릭터 전체 이미지 크기는 64 MiB 이하여야 합니다.");
        // Validate every declared PNG before the first decoder is created, even when its raster
        // fallback is not needed by a live rig. A falsely small manifest cannot hide a huge image.
        foreach (var path in allPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var headerStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32, FileOptions.SequentialScan);
                ValidatePngHeader(headerStream, definition, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException)
            {
                throw new InvalidDataException($"캐릭터 이미지 '{Path.GetFileName(path)}'을 읽을 수 없습니다. {ex.Message}", ex);
            }
        }
        var frames = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in definition.GetPresentationFramePaths(includeRasterGait))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = File.OpenRead(path);
                // Check selected files again through the actual decoder stream so a concurrent
                // replacement between the global header pass and decoding cannot evade limits.
                if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                    ValidatePngHeader(stream, definition, path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                if (bitmap.PixelWidth != definition.FrameWidth || bitmap.PixelHeight != definition.FrameHeight)
                    throw new InvalidDataException($"프레임 크기가 character.json과 다릅니다: {Path.GetFileName(path)}");
                bitmap.Freeze();
                frames.Add(path, bitmap);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException)
            {
                throw new InvalidDataException($"캐릭터 이미지 '{Path.GetFileName(path)}'을 읽을 수 없습니다. {ex.Message}", ex);
            }
        }
        return new CharacterPresentation(definition, frames);
    }

    private static void ValidatePngHeader(Stream stream, CharacterDefinition definition, string path)
    {
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        ReadOnlySpan<byte> signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (!header[..8].SequenceEqual(signature) ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) != 13 ||
            !header[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException($"PNG 헤더가 올바르지 않습니다: {Path.GetFileName(path)}");
        var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        if (width != (uint)definition.FrameWidth || height != (uint)definition.FrameHeight)
            throw new InvalidDataException($"PNG 헤더 크기가 character.json과 다릅니다: {Path.GetFileName(path)}");
        stream.Position = 0;
    }
}
