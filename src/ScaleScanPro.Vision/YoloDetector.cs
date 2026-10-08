using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using ScaleScanPro.Core.Logic;
using ScaleScanPro.Core.Models;

namespace ScaleScanPro.Vision;

/// <summary>Chạy model YOLO (best.onnx) tìm Barcode (class 0) và QR (class 1).</summary>
public sealed class YoloDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly YoloPreprocessor _pre = new();
    private readonly List<NamedOnnxValue> _inputs;
    private readonly object _lock = new();

    public float ConfidenceThreshold { get; set; }

    public YoloDetector(string modelPath, float confidenceThreshold, int intraOpThreads = 2)
    {
        ConfidenceThreshold = confidenceThreshold;

        using var options = new SessionOptions
        {
            IntraOpNumThreads = intraOpThreads,
            InterOpNumThreads = 1
        };
        _session = new InferenceSession(modelPath, options);

        var inputName = _session.InputMetadata.Keys.First();
        var tensor = new DenseTensor<float>(_pre.Tensor.AsMemory(),
            new[] { 1, 3, YoloPreprocessor.InputSize, YoloPreprocessor.InputSize });
        _inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
    }

    public IReadOnlyList<Detection> Detect(Mat frame)
    {
        if (frame.Empty())
            return Array.Empty<Detection>();

        lock (_lock)
        {
            var info = _pre.Prepare(frame);

            using var results = _session.Run(_inputs);
            var output = results.First().AsTensor<float>();
            var dims = output.Dimensions;

            ReadOnlySpan<float> data = output is DenseTensor<float> dense
                ? dense.Buffer.Span
                : output.ToArray();

            var found = new List<Detection>();

            if (dims.Length == 3 && dims[1] == 6)
            {
                // [1, 6, N]: hàng 0-3 = cx,cy,w,h; hàng 4-5 = điểm số từng class.
                int n = dims[2];
                for (int i = 0; i < n; i++)
                {
                    float s0 = data[4 * n + i];
                    float s1 = data[5 * n + i];
                    int classId = s1 > s0 ? 1 : 0;
                    float confidence = classId == 1 ? s1 : s0;
                    if (confidence < ConfidenceThreshold)
                        continue;

                    float cx = data[i], cy = data[n + i], w = data[2 * n + i], h = data[3 * n + i];
                    Add(found, frame, info, cx - w / 2f, cy - h / 2f, cx + w / 2f, cy + h / 2f, confidence, classId);
                }
            }
            else if (dims.Length == 3 && dims[2] == 6)
            {
                // [1, N, 6]: x1,y1,x2,y2,confidence,class (đã NMS trong model).
                int n = dims[1];
                for (int i = 0; i < n; i++)
                {
                    int b = i * 6;
                    float confidence = data[b + 4];
                    if (confidence < ConfidenceThreshold)
                        continue;

                    Add(found, frame, info, data[b], data[b + 1], data[b + 2], data[b + 3], confidence, (int)data[b + 5]);
                }
            }
            else
            {
                throw new InvalidOperationException("Định dạng đầu ra của model không được hỗ trợ: [" + string.Join(",", dims.ToArray()) + "]");
            }

            return Nms.Apply(found, 0.45f);
        }
    }

    private static void Add(List<Detection> list, Mat frame, LetterboxInfo info,
        float x1, float y1, float x2, float y2, float confidence, int classId)
    {
        int left = (int)Math.Round((x1 - info.PadX) / info.Scale);
        int top = (int)Math.Round((y1 - info.PadY) / info.Scale);
        int right = (int)Math.Round((x2 - info.PadX) / info.Scale);
        int bottom = (int)Math.Round((y2 - info.PadY) / info.Scale);

        left = Math.Clamp(left, 0, frame.Width - 1);
        top = Math.Clamp(top, 0, frame.Height - 1);
        right = Math.Clamp(right, left + 1, frame.Width);
        bottom = Math.Clamp(bottom, top + 1, frame.Height);

        int width = right - left;
        int height = bottom - top;
        if (width <= 0 || height <= 0)
            return;

        list.Add(new Detection(new BoundingBox(left, top, width, height), confidence, classId));
    }

    public void Dispose()
    {
        _session.Dispose();
        _pre.Dispose();
    }
}
