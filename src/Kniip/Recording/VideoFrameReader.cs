using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kniip.Recording;

/// <summary>Een gedecodeerd videoframe in BGRA (32 bits per pixel, van boven naar onder).</summary>
internal sealed record VideoFrame(byte[] Pixels, int Width, int Height, TimeSpan Time);

/// <summary>
/// Leest frames verliesvrij uit een video via de Media Foundation Source Reader
/// (met de ingebouwde omzetting naar RGB32).
/// </summary>
internal sealed class VideoFrameReader : IDisposable
{
    private const uint MfVersion = 0x00020070;
    private const int FirstVideoStream = unchecked((int)0xFFFFFFFC);
    private const int AllStreams = unchecked((int)0xFFFFFFFE);
    private const int MediaSource = unchecked((int)0xFFFFFFFF);
    private const int EndOfStream = 0x2;
    private const ushort VtI8 = 20;

    private static readonly Guid MfSourceReaderEnableAdvancedVideoProcessing = new("0f81da2c-b537-4672-a8b2-a681b17307a3");
    private static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MfMtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MfMtDefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid MfMtMinimumDisplayAperture = new("d7388766-18fe-48c6-a177-ee894867c8c4");
    private static readonly Guid MfPdDuration = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
    private static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatRgb32 = new("00000016-0000-0010-8000-00AA00389B71");

    private readonly IMFSourceReader _reader;
    private readonly int _bufferWidth;
    private readonly int _bufferHeight;
    private readonly int _stride;
    private readonly int _cropX;
    private readonly int _cropY;

    public VideoFrameReader(string path)
    {
        Check(MFStartup(MfVersion, 0));
        Check(MFCreateAttributes(out IMFAttributes attributes, 1));
        try
        {
            Guid key = MfSourceReaderEnableAdvancedVideoProcessing;
            attributes.SetUINT32(ref key, 1);
            Check(MFCreateSourceReaderFromURL(path, attributes, out _reader));
        }
        finally
        {
            Marshal.ReleaseComObject(attributes);
        }

        _reader.SetStreamSelection(AllStreams, false);
        _reader.SetStreamSelection(FirstVideoStream, true);

        _reader.GetNativeMediaType(FirstVideoStream, 0, out IMFMediaType native);
        (int apertureX, int apertureY, int apertureWidth, int apertureHeight)? aperture = ReadAperture(native);
        Marshal.ReleaseComObject(native);

        Check(MFCreateMediaType(out IMFMediaType requested));
        try
        {
            Guid major = MfMtMajorType, video = MfMediaTypeVideo, subtype = MfMtSubtype, rgb32 = MfVideoFormatRgb32;
            requested.SetGUID(ref major, ref video);
            requested.SetGUID(ref subtype, ref rgb32);
            _reader.SetCurrentMediaType(FirstVideoStream, IntPtr.Zero, requested);
        }
        finally
        {
            Marshal.ReleaseComObject(requested);
        }

        _reader.GetCurrentMediaType(FirstVideoStream, out IMFMediaType current);
        try
        {
            Guid frameSize = MfMtFrameSize;
            current.GetUINT64(ref frameSize, out long packed);
            _bufferWidth = (int)(packed >> 32);
            _bufferHeight = (int)(packed & 0xFFFFFFFF);

            Guid strideKey = MfMtDefaultStride;
            _stride = current.GetUINT32(ref strideKey, out int stride) == 0 ? stride : _bufferWidth * 4;
            aperture ??= ReadAperture(current);
        }
        finally
        {
            Marshal.ReleaseComObject(current);
        }

        Width = _bufferWidth;
        Height = _bufferHeight;
        if (aperture is var (x, y, w, h) && w > 0 && h > 0 && x + w <= _bufferWidth && y + h <= _bufferHeight)
        {
            _cropX = x;
            _cropY = y;
            Width = w;
            Height = h;
        }

        var key2 = MfPdDuration;
        if (_reader.GetPresentationAttribute(MediaSource, ref key2, out PropVariant duration) == 0)
        {
            Duration = TimeSpan.FromTicks(duration.Value);
        }
    }

    public int Width { get; }

    public int Height { get; }

    public TimeSpan Duration { get; }

