namespace ScaleScanPro.Vision;

/// <summary>
/// Tìm model YOLO. Model được nhúng sẵn trong ScaleScanPro.Vision.dll nên dù công cụ đóng gói (ClickOnce, zip, installer)
/// có bỏ sót file Models\best.onnx thì app vẫn tự giải nén ra thư mục dữ liệu và chạy được.
/// </summary>
public static class ModelLocator
{
    private const string ResourceName = "best.onnx";

    /// <summary>Ưu tiên file nằm cạnh ứng dụng (cho phép thay model mới mà không cần build lại); không có thì dùng bản nhúng.</summary>
    public static string Resolve(string extractRoot)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx");
        if (File.Exists(beside) && new FileInfo(beside).Length > 0)
            return beside;

        return Extract(extractRoot);
    }

    /// <summary>Giải nén model nhúng ra <c>{root}\Models\best.onnx</c> (bỏ qua nếu file đã đúng kích thước).</summary>
    public static string Extract(string root)
    {
        var target = Path.Combine(root, "Models", "best.onnx");

        using var stream = typeof(ModelLocator).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new FileNotFoundException("Không có model nhúng trong ứng dụng (thiếu resource best.onnx).");

        if (File.Exists(target) && new FileInfo(target).Length == stream.Length)
            return target;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".tmp";
        using (var file = File.Create(temp))
            stream.CopyTo(file);

        File.Move(temp, target, overwrite: true);
        return target;
    }
}
