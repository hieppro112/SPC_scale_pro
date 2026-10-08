using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using ScaleScanPro.Vision;

namespace ScaleScanPro.ViewModels;

/// <summary>
/// Một WriteableBitmap duy nhất dùng chung cho cửa sổ chính và cửa sổ mini.
/// Luồng preview chỉ sao chép điểm ảnh vào buffer; UI chỉ vẽ khung mới nhất (không xếp hàng hàng loạt khung).
/// </summary>
internal sealed class PreviewSurface
{
    private const int Width = ScanPipeline.PreviewWidth;
    private const int Height = ScanPipeline.PreviewHeight;
    private const int Stride = Width * 3;

    private readonly Dispatcher _dispatcher;
    private readonly byte[] _staging = new byte[Stride * Height];
    private readonly object _lock = new();
    private int _scheduled;

    public WriteableBitmap Bitmap { get; }

    public PreviewSurface(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Bitmap = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgr24, null);
    }

    /// <summary>Gọi từ luồng preview; <paramref name="frame"/> chỉ hợp lệ trong lúc gọi.</summary>
    public void Push(Mat frame)
    {
        if (frame.Width != Width || frame.Height != Height || frame.Type() != MatType.CV_8UC3 || !frame.IsContinuous())
            return;

        lock (_lock)
            Marshal.Copy(frame.Data, _staging, 0, _staging.Length);

        ScheduleRender();
    }

    public void Clear()
    {
        lock (_lock)
            Array.Clear(_staging);

        ScheduleRender();
    }

    private void ScheduleRender()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
            _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(Render));
    }

    private void Render()
    {
        Interlocked.Exchange(ref _scheduled, 0);

        lock (_lock)
            Bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), _staging, Stride, 0);
    }
}
