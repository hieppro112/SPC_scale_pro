using ScaleScanPro.Core.Models;

namespace ScaleScanPro.Core.Logic;

public static class Nms
{
    public static List<Detection> Apply(IEnumerable<Detection> detections, float iouThreshold = 0.45f)
    {
        var sorted = detections.OrderByDescending(d => d.Confidence).ToList();
        var suppressed = new bool[sorted.Count];
        var result = new List<Detection>(sorted.Count);

        for (int i = 0; i < sorted.Count; i++)
        {
            if (suppressed[i])
                continue;

            result.Add(sorted[i]);

            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (!suppressed[j] && IoU(sorted[i].Box, sorted[j].Box) > iouThreshold)
                    suppressed[j] = true;
            }
        }

        return result;
    }

    public static float IoU(BoundingBox a, BoundingBox b)
    {
        int x1 = Math.Max(a.X, b.X);
        int y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        long inter = (long)Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        long union = (long)a.Area + b.Area - inter;
        return union == 0 ? 0 : (float)inter / union;
    }
}
