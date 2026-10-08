using System.Runtime.InteropServices;
using OpenCvSharp;
using ScaleScanPro.Core.Models;
using ZXing;
using ZXing.Common;

namespace ScaleScanPro.Vision;

/// <summary>Đọc nội dung mã trong vùng YOLO đã phát hiện (crop + 25% lề, phóng 2x Cubic, xám, ZXing).</summary>
public sealed class CodeDecoder : IDisposable
{
    private readonly BarcodeReaderGeneric _reader;
    private readonly Mat _resized = new();
    private readonly Mat _gray = new();
    private byte[] _buffer = Array.Empty<byte>();

    public CodeDecoder()
    {
        _reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = new List<BarcodeFormat>
                {
                    BarcodeFormat.QR_CODE,
                    BarcodeFormat.CODE_128,
                    BarcodeFormat.EAN_13,
                    BarcodeFormat.EAN_8,
                    BarcodeFormat.CODE_39,
                    BarcodeFormat.DATA_MATRIX
                }
            }
        };
    }

    public string? Decode(Mat frame, BoundingBox box)
    {
        int padX = (int)(box.Width * 0.25);
        int padY = (int)(box.Height * 0.25);
        int x = Math.Max(0, box.X - padX);
        int y = Math.Max(0, box.Y - padY);
        int right = Math.Min(frame.Width, box.X + box.Width + padX);
        int bottom = Math.Min(frame.Height, box.Y + box.Height + padY);

        if (right - x < 2 || bottom - y < 2)
            return null;

        using var roi = new Mat(frame, new Rect(x, y, right - x, bottom - y));
        Cv2.Resize(roi, _resized, new Size(roi.Width * 2, roi.Height * 2), 0, 0, InterpolationFlags.Cubic);

        if (_resized.Channels() == 1)
            _resized.CopyTo(_gray);
        else
            Cv2.CvtColor(_resized, _gray, _resized.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);

        return DecodeGray(_gray);
    }

    /// <summary>Giải mã trực tiếp từ ảnh xám 8-bit (không tạo Bitmap trung gian).</summary>
    public string? DecodeGray(Mat gray)
    {
        int width = gray.Width;
        int height = gray.Height;
        int length = width * height;
        if (length == 0)
            return null;

        var source = gray.IsContinuous() ? gray : gray.Clone();
        try
        {
            if (_buffer.Length < length)
                _buffer = new byte[length];

            Marshal.Copy(source.Data, _buffer, 0, length);

            var luminance = new RGBLuminanceSource(_buffer, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
            var result = _reader.Decode(luminance);
            return string.IsNullOrWhiteSpace(result?.Text) ? null : result!.Text;
        }
        finally
        {
            if (!ReferenceEquals(source, gray))
                source.Dispose();
        }
    }

    public void Dispose()
    {
        _resized.Dispose();
        _gray.Dispose();
    }
}
