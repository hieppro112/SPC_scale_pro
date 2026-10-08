using System.Runtime.InteropServices;
using OpenCvSharp;
using ScaleScanPro.Core.Models;
using ScaleScanPro.Vision;
using Xunit.Abstractions;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace ScaleScanPro.Tests;

public class VisionTests
{
    private readonly ITestOutputHelper _out;

    public VisionTests(ITestOutputHelper output) => _out = output;

    private static Mat RandomBgr(int width, int height, int seed)
    {
        var rng = new Random(seed);
        var bytes = new byte[width * height * 3];
        rng.NextBytes(bytes);
        var mat = new Mat(height, width, MatType.CV_8UC3);
        Marshal.Copy(bytes, 0, mat.Data, bytes.Length);
        return mat;
    }

    /// <summary>Cách làm "chậm" gốc: Resize + CvtColor + Split + /255 để đối chiếu.</summary>
    private static float[] ReferenceTensor(Mat frame)
    {
        const int size = 640;
        float scale = Math.Min((float)size / frame.Width, (float)size / frame.Height);
        int nw = (int)Math.Round(frame.Width * scale);
        int nh = (int)Math.Round(frame.Height * scale);
        int padX = (size - nw) / 2;
        int padY = (size - nh) / 2;

        using var resized = new Mat();
        Cv2.Resize(frame, resized, new Size(nw, nh), 0, 0, InterpolationFlags.Area);
        using var canvas = new Mat(size, size, MatType.CV_8UC3, new Scalar(114, 114, 114));
        using (var roi = new Mat(canvas, new Rect(padX, padY, nw, nh)))
            resized.CopyTo(roi);

        using var rgb = new Mat();
        Cv2.CvtColor(canvas, rgb, ColorConversionCodes.BGR2RGB);
        var channels = Cv2.Split(rgb);

        var data = new float[3 * size * size];
        for (int c = 0; c < 3; c++)
        {
            channels[c].GetArray(out byte[] bytes);
            for (int i = 0; i < bytes.Length; i++)
                data[c * size * size + i] = bytes[i] / 255.0f;
            channels[c].Dispose();
        }

        return data;
    }

    [Theory]
    [InlineData(960, 540)]
    [InlineData(640, 480)]
    [InlineData(480, 800)]
    [InlineData(640, 640)]
    public void Preprocessor_matches_reference_implementation(int width, int height)
    {
        using var frame = RandomBgr(width, height, 42);
        using var pre = new YoloPreprocessor();

        var info = pre.Prepare(frame);
        var expected = ReferenceTensor(frame);

        Assert.Equal(expected.Length, pre.Tensor.Length);
        double maxDiff = 0;
        for (int i = 0; i < expected.Length; i++)
            maxDiff = Math.Max(maxDiff, Math.Abs(expected[i] - pre.Tensor[i]));

        Assert.True(maxDiff < 1e-6, $"Lệch tối đa {maxDiff}");
        Assert.True(info.Scale > 0);
    }

