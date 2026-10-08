using System.Diagnostics;
using OpenCvSharp;
using ScaleScanPro.Core.Models;
using ScaleScanPro.Core.Settings;

namespace ScaleScanPro.Vision;

/// <summary>
/// Camera → (luồng preview ~10fps) + (luồng nhận diện ~7fps: YOLO → ZXing).
/// Các luồng chỉ dùng chung <see cref="FrameStore"/>; mỗi luồng sao chép vào buffer riêng.
/// </summary>
public sealed class ScanPipeline : IDisposable
{
    public const int PreviewWidth = 640;
    public const int PreviewHeight = 360;
    private const int KeepRectMs = 500;

    private readonly FrameStore _store = new();
    private readonly CameraCapture _camera;
    private readonly string _modelPath;
    private readonly object _detectionLock = new();

    private YoloDetector? _detector;
    private CodeDecoder? _decoder;
    private CancellationTokenSource? _cts;
    private Thread? _previewThread;
    private Thread? _detectThread;
    private IReadOnlyList<Detection> _detections = Array.Empty<Detection>();
    private long _detectionsTick;

    public ScanPipeline(string modelPath)
    {
        _modelPath = modelPath;
        _camera = new CameraCapture(_store);
        _camera.StatusChanged += (s, m) => CameraStatusChanged?.Invoke(s, m);
    }

    /// <summary>Mat chỉ hợp lệ trong lúc callback chạy; người nhận phải sao chép nếu cần giữ lại.</summary>
    public event Action<Mat>? PreviewReady;

    public event Action<string>? CodeDetected;

    public event Action<CameraStatus, string>? CameraStatusChanged;

    public Action<string>? Log
    {
        get => _log;
        set { _log = value; _camera.Log = value; }
    }

    private Action<string>? _log;

    public bool IsRunning => _cts != null;

    public void Start(CameraSettings camera, float confidenceThreshold)
    {
        Stop();

        if (_detector == null)
            _detector = new YoloDetector(_modelPath, confidenceThreshold);
        else
            _detector.ConfidenceThreshold = confidenceThreshold;

        _decoder ??= new CodeDecoder();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        int previewInterval = 1000 / Math.Clamp(camera.PreviewFps, 1, 30);
        int detectInterval = Math.Max(20, camera.DetectIntervalMs);

        _camera.Start(camera);

        _previewThread = new Thread(() => PreviewLoop(previewInterval, token)) { IsBackground = true, Name = "Preview" };
        _detectThread = new Thread(() => DetectionLoop(detectInterval, token)) { IsBackground = true, Name = "Detection" };
        _previewThread.Start();
        _detectThread.Start();
    }

    public void Stop()
    {
        var cts = _cts;
        if (cts == null)
            return;

        _cts = null;
        cts.Cancel();

        _camera.Stop();
        _previewThread?.Join(TimeSpan.FromSeconds(3));
        _detectThread?.Join(TimeSpan.FromSeconds(5));
        _previewThread = null;
        _detectThread = null;
        cts.Dispose();

        lock (_detectionLock)
        {
            _detections = Array.Empty<Detection>();
            _detectionsTick = 0;
        }
    }

    private void PreviewLoop(int intervalMs, CancellationToken ct)
    {
        using var frame = new Mat();
        using var display = new Mat();
        long lastSequence = 0;
        var clock = Stopwatch.StartNew();

        while (!ct.IsCancellationRequested)
        {
            long started = clock.ElapsedMilliseconds;

            try
            {
                if (_store.TryCopyLatest(frame, out long sequence) && sequence != lastSequence)
                {
                    lastSequence = sequence;
                    DrawDetections(frame);
                    Cv2.Resize(frame, display, new Size(PreviewWidth, PreviewHeight), 0, 0, InterpolationFlags.Area);
                    PreviewReady?.Invoke(display);
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke("Lỗi luồng preview: " + ex.Message);
            }

            int wait = intervalMs - (int)(clock.ElapsedMilliseconds - started);
            if (wait > 0)
                ct.WaitHandle.WaitOne(wait);
        }
    }

    private void DetectionLoop(int intervalMs, CancellationToken ct)
    {
        using var frame = new Mat();
        long lastSequence = 0;
        var clock = Stopwatch.StartNew();

        while (!ct.IsCancellationRequested)
        {
            if (!_store.WaitForNewer(lastSequence, 500, frame, out lastSequence, ct))
                continue;

            long started = clock.ElapsedMilliseconds;

            try
            {
                var found = _detector!.Detect(frame);
                lock (_detectionLock)
                {
                    _detections = found;
                    _detectionsTick = Environment.TickCount64;
                }

                foreach (var detection in found)
                {
                    var text = _decoder!.Decode(frame, detection.Box);
                    if (text != null)
                        CodeDetected?.Invoke(text);
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke("Lỗi luồng nhận diện: " + ex);
                ct.WaitHandle.WaitOne(500);
            }

            int wait = intervalMs - (int)(clock.ElapsedMilliseconds - started);
            if (wait > 0)
                ct.WaitHandle.WaitOne(wait);
        }
    }

    private void DrawDetections(Mat frame)
    {
        IReadOnlyList<Detection> snapshot;
        lock (_detectionLock)
        {
            if (_detections.Count == 0 || Environment.TickCount64 - _detectionsTick > KeepRectMs)
                return;
            snapshot = _detections;
        }

        foreach (var d in snapshot)
        {
            // Barcode: xanh lá, QR: đỏ (BGR)
            var color = d.ClassId == 0 ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
            Cv2.Rectangle(frame, new Rect(d.Box.X, d.Box.Y, d.Box.Width, d.Box.Height), color, 3);
        }
    }

    public void Dispose()
    {
        Stop();
        _camera.Dispose();
        _detector?.Dispose();
        _decoder?.Dispose();
        _store.Dispose();
    }
}
