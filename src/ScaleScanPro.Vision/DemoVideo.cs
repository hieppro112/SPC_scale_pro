using System.Runtime.InteropServices;
using OpenCvSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace ScaleScanPro.Vision;

/// <summary>Tạo video giả lập có các mã QR luân phiên để chạy thử ứng dụng khi không có camera.</summary>
public static class DemoVideo
{
    public static void Create(string path, IReadOnlyList<string> codes, int secondsPerCode = 8, int blankSeconds = 4, int fps = 15)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var writerFactory = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions { Width = 260, Height = 260, Margin = 4 }
        };

        using var video = new VideoWriter(path, FourCC.MJPG, fps, new Size(960, 540));
        if (!video.IsOpened())
            throw new IOException("Không tạo được video giả lập: " + path);

        using var blank = new Mat(540, 960, MatType.CV_8UC3, new Scalar(150, 150, 150));
        for (int i = 0; i < fps * blankSeconds; i++)
            video.Write(blank);

        foreach (var code in codes)
        {
            var pixels = writerFactory.Write(code);
            using var qrBgra = new Mat(pixels.Height, pixels.Width, MatType.CV_8UC4);
            Marshal.Copy(pixels.Pixels, 0, qrBgra.Data, pixels.Pixels.Length);
            using var qr = new Mat();
            Cv2.CvtColor(qrBgra, qr, ColorConversionCodes.BGRA2BGR);

            for (int i = 0; i < fps * secondsPerCode; i++)
            {
                using var scene = blank.Clone();
                using var roi = new Mat(scene, new Rect(350 + (i % 7), 140 + (i % 5), pixels.Width, pixels.Height));
                qr.CopyTo(roi);
                video.Write(scene);
            }

            for (int i = 0; i < fps * blankSeconds; i++)
                video.Write(blank);
        }
    }
}
