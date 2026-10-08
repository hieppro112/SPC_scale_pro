using OpenCvSharp;
using ScaleScanPro.Core.Settings;

namespace ScaleScanPro.Vision;

public enum CameraStatus
{
    Stopped,
    Connecting,
    Running,
    Reconnecting
}

/// <summary>Một luồng riêng đọc camera (hoặc file video để giả lập) và đẩy khung mới nhất vào <see cref="FrameStore"/>.</summary>
public sealed class CameraCapture : IDisposable
{
    private const int MaxConsecutiveFailures = 30;

    private readonly FrameStore _store;
    private CancellationTokenSource? _cts;
    private Thread? _thread;

    public CameraCapture(FrameStore store) => _store = store;

    public event Action<CameraStatus, string>? StatusChanged;

    public Action<string>? Log { get; set; }

    public void Start(CameraSettings settings)
    {
        Stop();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var snapshot = new CameraSettings
        {
            Index = settings.Index,
            Width = settings.Width,
            Height = settings.Height,
            Fps = settings.Fps,
            VideoFilePath = settings.VideoFilePath
        };

        _thread = new Thread(() => Run(snapshot, token)) { IsBackground = true, Name = "CameraCapture" };
        _thread.Start();
    }

    public void Stop()
    {
        var cts = _cts;
        var thread = _thread;
        _cts = null;
        _thread = null;

        if (cts == null)
            return;

        cts.Cancel();
        thread?.Join(TimeSpan.FromSeconds(4));
        cts.Dispose();
        _store.Clear();
        Raise(CameraStatus.Stopped, "Đã dừng");
    }

    private void Run(CameraSettings s, CancellationToken ct)
    {
        bool isFile = !string.IsNullOrWhiteSpace(s.VideoFilePath);
        string name = isFile ? Path.GetFileName(s.VideoFilePath) : $"camera {s.Index}";
        int frameDelayMs = Math.Max(1, 1000 / Math.Max(1, s.Fps));
        bool firstAttempt = true;

        while (!ct.IsCancellationRequested)
        {
            Raise(firstAttempt ? CameraStatus.Connecting : CameraStatus.Reconnecting,
                firstAttempt ? $"Đang mở {name}..." : $"Mất tín hiệu, đang kết nối lại {name}...");
            firstAttempt = false;

            VideoCapture? capture = null;
            try
            {
                capture = Open(s, isFile);
                if (!capture.IsOpened())
                {
                    Log?.Invoke($"Không mở được {name}");
                    Raise(CameraStatus.Reconnecting, $"Không mở được {name}, sẽ thử lại");
                    ct.WaitHandle.WaitOne(2000);
                    continue;
                }

                Raise(CameraStatus.Running, $"Đang chạy: {name}");
                ReadLoop(capture, isFile, frameDelayMs, ct);
            }
            catch (Exception ex)
            {
                Log?.Invoke("Lỗi luồng camera: " + ex);
                ct.WaitHandle.WaitOne(2000);
            }
            finally
            {
                capture?.Release();
                capture?.Dispose();
            }
        }
    }

    private void ReadLoop(VideoCapture capture, bool isFile, int frameDelayMs, CancellationToken ct)
    {
        using var frame = new Mat();
        int failures = 0;

        while (!ct.IsCancellationRequested)
        {
            if (!capture.Read(frame) || frame.Empty())
            {
                if (isFile)
                {
                    capture.Set(VideoCaptureProperties.PosFrames, 0); // phát lặp
                    continue;
                }

                if (++failures >= MaxConsecutiveFailures)
                {
                    Log?.Invoke("Camera không trả về khung hình, sẽ mở lại");
                    return;
                }

                Thread.Sleep(10);
                continue;
            }

            failures = 0;
            _store.Publish(frame);

            if (isFile)
                Thread.Sleep(frameDelayMs);
        }
    }

    private static VideoCapture Open(CameraSettings s, bool isFile)
    {
        if (isFile)
            return new VideoCapture(s.VideoFilePath);

        var capture = new VideoCapture(s.Index, VideoCaptureAPIs.DSHOW);
        if (capture.IsOpened())
        {
            capture.Set(VideoCaptureProperties.FrameWidth, s.Width);
            capture.Set(VideoCaptureProperties.FrameHeight, s.Height);
            capture.Set(VideoCaptureProperties.Fps, s.Fps);
            capture.Set(VideoCaptureProperties.AutoFocus, 1);
        }

        return capture;
    }

    private void Raise(CameraStatus status, string message)
    {
        try { StatusChanged?.Invoke(status, message); }
        catch (Exception ex) { Log?.Invoke("Lỗi trong trình xử lý trạng thái camera: " + ex.Message); }
    }

    public void Dispose() => Stop();
}

public static class CameraProbe
{
    /// <summary>Thử mở lần lượt các chỉ số camera. Chỉ gọi khi pipeline đang dừng (camera đang chạy sẽ báo là không mở được).</summary>
    public static IReadOnlyList<int> Probe(int maxIndex = 6)
    {
        var found = new List<int>();
        for (int i = 0; i < maxIndex; i++)
        {
            try
            {
                using var capture = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                if (capture.IsOpened())
                    found.Add(i);
            }
            catch
            {
                // bỏ qua chỉ số lỗi
            }
        }

        return found;
    }
}
