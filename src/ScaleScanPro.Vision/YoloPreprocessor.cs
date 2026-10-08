using System.Runtime.InteropServices;
using OpenCvSharp;

namespace ScaleScanPro.Vision;

public readonly record struct LetterboxInfo(float Scale, int PadX, int PadY);

/// <summary>
/// Chuẩn bị ảnh cho YOLO: letterbox 640x640 (nền xám 114), RGB, chuẩn hóa /255, bố cục NCHW.
/// Mọi buffer được cấp phát một lần và tái sử dụng cho mỗi khung hình (không cấp phát trong vòng lặp).
/// </summary>
public sealed class YoloPreprocessor : IDisposable
{
    public const int InputSize = 640;
    private const int PlaneSize = InputSize * InputSize;

    private readonly Mat _letterbox = new(InputSize, InputSize, MatType.CV_8UC3);
    private readonly Mat _resized = new();
    private readonly Mat _bgr = new();
    private readonly Mat _float = new();
    private readonly Mat[] _planes = new Mat[3];

    /// <summary>Tensor [1,3,640,640] dạng phẳng, nằm trên Pinned Object Heap để Mat có thể ghi thẳng vào.</summary>
    public float[] Tensor { get; }

    public YoloPreprocessor()
    {
        Tensor = GC.AllocateArray<float>(PlaneSize * 3, pinned: true);
        var basePtr = Marshal.UnsafeAddrOfPinnedArrayElement(Tensor, 0);
        for (int i = 0; i < 3; i++)
            _planes[i] = Mat.FromPixelData(InputSize, InputSize, MatType.CV_32FC1, basePtr + i * PlaneSize * sizeof(float));
    }

    public LetterboxInfo Prepare(Mat frame)
    {
        var source = EnsureBgr(frame);

        float scale = Math.Min((float)InputSize / source.Width, (float)InputSize / source.Height);
        int newWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        int newHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        int padX = (InputSize - newWidth) / 2;
        int padY = (InputSize - newHeight) / 2;

        _letterbox.SetTo(new Scalar(114, 114, 114));
        Cv2.Resize(source, _resized, new Size(newWidth, newHeight), 0, 0, InterpolationFlags.Area);

        using (var roi = new Mat(_letterbox, new Rect(padX, padY, newWidth, newHeight)))
            _resized.CopyTo(roi);

        _letterbox.ConvertTo(_float, MatType.CV_32FC3, 1.0 / 255.0);

        // Nguồn xen kẽ B,G,R -> ba mặt phẳng R,G,B ghi thẳng vào Tensor.
        Cv2.MixChannels(new[] { _float }, _planes, new[] { 2, 0, 1, 1, 0, 2 });

        return new LetterboxInfo(scale, padX, padY);
    }

    private Mat EnsureBgr(Mat frame)
    {
        switch (frame.Channels())
        {
            case 3:
                return frame;
            case 1:
                Cv2.CvtColor(frame, _bgr, ColorConversionCodes.GRAY2BGR);
                return _bgr;
            case 4:
                Cv2.CvtColor(frame, _bgr, ColorConversionCodes.BGRA2BGR);
                return _bgr;
            default:
                throw new NotSupportedException($"Ảnh có {frame.Channels()} kênh không được hỗ trợ.");
        }
    }

    public void Dispose()
    {
        foreach (var p in _planes) p.Dispose();
        _letterbox.Dispose();
        _resized.Dispose();
        _bgr.Dispose();
        _float.Dispose();
    }
}
