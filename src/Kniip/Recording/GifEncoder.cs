using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kniip.Recording;

/// <summary>
/// Geanimeerde GIF-encoder voor schermopnames. Elk frame krijgt een eigen palet van maximaal 255 kleuren
/// (exact als het frame zo weinig kleuren heeft, anders via octree-quantisatie). Alleen het gewijzigde deel
/// wordt opgeslagen en ongewijzigde pixels daarin zijn transparant, zodat bestanden klein blijven.
/// </summary>
internal sealed class GifEncoder : IDisposable
{
    private const int TransparentIndex = 255;
    private const int MaxColors = 255;

    private readonly Stream _stream;
    private readonly int _width;
    private readonly int _height;
    private byte[]? _previous;
    private bool _finished;

    public GifEncoder(Stream stream, int width, int height)
    {
        _stream = stream;
        _width = width;
        _height = height;
        WriteHeader();
    }

    /// <summary>Voegt een BGRA-frame toe dat <paramref name="delayCentiseconds"/> zichtbaar blijft.</summary>
    public void AddFrame(byte[] bgra, int delayCentiseconds)
    {
        int left = 0, top = 0, right = _width - 1, bottom = _height - 1;
        if (_previous != null && !FindChangedArea(bgra, _previous, out left, out top, out right, out bottom))
        {
            // Niets veranderd: één transparante pixel zodat de vertraging behouden blijft.
            left = top = right = bottom = 0;
        }

        int areaWidth = right - left + 1;
        int areaHeight = bottom - top + 1;
        var argb = new int[areaWidth * areaHeight];
        var changed = new bool[argb.Length];
        for (int y = 0; y < areaHeight; y++)
        {
            int rowOffset = ((top + y) * _width + left) * 4;
            for (int x = 0; x < areaWidth; x++)
            {
                int i = rowOffset + x * 4;
                int color = (bgra[i + 2] << 16) | (bgra[i + 1] << 8) | bgra[i];
                argb[y * areaWidth + x] = color;
                changed[y * areaWidth + x] = _previous == null
                                              || bgra[i] != _previous[i] || bgra[i + 1] != _previous[i + 1] || bgra[i + 2] != _previous[i + 2];
            }
        }

        (int[] palette, byte[] indices) = Quantize(argb, changed);
        WriteFrame(left, top, areaWidth, areaHeight, palette, indices, Math.Max(2, delayCentiseconds), _previous != null);
        _previous = bgra;
    }

    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _stream.WriteByte(0x3B);
        _stream.Flush();
    }

    public void Dispose() => Finish();

    private bool FindChangedArea(byte[] current, byte[] previous, out int left, out int top, out int right, out int bottom)
    {
        left = _width;
        top = _height;
        right = -1;
        bottom = -1;
        for (int y = 0; y < _height; y++)
        {
            int row = y * _width * 4;
            for (int x = 0; x < _width; x++)
            {
                int i = row + x * 4;
                if (current[i] != previous[i] || current[i + 1] != previous[i + 1] || current[i + 2] != previous[i + 2])
                {
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    bottom = y;
                }
            }
        }

        return right >= 0;
    }

    private static (int[] Palette, byte[] Indices) Quantize(int[] colors, bool[] changed)
    {
        var exact = new Dictionary<int, byte>();
        bool fits = true;
        for (int i = 0; i < colors.Length && fits; i++)
        {
            if (changed[i] && !exact.ContainsKey(colors[i]))
            {
                if (exact.Count == MaxColors)
                {
                    fits = false;
                }
                else
                {
                    exact[colors[i]] = (byte)exact.Count;
                }
            }
        }

        int[] palette;
        Func<int, byte> lookup;
        if (fits)
        {
            palette = new int[exact.Count];
            foreach (KeyValuePair<int, byte> entry in exact)
            {
                palette[entry.Value] = entry.Key;
            }

            lookup = color => exact[color];
        }
        else
        {
            var octree = new Octree();
            for (int i = 0; i < colors.Length; i++)
            {
                if (changed[i])
                {
                    octree.Add(colors[i]);
                }
            }

            palette = octree.BuildPalette(MaxColors);
            var cache = new Dictionary<int, byte>();
            int[] localPalette = palette;
            lookup = color =>
            {
                if (!cache.TryGetValue(color, out byte index))
                {
                    index = Nearest(localPalette, color);
                    cache[color] = index;
                }

                return index;
            };
        }

        var indices = new byte[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            indices[i] = changed[i] ? lookup(colors[i]) : (byte)TransparentIndex;
        }

        return (palette, indices);
    }

    private static byte Nearest(int[] palette, int color)
    {
        int r = (color >> 16) & 0xFF, g = (color >> 8) & 0xFF, b = color & 0xFF;
        int best = 0, bestDistance = int.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            int pr = (palette[i] >> 16) & 0xFF, pg = (palette[i] >> 8) & 0xFF, pb = palette[i] & 0xFF;
            int distance = (r - pr) * (r - pr) * 2 + (g - pg) * (g - pg) * 4 + (b - pb) * (b - pb) * 3;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return (byte)best;
    }

    private void WriteHeader()
    {
        _stream.Write(Encoding.ASCII.GetBytes("GIF89a"));
        WriteUInt16(_width);
        WriteUInt16(_height);
        _stream.WriteByte(0x70); // geen globaal palet, 8 bits kleurresolutie
        _stream.WriteByte(0);
        _stream.WriteByte(0);

        // NETSCAPE2.0: oneindig herhalen.
        _stream.Write(new byte[] { 0x21, 0xFF, 0x0B });
        _stream.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        _stream.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
    }

    private void WriteFrame(int left, int top, int width, int height, int[] palette, byte[] indices, int delay, bool transparent)
    {
        // Graphic Control Extension: frame laten staan (disposal 1), eventueel met transparante index.
        _stream.Write(new byte[] { 0x21, 0xF9, 0x04, (byte)((1 << 2) | (transparent ? 1 : 0)) });
        WriteUInt16(delay);
        _stream.WriteByte(TransparentIndex);
        _stream.WriteByte(0);

        _stream.WriteByte(0x2C);
        WriteUInt16(left);
        WriteUInt16(top);
        WriteUInt16(width);
        WriteUInt16(height);
        _stream.WriteByte(0x87); // lokaal palet van 256 kleuren

        var table = new byte[256 * 3];
        for (int i = 0; i < palette.Length; i++)
        {
            table[i * 3] = (byte)(palette[i] >> 16);
            table[i * 3 + 1] = (byte)(palette[i] >> 8);
            table[i * 3 + 2] = (byte)palette[i];
        }

        _stream.Write(table);
        LzwEncoder.Encode(_stream, indices);
    }

    private void WriteUInt16(int value)
    {
        _stream.WriteByte((byte)(value & 0xFF));
        _stream.WriteByte((byte)((value >> 8) & 0xFF));
    }

    /// <summary>Klassieke octree-kleurreductie.</summary>
    private sealed class Octree
    {
        private readonly List<Node>[] _levels = new List<Node>[8];
        private readonly Node _root;
        private int _leafCount;

        public Octree()
        {
            for (int i = 0; i < _levels.Length; i++)
            {
                _levels[i] = new List<Node>();
            }

            _root = new Node();
        }

        public void Add(int color)
        {
            int r = (color >> 16) & 0xFF, g = (color >> 8) & 0xFF, b = color & 0xFF;
            Node node = _root;
            for (int level = 0; level < 8; level++)
            {
                if (node.IsLeaf)
                {
                    break;
                }

                int shift = 7 - level;
                int index = (((r >> shift) & 1) << 2) | (((g >> shift) & 1) << 1) | ((b >> shift) & 1);
                node.Children ??= new Node?[8];
                Node? child = node.Children[index];
                if (child == null)
                {
                    child = new Node { IsLeaf = level == 7 };
                    node.Children[index] = child;
                    if (child.IsLeaf)
                    {
                        _leafCount++;
                    }
                    else
                    {
                        _levels[level + 1].Add(child);
                    }
                }

                node = child;
            }

            node.Red += r;
            node.Green += g;
            node.Blue += b;
            node.Count++;
        }

        public int[] BuildPalette(int maxColors)
        {
            for (int level = 7; level > 0 && _leafCount > maxColors; level--)
            {
                List<Node> nodes = _levels[level];
                nodes.Sort((a, b) => a.TotalCount().CompareTo(b.TotalCount()));
                foreach (Node node in nodes)
                {
                    if (_leafCount <= maxColors)
                    {
                        break;
                    }

                    _leafCount -= node.Merge() - 1;
                }
            }

            var palette = new List<int>();
            Collect(_root, palette);
            if (palette.Count > maxColors)
            {
                palette.RemoveRange(maxColors, palette.Count - maxColors);
            }

            return palette.ToArray();
        }

        private static void Collect(Node node, List<int> palette)
        {
            if (node.IsLeaf)
            {
                if (node.Count > 0)
                {
                    palette.Add(((int)(node.Red / node.Count) << 16) | ((int)(node.Green / node.Count) << 8)
                                                                      | (int)(node.Blue / node.Count));
                }

                return;
            }

            if (node.Children == null)
            {
                return;
            }

            foreach (Node? child in node.Children)
            {
                if (child != null)
                {
                    Collect(child, palette);
                }
            }
        }

        private sealed class Node
        {
            public bool IsLeaf;
            public long Red, Green, Blue, Count;
            public Node?[]? Children;

            public long TotalCount()
            {
                if (IsLeaf || Children == null)
                {
                    return Count;
                }

                long total = 0;
                foreach (Node? child in Children)
                {
                    if (child != null)
                    {
                        total += child.TotalCount();
                    }
                }

                return total;
            }

            /// <summary>Voegt de kinderen samen tot dit blad; geeft het aantal verdwenen bladeren terug.</summary>
            public int Merge()
            {
                if (IsLeaf || Children == null)
                {
                    return 1;
                }

                int leaves = 0;
                foreach (Node? child in Children)
                {
                    if (child == null)
                    {
                        continue;
                    }

                    int childLeaves = child.Merge();
                    leaves += childLeaves;
                    Red += child.Red;
                    Green += child.Green;
                    Blue += child.Blue;
                    Count += child.Count;
                }

                Children = null;
                IsLeaf = true;
                return Math.Max(leaves, 1);
            }
        }
    }

    /// <summary>GIF-variant van LZW (variabele codelengte tot 12 bits, LSB eerst, blokken van 255 bytes).</summary>
    private static class LzwEncoder
    {
        private const int MinCodeSize = 8;
        private const int ClearCode = 1 << MinCodeSize;
        private const int EndCode = ClearCode + 1;
        private const int MaxCode = 4095;

        public static void Encode(Stream stream, byte[] pixels)
        {
            stream.WriteByte(MinCodeSize);
            var output = new BlockWriter(stream);
            var table = new Dictionary<int, int>(4096);
            int codeSize = MinCodeSize + 1;
            int nextCode = EndCode + 1;

            output.Write(ClearCode, codeSize);
            if (pixels.Length == 0)
            {
                output.Write(EndCode, codeSize);
                output.Flush();
                return;
            }

            int prefix = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                int key = (prefix << 8) | pixels[i];
                if (table.TryGetValue(key, out int code))
                {
                    prefix = code;
                    continue;
                }

                output.Write(prefix, codeSize);
                if (nextCode <= MaxCode)
                {
                    table[key] = nextCode++;
                    if (nextCode > (1 << codeSize) && codeSize < 12)
                    {
                        codeSize++;
                    }
                }
                else
                {
                    output.Write(ClearCode, codeSize);
                    table.Clear();
                    codeSize = MinCodeSize + 1;
                    nextCode = EndCode + 1;
                }

                prefix = pixels[i];
            }

            output.Write(prefix, codeSize);
            // De decoder voegt na deze laatste code nog een tabelregel toe en kan daardoor een bit breder lezen.
            if (nextCode >= (1 << codeSize) && codeSize < 12)
            {
                codeSize++;
            }

            output.Write(EndCode, codeSize);
            output.Flush();
        }

        private sealed class BlockWriter
        {
            private readonly Stream _stream;
            private readonly byte[] _block = new byte[255];
            private int _blockLength;
            private int _bitBuffer;
            private int _bitCount;

            public BlockWriter(Stream stream) => _stream = stream;

            public void Write(int code, int size)
            {
                _bitBuffer |= code << _bitCount;
                _bitCount += size;
                while (_bitCount >= 8)
                {
                    AddByte((byte)(_bitBuffer & 0xFF));
                    _bitBuffer >>= 8;
                    _bitCount -= 8;
                }
            }

            public void Flush()
            {
                if (_bitCount > 0)
                {
                    AddByte((byte)(_bitBuffer & 0xFF));
                    _bitBuffer = 0;
                    _bitCount = 0;
                }

                WriteBlock();
                _stream.WriteByte(0);
            }

            private void AddByte(byte value)
            {
                _block[_blockLength++] = value;
                if (_blockLength == _block.Length)
                {
                    WriteBlock();
                }
            }

            private void WriteBlock()
            {
                if (_blockLength == 0)
                {
                    return;
                }

                _stream.WriteByte((byte)_blockLength);
                _stream.Write(_block, 0, _blockLength);
                _blockLength = 0;
            }
        }
    }
}