    /// <summary>Alle frames vanaf (ongeveer) <paramref name="from"/>; het eerste frame kan iets eerder liggen.</summary>
    public IEnumerable<VideoFrame> ReadFrames(TimeSpan from, CancellationToken cancellationToken)
    {
        var position = new PropVariant { Type = VtI8, Value = Math.Max(0, from.Ticks) };
        Guid timeFormat = Guid.Empty;
        _reader.SetCurrentPosition(ref timeFormat, ref position);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _reader.ReadSample(FirstVideoStream, 0, out _, out int flags, out long timestamp, out IMFSample? sample);
            if (sample == null)
            {
                if ((flags & EndOfStream) != 0)
                {
                    yield break;
                }

                continue;
            }

            byte[] pixels;
            try
            {
                pixels = CopyPixels(sample);
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }

            yield return new VideoFrame(pixels, Width, Height, TimeSpan.FromTicks(timestamp));
            if ((flags & EndOfStream) != 0)
            {
                yield break;
            }
        }
    }

    private byte[] CopyPixels(IMFSample sample)
    {
        sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        try
        {
            buffer.Lock(out IntPtr data, out _, out int length);
            try
            {
                int stride = Math.Abs(_stride);
                bool bottomUp = _stride < 0;
                int rowBytes = Width * 4;
                var pixels = new byte[rowBytes * Height];
                for (int row = 0; row < Height; row++)
                {
                    int sourceRow = _cropY + row;
                    if (bottomUp)
                    {
                        sourceRow = _bufferHeight - 1 - sourceRow;
                    }

                    long offset = (long)sourceRow * stride + _cropX * 4;
                    if (offset + rowBytes > length)
                    {
                        break;
                    }

                    Marshal.Copy(data + (int)offset, pixels, row * rowBytes, rowBytes);
                }

                return pixels;
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }
    }

    private static (int, int, int, int)? ReadAperture(IMFMediaType type)
    {
        Guid key = MfMtMinimumDisplayAperture;
        var blob = new byte[16];
        if (type.GetBlob(ref key, blob, blob.Length, out int size) != 0 || size < 16)
        {
            return null;
        }

        // MFVideoArea: twee MFOffset's (fract ushort + value short) en een SIZE.
        int x = BitConverter.ToInt16(blob, 2);
        int y = BitConverter.ToInt16(blob, 6);
        int width = BitConverter.ToInt32(blob, 8);
        int height = BitConverter.ToInt32(blob, 12);
        return (x, y, width, height);
    }

    private static void Check(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(_reader);
        MFShutdown();
    }

    [DllImport("mfplat.dll")]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll")]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll")]
    private static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateMediaType(out IMFMediaType mediaType);

    [DllImport("mfreadwrite.dll")]
    private static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url,
        IMFAttributes attributes, out IMFSourceReader reader);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public long Value;
    }

    // De COM-interfaces hieronder volgen de vtable-volgorde van de Windows SDK; methoden die kniip niet
    // gebruikt staan er als lege plaatshouder in zodat de volgorde klopt.

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        void GetItem(); void GetItemType(); void CompareItem(); void Compare();
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
        void GetBlobSize();
        [PreserveSig] int GetBlob(ref Guid key, [Out] byte[] buffer, int bufferSize, out int blobSize);
        void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem(); void DeleteAllItems();
        void SetUINT32(ref Guid key, int value);
        void SetUINT64(); void SetDouble();
        void SetGUID(ref Guid key, ref Guid value);
        void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore();
        void GetCount(); void GetItemByIndex(); void CopyAllItems();
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        void GetItem(); void GetItemType(); void CompareItem(); void Compare();
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        void GetUINT64(ref Guid key, out long value);
        void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
        void GetBlobSize();
        [PreserveSig] int GetBlob(ref Guid key, [Out] byte[] buffer, int bufferSize, out int blobSize);
        void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem(); void DeleteAllItems();
        void SetUINT32(ref Guid key, int value);
        void SetUINT64(); void SetDouble();
        void SetGUID(ref Guid key, ref Guid value);
        void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore();
        void GetCount(); void GetItemByIndex(); void CopyAllItems();
        void GetMajorType(); void IsCompressedFormat(); void IsEqual(); void GetRepresentation(); void FreeRepresentation();
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64();
        void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
        void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
        void DeleteAllItems(); void SetUINT32(); void SetUINT64(); void SetDouble(); void SetGUID(); void SetString();
        void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex();
        void CopyAllItems();
        void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime(); void SetSampleTime(); void GetSampleDuration();
        void SetSampleDuration(); void GetBufferCount(); void GetBufferByIndex();
        void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        void Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        void Unlock();
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSourceReader
    {
        void GetStreamSelection();
        void SetStreamSelection(int streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);
        void GetNativeMediaType(int streamIndex, int mediaTypeIndex, out IMFMediaType mediaType);
        void GetCurrentMediaType(int streamIndex, out IMFMediaType mediaType);
        void SetCurrentMediaType(int streamIndex, IntPtr reserved, IMFMediaType mediaType);
        void SetCurrentPosition(ref Guid timeFormat, ref PropVariant position);
        void ReadSample(int streamIndex, int controlFlags, out int actualStreamIndex, out int streamFlags,
            out long timestamp, out IMFSample? sample);
        void Flush();
        void GetServiceForStream();
        [PreserveSig] int GetPresentationAttribute(int streamIndex, ref Guid key, out PropVariant value);
    }
}
