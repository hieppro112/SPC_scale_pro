using ScaleScanPro.Vision;

namespace ScaleScanPro.Infrastructure;

internal static class AppPaths
{
    /// <summary>
    /// Thư mục dữ liệu (cấu hình, log, hàng đợi lưu tạm). Mặc định %LocalAppData%\ScaleScanPro;
    /// đặt biến môi trường SCALESCANPRO_DATA để dùng thư mục khác (chạy thử, bản portable) mà không đụng dữ liệu thật.
    /// </summary>
    public static readonly string Root =
        Environment.GetEnvironmentVariable("SCALESCANPRO_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScaleScanPro");

    private static readonly Lazy<string> Model = new(() => ModelLocator.Resolve(Root));

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
