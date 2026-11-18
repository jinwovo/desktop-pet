using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopPet.App;

/// <summary>Premultiplied pixel interpolation avoids the opacity dip of two translucent drawing banks.</summary>
internal sealed class RigTextureBlend
{
    private readonly BitmapSource _sideSource, _turnSource;
    private readonly byte[] _side, _turn, _pixels;
    private readonly int[] _changed;
    private readonly WriteableBitmap _scratch;
    private readonly int _stride;
    private int _lastWeight = -1;
    public RigTextureBlend(BitmapSource side, BitmapSource turn)
    {
        _sideSource = side; _turnSource = turn;
        var width = side.PixelWidth; var height = side.PixelHeight;
        if (turn.PixelWidth != width || turn.PixelHeight != height) throw new System.IO.InvalidDataException("Rig view sizes differ.");
        _stride = checked(width * 4);
        byte[] Read(BitmapSource source)
        {
            var converted = source.Format == PixelFormats.Pbgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            var bytes = new byte[checked(_stride * height)]; converted.CopyPixels(bytes, _stride, 0); return bytes;
        }
        _side = Read(side); _turn = Read(turn); _pixels = (byte[])_side.Clone();
        var changed = new List<int>();
        for (var i = 0; i < _side.Length; i += 4)
            if (_side[i] != _turn[i] || _side[i + 1] != _turn[i + 1] || _side[i + 2] != _turn[i + 2] || _side[i + 3] != _turn[i + 3]) changed.Add(i);
        _changed = changed.ToArray();
        _scratch = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
    }
    public ImageSource At(double turnWeight)
    {
        var weight = (int)Math.Round(Math.Clamp(turnWeight, 0, 1) * 256);
        if (weight == 0) return _sideSource;
        if (weight == 256) return _turnSource;
        if (weight != _lastWeight)
        {
            foreach (var pixel in _changed)
                for (var channel = 0; channel < 4; channel++)
                    _pixels[pixel + channel] = (byte)((_side[pixel + channel] * (256 - weight) + _turn[pixel + channel] * weight + 128) >> 8);
            _scratch.WritePixels(new Int32Rect(0, 0, _scratch.PixelWidth, _scratch.PixelHeight), _pixels, _stride, 0);
            _lastWeight = weight;
        }
        return _scratch;
    }
}
