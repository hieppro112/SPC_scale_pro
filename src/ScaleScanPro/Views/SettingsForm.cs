using System.Globalization;
using ScaleScanPro.Core.Settings;

namespace ScaleScanPro.Views;

/// <summary>Dữ liệu của màn hình cài đặt. Mọi ô số để dạng chuỗi rồi kiểm tra khi lưu để báo lỗi rõ ràng.</summary>
internal sealed class SettingsForm
{
    // Cơ sở dữ liệu
    public string Server { get; set; } = "";
    public string PoDatabase { get; set; } = "";
    public string PoUser { get; set; } = "";
    public string HistoryDatabase { get; set; } = "";
    public string HistoryUser { get; set; } = "";
    public string TimeoutSeconds { get; set; } = "";

    // Cân
    public string PortName { get; set; } = "";
    public string BaudRate { get; set; } = "";
    public string RequestCommand { get; set; } = "";
    public string PollIntervalMs { get; set; } = "";

    // Camera
    public string CameraIndex { get; set; } = "";
    public string CameraWidth { get; set; } = "";
    public string CameraHeight { get; set; } = "";
    public string CameraFps { get; set; } = "";
    public string VideoFilePath { get; set; } = "";

    // Cân và kiểm tra
    public string ToleranceKg { get; set; } = "";
    public string TolerancePercent { get; set; } = "";
    public string StableCount { get; set; } = "";
    public string StableSpreadKg { get; set; } = "";
    public string MinWeightKg { get; set; } = "";
    public string WaitTimeoutSeconds { get; set; } = "";
    public string MinPlainCodeLength { get; set; } = "";
    public string ConfidenceThreshold { get; set; } = "";

    // Khác
    public bool SearchContains { get; set; }
    public string ErrorLogFolder { get; set; } = "";
    public bool DemoMode { get; set; }

    public static SettingsForm From(AppSettings s) => new()
    {
        Server = s.Database.Server,
        PoDatabase = s.Database.PoDatabase,
        PoUser = s.Database.PoUser,
        HistoryDatabase = s.Database.HistoryDatabase,
        HistoryUser = s.Database.HistoryUser,
        TimeoutSeconds = Fmt(s.Database.TimeoutSeconds),
        PortName = s.Scale.PortName,
        BaudRate = Fmt(s.Scale.BaudRate),
        RequestCommand = s.Scale.RequestCommand,
        PollIntervalMs = Fmt(s.Scale.PollIntervalMs),
        CameraIndex = Fmt(s.Camera.Index),
        CameraWidth = Fmt(s.Camera.Width),
        CameraHeight = Fmt(s.Camera.Height),
        CameraFps = Fmt(s.Camera.Fps),
        VideoFilePath = s.Camera.VideoFilePath,
        ToleranceKg = Fmt(s.Weighing.ToleranceKg),
        TolerancePercent = Fmt(s.Weighing.TolerancePercent),
        StableCount = Fmt(s.Weighing.StableCount),
        StableSpreadKg = Fmt(s.Weighing.StableSpreadKg),
        MinWeightKg = Fmt(s.Weighing.MinWeightKg),
        WaitTimeoutSeconds = Fmt(s.Weighing.WaitTimeoutSeconds),
        MinPlainCodeLength = Fmt(s.Weighing.MinPlainCodeLength),
        ConfidenceThreshold = Fmt(s.Weighing.ConfidenceThreshold),
        SearchContains = s.Ui.SearchContains,
        ErrorLogFolder = s.ErrorLogFolder,
        DemoMode = s.DemoMode
    };

    /// <summary>Ghi giá trị vào <paramref name="target"/> (mật khẩu do cửa sổ xử lý riêng). Trả về danh sách lỗi nhập liệu.</summary>
    public List<string> ApplyTo(AppSettings target)
    {
        var errors = new List<string>();

        target.Database.Server = Required(Server, "Máy chủ SQL", errors);
        target.Database.PoDatabase = Required(PoDatabase, "CSDL tra PO", errors);
        target.Database.PoUser = PoUser.Trim();
        target.Database.HistoryDatabase = Required(HistoryDatabase, "CSDL lịch sử", errors);
        target.Database.HistoryUser = HistoryUser.Trim();
        target.Database.TimeoutSeconds = Int(TimeoutSeconds, "Timeout SQL (giây)", 3, 120, errors);

        target.Scale.PortName = Required(PortName, "Cổng COM", errors);
        target.Scale.BaudRate = Int(BaudRate, "Baud rate", 300, 921600, errors);
        target.Scale.RequestCommand = Required(RequestCommand, "Lệnh yêu cầu số cân", errors);
        target.Scale.PollIntervalMs = Int(PollIntervalMs, "Chu kỳ hỏi cân (ms)", 100, 5000, errors);

        target.Camera.Index = Int(CameraIndex, "Chỉ số camera", 0, 20, errors);
        target.Camera.Width = Int(CameraWidth, "Độ rộng camera", 160, 4096, errors);
        target.Camera.Height = Int(CameraHeight, "Độ cao camera", 120, 2304, errors);
        target.Camera.Fps = Int(CameraFps, "FPS camera", 1, 60, errors);
        target.Camera.VideoFilePath = VideoFilePath.Trim();

        target.Weighing.ToleranceKg = Double(ToleranceKg, "Dung sai (kg)", 0, 1000, errors);
        target.Weighing.TolerancePercent = Double(TolerancePercent, "Dung sai (%)", 0, 100, errors);
        target.Weighing.StableCount = Int(StableCount, "Số lần đọc ổn định", 1, 50, errors);
        target.Weighing.StableSpreadKg = Double(StableSpreadKg, "Độ lệch ổn định (kg)", 0, 10, errors);
        target.Weighing.MinWeightKg = Double(MinWeightKg, "Khối lượng tối thiểu (kg)", 0, 1000, errors);
        target.Weighing.WaitTimeoutSeconds = Int(WaitTimeoutSeconds, "Thời gian chờ cân (giây)", 5, 600, errors);
        target.Weighing.MinPlainCodeLength = Int(MinPlainCodeLength, "Độ dài tối thiểu mã PO", 1, 100, errors);
        target.Weighing.ConfidenceThreshold = (float)Double(ConfidenceThreshold, "Ngưỡng tin cậy YOLO", 0.01, 0.99, errors);

        target.Ui.SearchContains = SearchContains;
        target.ErrorLogFolder = ErrorLogFolder.Trim();
        target.DemoMode = DemoMode;
        return errors;
    }

    private static string Fmt(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Required(string value, string name, List<string> errors)
    {
        value = value.Trim();
        if (value.Length == 0)
            errors.Add($"{name}: không được để trống");
        return value;
    }

    private static int Int(string text, string name, int min, int max, List<string> errors)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < min || v > max)
        {
            errors.Add($"{name}: nhập số nguyên từ {min} đến {max}");
            return min;
        }

        return v;
    }

    private static double Double(string text, string name, double min, double max, List<string> errors)
    {
        var normalized = text.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < min || v > max)
        {
            errors.Add($"{name}: nhập số từ {min} đến {max}");
            return min;
        }

        return v;
    }
}
