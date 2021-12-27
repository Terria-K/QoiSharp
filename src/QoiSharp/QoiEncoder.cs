﻿using QoiSharp.Codec;
using QoiSharp.Exceptions;

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace QoiSharp;

/// <summary>
/// QOI encoder.
/// </summary>
public static class QoiEncoder
{
    /// <summary>
    /// Encodes raw pixel data into QOI format.
    /// </summary>
    /// <param name="image">QOI image.</param>
    /// <returns>Encoded image.</returns>
    /// <exception cref="QoiEncodingException">Thrown when image information is invalid.</exception>
    public static byte[] Encode(QoiImage image)
    {
        var bytes = new byte[QoiCodec.HeaderSize + QoiCodec.ReadOnlyPadding.Length + (image.Width * image.Height * (byte)image.Channels)];
        return bytes[..Encode(image, bytes)];
    }

    /// <summary>
    /// Encodes raw pixel data into QOI format.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="buffer">The buffer to receive the encoded bytes</param>
    /// <exception cref="QoiEncodingException">Thrown when image information is invalid.</exception>
    /// <returns>The number of bytes written to the span</returns>
    public static int Encode(QoiImage image, Span<byte> buffer)
    {
        if (image.Width == 0)
        {
            throw new QoiEncodingException($"Invalid width: {image.Width}");
        }

        if (image.Height == 0 || image.Height >= QoiCodec.MaxPixelsReadOnly / image.Width)
        {
            throw new QoiEncodingException($"Invalid height: {image.Height}. Maximum for this image is {QoiCodec.MaxPixelsReadOnly / image.Width - 1}");
        }

        int width = image.Width;
        int height = image.Height;
        int channels = (int)image.Channels;
        byte colorSpace = (byte)image.ColorSpace;

        if (buffer.Length < QoiCodec.HeaderSize + QoiCodec.ReadOnlyPadding.Length + (width * height * channels))
            return -1;

        BinaryPrimitives.WriteInt32BigEndian(buffer, QoiCodec.Magic);
        BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(4), width);
        BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(8), height);

        buffer[12] = (byte)channels;
        buffer[13] = colorSpace;

        Span<int> index = stackalloc int[QoiCodec.HashTableSize];

        Span<byte> prev = stackalloc byte[4] { 0, 0, 0, 255 };
        Span<byte> rgba = stackalloc byte[4] { 0, 0, 0, 255 };
        Span<byte> rgb = rgba.Slice(0, 3);

        Span<int> prevAsInt = MemoryMarshal.Cast<byte, int>(prev);
        Span<int> rgbaAsInt = MemoryMarshal.Cast<byte, int>(rgba);

        int run = 0;
        int counter = 0;
        int p = QoiCodec.HeaderSize;
        var pixels = image.Data.AsSpan(0, width * height * channels);
        while (pixels.Length > 0)
        {
            pixels.Slice(0, channels).CopyTo(rgba);
            pixels = pixels.Slice(channels);

            if (prevAsInt[0] == rgbaAsInt[0])
            {
                run++;
                if (run == 62 || pixels.Length == 0)
                {
                    buffer[p++] = (byte)(QoiCodec.Run | (run - 1));
                    run = 0;
                }
            }
            else
            {
                if (run > 0)
                {
                    buffer[p++] = (byte)(QoiCodec.Run | (run - 1));
                    run = 0;
                }

                int indexPos = (rgba[0] * 3 + rgba[1] * 5 + rgba[2] * 7 + rgba[3] * 11) % QoiCodec.HashTableSize;
                if (rgbaAsInt[0] == index[indexPos])
                {
                    buffer[p++] = (byte)(QoiCodec.Index | (indexPos));
                }
                else
                {
                    index[indexPos] = rgbaAsInt[0];

                    if (rgba[3] == prev[3])
                    {
                        int vr = rgba[0] - prev[0];
                        int vg = rgba[1] - prev[1];
                        int vb = rgba[2] - prev[2];

                        int vgr = vr - vg;
                        int vgb = vb - vg;

                        if (vr is > -3 and < 2 &&
                            vg is > -3 and < 2 &&
                            vb is > -3 and < 2)
                        {
                            counter++;
                            buffer[p++] = (byte)(QoiCodec.Diff | (vr + 2) << 4 | (vg + 2) << 2 | (vb + 2));
                        }
                        else if (vgr is > -9 and < 8 &&
                                 vg is > -33 and < 32 &&
                                 vgb is > -9 and < 8
                                )
                        {
                            buffer[p++] = (byte)(QoiCodec.Luma | (vg + 32));
                            buffer[p++] = (byte)((vgr + 8) << 4 | (vgb + 8));
                        }
                        else
                        {
                            buffer[p++] = QoiCodec.Rgb;
                            rgb.CopyTo(buffer.Slice(p));
                            p += 3;
                        }
                    }
                    else
                    {
                        buffer[p++] = QoiCodec.Rgba;
                        rgba.CopyTo(buffer.Slice(p));
                        p += 4;
                    }
                }
            }
            prevAsInt[0] = rgbaAsInt[0];
        }

        QoiCodec.ReadOnlyPadding.Span.CopyTo(buffer.Slice(p));
        p += QoiCodec.ReadOnlyPadding.Length;

        return p;
    }
}