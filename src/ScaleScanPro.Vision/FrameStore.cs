using OpenCvSharp;

namespace ScaleScanPro.Vision;

/// <summary>
/// Giữ khung hình mới nhất. Người ghi và các người đọc đều sao chép vào buffer có sẵn của mình
/// (không cấp phát mỗi khung), khóa chỉ giữ trong lúc memcpy nên không tranh chấp lâu.
/// </summary>
public sealed class FrameStore : IDisposable
{
    private readonly object _lock = new();
    private readonly Mat _latest = new();
    private long _sequence;
    private bool _hasFrame;

    public void Publish(Mat frame)
    {
        lock (_lock)
        {
            frame.CopyTo(_latest);
            _sequence++;
            _hasFrame = true;
            Monitor.PulseAll(_lock);
        }
    }

    public bool TryCopyLatest(Mat destination, out long sequence)
    {
        lock (_lock)
        {
            sequence = _sequence;
            if (!_hasFrame)
                return false;

            _latest.CopyTo(destination);
            return true;
        }
    }

    /// <summary>Chờ tới khi có khung mới hơn <paramref name="lastSequence"/> rồi sao chép vào <paramref name="destination"/>.</summary>
    public bool WaitForNewer(long lastSequence, int timeoutMs, Mat destination, out long sequence, CancellationToken ct)
    {
        long deadline = Environment.TickCount64 + timeoutMs;

        lock (_lock)
        {
            while (!_hasFrame || _sequence <= lastSequence)
            {
                long remaining = deadline - Environment.TickCount64;
                if (ct.IsCancellationRequested || remaining <= 0)
                {
                    sequence = lastSequence;
                    return false;
                }

                Monitor.Wait(_lock, (int)Math.Min(remaining, 100));
            }

            _latest.CopyTo(destination);
            sequence = _sequence;
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _hasFrame = false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _latest.Dispose();
            _hasFrame = false;
        }
    }
}
