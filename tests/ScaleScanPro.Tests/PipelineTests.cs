using System.Runtime.InteropServices;
using OpenCvSharp;
using ScaleScanPro.Core.Settings;
using ScaleScanPro.Vision;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace ScaleScanPro.Tests;

public class PipelineTests : IDisposable
{
    private readonly string _videoPath = Path.Combine(Path.GetTempPath(), $"scan-test-{Guid.NewGuid():N}.avi");

    private void CreateVideo(string qrText, int frames = 30)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions { Width = 260, Height = 260, Margin = 4 }
        };
        var pixels = writer.Write(qrText);
        using var qr = new Mat(pixels.Height, pixels.Width, MatType.CV_8UC4);
        Marshal.Copy(pixels.Pixels, 0, qr.Data, pixels.Pixels.Length);
        using var qrBgr = new Mat();
        Cv2.CvtColor(qr, qrBgr, ColorConversionCodes.BGRA2BGR);

        using var video = new VideoWriter(_videoPath, FourCC.MJPG, 15, new Size(960, 540));
        Assert.True(video.IsOpened(), "Không tạo được file video thử nghiệm");

        for (int i = 0; i < frames; i++)
        {
            using var scene = new Mat(540, 960, MatType.CV_8UC3, new Scalar(150, 150, 150));
            using var roi = new Mat(scene, new Rect(300 + i % 5, 150, pixels.Width, pixels.Height));
            qrBgr.CopyTo(roi);
            video.Write(scene);
        }
    }

    [Fact]
    public async Task Pipeline_reads_code_and_produces_preview_from_video_file()
    {
        const string text = "A,520006656409,XYZ";
        CreateVideo(text);

        var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx");
        using var pipeline = new ScanPipeline(modelPath);

        var codes = new List<string>();
        int previews = 0;
        int previewWidth = 0;
        var statuses = new List<CameraStatus>();
        var gotCode = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        pipeline.CodeDetected += c => { lock (codes) codes.Add(c); gotCode.TrySetResult(true); };
        pipeline.PreviewReady += m => { Interlocked.Increment(ref previews); previewWidth = m.Width; };
        pipeline.CameraStatusChanged += (s, _) => { lock (statuses) statuses.Add(s); };

        pipeline.Start(new CameraSettings { VideoFilePath = _videoPath, Fps = 15, PreviewFps = 10, DetectIntervalMs = 100 }, 0.15f);

        var finished = await Task.WhenAny(gotCode.Task, Task.Delay(15000));
        Assert.Same(gotCode.Task, finished);
        await Task.Delay(500);
        pipeline.Stop();

        lock (codes)
        {
            Assert.NotEmpty(codes);
            Assert.All(codes, c => Assert.Equal(text, c));
        }

        Assert.True(previews >= 3, $"Chỉ có {previews} khung preview");
        Assert.Equal(ScanPipeline.PreviewWidth, previewWidth);
        lock (statuses)
        {
            Assert.Contains(CameraStatus.Running, statuses);
            Assert.Equal(CameraStatus.Stopped, statuses.Last());
        }
    }

    [Fact]
    public void Pipeline_can_be_restarted_and_stopped_repeatedly()
    {
        CreateVideo("520006656409", 10);
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx");
        using var pipeline = new ScanPipeline(modelPath);
        int previews = 0;
        pipeline.PreviewReady += _ => Interlocked.Increment(ref previews);

        for (int i = 0; i < 3; i++)
        {
            pipeline.Start(new CameraSettings { VideoFilePath = _videoPath, Fps = 30 }, 0.15f);
            Thread.Sleep(600);
            pipeline.Stop();
            Assert.False(pipeline.IsRunning);
        }

        Assert.True(previews > 0);
    }

    [Fact]
    public void Missing_video_file_does_not_crash_and_reports_reconnecting()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx");
        using var pipeline = new ScanPipeline(modelPath);
        var statuses = new List<CameraStatus>();
        pipeline.CameraStatusChanged += (s, _) => { lock (statuses) statuses.Add(s); };

        pipeline.Start(new CameraSettings { VideoFilePath = Path.Combine(Path.GetTempPath(), "does-not-exist.avi") }, 0.15f);
        Thread.Sleep(800);
        pipeline.Stop();

        lock (statuses)
        {
            Assert.Contains(CameraStatus.Reconnecting, statuses);
            Assert.DoesNotContain(CameraStatus.Running, statuses);
        }
    }

    [Fact]
    public async Task FrameStore_waits_for_newer_frame_and_times_out()
    {
        using var store = new FrameStore();
        using var dst = new Mat();
        using var frame = new Mat(10, 10, MatType.CV_8UC3, new Scalar(1, 2, 3));

        Assert.False(store.TryCopyLatest(dst, out _));
        Assert.False(store.WaitForNewer(0, 50, dst, out _, CancellationToken.None));

        store.Publish(frame);
        Assert.True(store.WaitForNewer(0, 50, dst, out var seq, CancellationToken.None));
        Assert.Equal(10, dst.Width);

        Assert.False(store.WaitForNewer(seq, 50, dst, out _, CancellationToken.None));

        var waiter = Task.Run(() => store.WaitForNewer(seq, 2000, dst, out _, CancellationToken.None));
        await Task.Delay(100);
        store.Publish(frame);
        Assert.True(await waiter);
    }

    public void Dispose()
    {
        try { File.Delete(_videoPath); } catch { }
    }
}
