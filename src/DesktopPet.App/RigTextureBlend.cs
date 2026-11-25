using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopPet.App;

/// <summary>Premultiplied pixel interpolation avoids the opacity dip of two translucent drawing banks.</summary>
internal sealed class RigTextureBlend
{
    private readonly BitmapSource _sideSource, _turnSource;
    private readonly byte[] _side, _turn, _pixels;
    private readonly byte[]? _sideMirrored, _turnMirrored;
    private readonly BitmapSource? _sideMirroredSource, _turnMirroredSource;
    private readonly byte[]? _pivot, _pivotMirrored;
    private readonly BitmapSource? _pivotSource, _pivotMirroredSource;
    private readonly int[] _changed;
    private readonly Int32Rect _changedBounds;
    private readonly WriteableBitmap _scratch;
    private readonly int _stride;
    private int _lastWeight = -1;
    private int _lastMirrorWeight = -1;
    private bool _lastPivotMirrored;
    public RigTextureBlend(BitmapSource side, BitmapSource turn, BitmapSource? pivotSource = null)
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
        if (pivotSource is not null)
        {
            byte[] Mirror(byte[] input)
            {
                var output = new byte[input.Length];
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                        Buffer.BlockCopy(input, y * _stride + x * 4, output, y * _stride + (width - x - 1) * 4, 4);
                return output;
            }
            BitmapSource Frozen(byte[] pixels)
            {
                var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, _stride);
                source.Freeze(); return source;
            }
            _sideMirrored = Mirror(_side); _turnMirrored = Mirror(_turn);
            _sideMirroredSource = Frozen(_sideMirrored); _turnMirroredSource = Frozen(_turnMirrored);
            if (pivotSource.PixelWidth != width || pivotSource.PixelHeight != height)
                throw new System.IO.InvalidDataException("Rig pivot size differs.");
            _pivot = Read(pivotSource);
            // Rig body layers end above the separate feet. Keep only that same upper region
            // from the cached frontal idle frame, so the pivot cannot draw a second pair of feet.
            var bottom = -1;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (_side[y * _stride + x * 4 + 3] > 0 || _turn[y * _stride + x * 4 + 3] > 0) bottom = y;
            Array.Clear(_pivot, (bottom + 1) * _stride, (height - bottom - 1) * _stride);
            _pivotMirrored = Mirror(_pivot);
            _pivotSource = Frozen(_pivot); _pivotMirroredSource = Frozen(_pivotMirrored);
        }
        var changed = new List<int>();
        var minX = width;
        var maxX = -1;
        for (var i = 0; i < _side.Length; i += 4)
        {
            var differs = false;
            for (var c = 0; c < 4; c++)
                differs |= _side[i + c] != _turn[i + c] || _sideMirrored is not null &&
                    (_side[i + c] != _sideMirrored[i + c] || _side[i + c] != _turnMirrored![i + c] ||
                     _side[i + c] != _pivot![i + c] || _side[i + c] != _pivotMirrored![i + c]);
            if (!differs) continue;
            changed.Add(i);
            var x = i % _stride / 4;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
        }
        _changed = changed.ToArray();
        if (_changed.Length > 0)
        {
            var top = _changed[0] / _stride;
            var bottom = _changed[^1] / _stride;
            _changedBounds = new Int32Rect(minX, top, maxX - minX + 1, bottom - top + 1);
        }
        _scratch = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
    }
    public ImageSource At(double turnWeight, double mirrorWeight = 0, bool pivotMirrored = false)
    {
        var weight = (int)Math.Round(Math.Clamp(turnWeight, 0, 1) * 256);
        var mirror = _sideMirrored is null ? 0 : (int)Math.Round(Math.Clamp(mirrorWeight, 0, 1) * 256);
        if (mirror == 0 && weight == 0) return _sideSource;
        if (mirror == 0 && weight == 256) return _turnSource;
        if (mirror == 256 && weight == 0) return _sideMirroredSource!;
        if (mirror == 256 && weight == 256) return _turnMirroredSource!;
        if (mirror == 128) return pivotMirrored ? _pivotMirroredSource! : _pivotSource!;
        if (weight != _lastWeight || mirror != _lastMirrorWeight || pivotMirrored != _lastPivotMirrored)
        {
            foreach (var pixel in _changed)
                for (var channel = 0; channel < 4; channel++)
                {
                    var i = pixel + channel;
                    var normal = (_side[i] * (256 - weight) + _turn[i] * weight + 128) >> 8;
                    if (mirror == 0) _pixels[i] = (byte)normal;
                    else
                    {
                        var pivot = (pivotMirrored ? _pivotMirrored! : _pivot!)[i];
                        var reflected = (_sideMirrored![i] * (256 - weight) + _turnMirrored![i] * weight + 128) >> 8;
                        _pixels[i] = mirror < 128
                            ? (byte)((normal * (256 - 2 * mirror) + pivot * (2 * mirror) + 128) >> 8)
                            : (byte)((pivot * (512 - 2 * mirror) + reflected * (2 * mirror - 256) + 128) >> 8);
                    }
                }
            // Initialize unchanged pixels once. Later blends only overwrite the region that can differ.
            if (_lastWeight < 0)
                _scratch.WritePixels(new Int32Rect(0, 0, _scratch.PixelWidth, _scratch.PixelHeight), _pixels, _stride, 0, 0);
            else if (!_changedBounds.IsEmpty)
                _scratch.WritePixels(_changedBounds, _pixels, _stride, _changedBounds.X, _changedBounds.Y);
            _lastWeight = weight; _lastMirrorWeight = mirror; _lastPivotMirrored = pivotMirrored;
        }
        return _scratch;
    }
}