    [Fact]
    public void Preprocessor_can_be_reused_across_frames_and_sizes()
    {
        using var pre = new YoloPreprocessor();
        using var a = RandomBgr(960, 540, 1);
        using var b = RandomBgr(640, 480, 2);

        pre.Prepare(a);
        pre.Prepare(b);          // khung khác tỉ lệ: vùng viền cũ phải được xóa
        var expected = ReferenceTensor(b);

        for (int i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - pre.Tensor[i]) < 1e-6, $"khác nhau tại {i}");
    }

    [Fact]
    public void Preprocessor_pads_with_gray_114_and_maps_colors_to_rgb_planes()
    {
        using var pre = new YoloPreprocessor();
        using var frame = new Mat(540, 960, MatType.CV_8UC3, new Scalar(10, 20, 30)); // B=10 G=20 R=30

        var info = pre.Prepare(frame);
        Assert.Equal(140, info.PadY);
        Assert.Equal(0, info.PadX);

        const int plane = 640 * 640;
        // góc trên-trái là viền
        Assert.Equal(114 / 255f, pre.Tensor[0], 4);
        Assert.Equal(114 / 255f, pre.Tensor[plane], 4);
        Assert.Equal(114 / 255f, pre.Tensor[2 * plane], 4);

        // tâm ảnh: mặt phẳng 0 = R, 1 = G, 2 = B
        int center = 320 * 640 + 320;
        Assert.Equal(30 / 255f, pre.Tensor[center], 3);
        Assert.Equal(20 / 255f, pre.Tensor[plane + center], 3);
        Assert.Equal(10 / 255f, pre.Tensor[2 * plane + center], 3);
    }

    private static Mat SceneWithQr(string text, int qrX, int qrY, int qrSize, out BoundingBox box)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions { Width = qrSize, Height = qrSize, Margin = 4 }
        };
        var pixels = writer.Write(text);

        using var qr = new Mat(pixels.Height, pixels.Width, MatType.CV_8UC4);
        Marshal.Copy(pixels.Pixels, 0, qr.Data, pixels.Pixels.Length);

        var scene = new Mat(540, 960, MatType.CV_8UC3, new Scalar(150, 150, 150));
        using var qrBgr = new Mat();
        Cv2.CvtColor(qr, qrBgr, ColorConversionCodes.BGRA2BGR);
        using (var roi = new Mat(scene, new Rect(qrX, qrY, pixels.Width, pixels.Height)))
            qrBgr.CopyTo(roi);

        box = new BoundingBox(qrX, qrY, pixels.Width, pixels.Height);
        return scene;
    }

    [Theory]
    [InlineData("A,520006656409,XYZ")]
    [InlineData("520006656409")]
    public void Decoder_reads_qr_inside_the_box(string text)
    {
        using var scene = SceneWithQr(text, 300, 150, 240, out var box);
        using var decoder = new CodeDecoder();

        Assert.Equal(text, decoder.Decode(scene, box));
    }

    [Fact]
    public void Decoder_returns_null_when_there_is_no_code()
    {
        using var scene = new Mat(540, 960, MatType.CV_8UC3, new Scalar(150, 150, 150));
        using var decoder = new CodeDecoder();

        Assert.Null(decoder.Decode(scene, new BoundingBox(100, 100, 200, 200)));
    }

    [Fact]
    public void Detector_loads_model_runs_repeatedly_and_finds_synthetic_qr()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx");
        Assert.True(File.Exists(modelPath), "Không thấy best.onnx ở " + modelPath);

        using var detector = new YoloDetector(modelPath, 0.15f);
        using var scene = SceneWithQr("A,520006656409,XYZ", 300, 150, 260, out var qrBox);

        IReadOnlyList<Detection> last = Array.Empty<Detection>();
        for (int i = 0; i < 3; i++)   // chạy lặp để chắc buffer tái sử dụng không làm hỏng kết quả
            last = detector.Detect(scene);

        _out.WriteLine($"Số phát hiện: {last.Count}");
        foreach (var d in last)
            _out.WriteLine($"class={d.ClassId} conf={d.Confidence:0.00} box={d.Box} IoU với QR thật={ScaleScanPro.Core.Logic.Nms.IoU(d.Box, qrBox):0.00}");

        Assert.All(last, d =>
        {
            Assert.InRange(d.Box.X, 0, 959);
            Assert.InRange(d.Box.Y, 0, 539);
            Assert.True(d.Box.X + d.Box.Width <= 960 && d.Box.Y + d.Box.Height <= 540);
        });

        // Ảnh QR sạch phải được model phát hiện (dù ảnh tổng hợp, không phải ảnh chụp thật)
        Assert.Contains(last, d => ScaleScanPro.Core.Logic.Nms.IoU(d.Box, qrBox) > 0.3f);
    }

    [Fact]
    public void Embedded_model_is_extracted_when_the_file_is_missing_and_matches_the_original()
    {
        var root = Path.Combine(Path.GetTempPath(), "ssp-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = ModelLocator.Extract(root);

            Assert.Equal(Path.Combine(root, "Models", "best.onnx"), path);
            var original = Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx");
            Assert.Equal(new FileInfo(original).Length, new FileInfo(path).Length);
            Assert.Equal(File.ReadAllBytes(original).Take(4096), File.ReadAllBytes(path).Take(4096));

            // Gọi lần hai không ghi lại file
            var stamp = File.GetLastWriteTimeUtc(path);
            Thread.Sleep(30);
            ModelLocator.Extract(root);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));

            // Model giải nén ra chạy được
            using var detector = new YoloDetector(path, 0.5f);
            using var blank = new Mat(540, 960, MatType.CV_8UC3, new Scalar(150, 150, 150));
            Assert.Empty(detector.Detect(blank));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detector_returns_nothing_on_blank_scene()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx");
        using var detector = new YoloDetector(modelPath, 0.5f);
        using var blank = new Mat(540, 960, MatType.CV_8UC3, new Scalar(150, 150, 150));

        Assert.Empty(detector.Detect(blank));
    }
}
