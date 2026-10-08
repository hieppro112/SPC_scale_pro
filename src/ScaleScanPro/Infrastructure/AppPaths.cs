using ScaleScanPro.Vision;

namespace ScaleScanPro.Infrastructure;

internal static class AppPaths
{
    private static readonly Lazy<string> Model = new(() => ModelLocator.Resolve(Root));

    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScaleScanPro");

    public static string Logs => Path.Combine(Root, "logs");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string PendingFile => Path.Combine(Root, "pending.jsonl");
    public static string DefaultErrorLogs => Path.Combine(Root, "error-logs");

    /// <summary>Đường dẫn model: file cạnh ứng dụng nếu có, nếu không thì giải nén bản nhúng ra thư mục dữ liệu.</summary>
    public static string ModelPath => Model.Value;

    /// <summary>Không ném lỗi: trả về đường dẫn dự phòng để lỗi hiện ra rõ ràng khi bắt đầu quét.</summary>
    public static string ModelPathOrFallback()
    {
        try
        {
            return ModelPath;
        }
        catch (Exception ex)
        {
            AppLog.Error("Không chuẩn bị được model nhận diện", ex);
            return Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx");
        }
    }
}
