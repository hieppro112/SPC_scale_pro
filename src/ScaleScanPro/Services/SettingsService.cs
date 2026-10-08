using System.Text.Json;
using ScaleScanPro.Core.Settings;
using ScaleScanPro.Infrastructure;

namespace ScaleScanPro.Services;

internal sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _lock = new();
    private AppSettings _current = new();

    public AppSettings Current
    {
        get { lock (_lock) return _current; }
    }

    public event Action? Changed;

    /// <summary>Tham số dòng lệnh --demo: chạy giả lập mà không ghi vào file cấu hình.</summary>
    public bool DemoOverride { get; set; }

    public bool IsDemo => DemoOverride || Current.DemoMode;

    public void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    lock (_lock) _current = Normalize(loaded);
                    AppLog.Info("Đã đọc cấu hình: " + AppPaths.SettingsFile);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("File cấu hình bị lỗi, dùng giá trị mặc định (file lỗi được giữ lại .bak)", ex);
            try { File.Move(AppPaths.SettingsFile, AppPaths.SettingsFile + ".bak", overwrite: true); } catch { }
        }

        lock (_lock) _current = new AppSettings();
        Save(_current, notify: false);
    }

    public void Save(AppSettings settings, bool notify = true)
    {
        settings = Normalize(settings);
        lock (_lock) _current = settings;

        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Không lưu được cấu hình", ex);
            throw;
        }

        if (notify)
            Changed?.Invoke();
    }

    public static AppSettings Clone(AppSettings source)
        => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;

    public string ResolveErrorLogFolder()
        => string.IsNullOrWhiteSpace(Current.ErrorLogFolder) ? AppPaths.DefaultErrorLogs : Current.ErrorLogFolder;

    /// <summary>Chặn các giá trị vô lý (ví dụ 0 khung hình/giây) làm app chạy sai.</summary>
    private static AppSettings Normalize(AppSettings s)
    {
        s.Database ??= new DatabaseSettings();
        s.Scale ??= new ScaleSettings();
        s.Camera ??= new CameraSettings();
        s.Weighing ??= new WeighingSettings();
        s.Ui ??= new UiSettings();

        s.Database.TimeoutSeconds = Math.Clamp(s.Database.TimeoutSeconds, 3, 120);
        s.Scale.BaudRate = s.Scale.BaudRate <= 0 ? 9600 : s.Scale.BaudRate;
        s.Scale.PollIntervalMs = Math.Clamp(s.Scale.PollIntervalMs, 100, 5000);
        s.Scale.RequestCommand = string.IsNullOrWhiteSpace(s.Scale.RequestCommand) ? "SP" : s.Scale.RequestCommand;
        s.Camera.Index = Math.Max(0, s.Camera.Index);
        s.Camera.Width = s.Camera.Width < 160 ? 960 : s.Camera.Width;
        s.Camera.Height = s.Camera.Height < 120 ? 540 : s.Camera.Height;
        s.Camera.Fps = Math.Clamp(s.Camera.Fps, 1, 60);
        s.Camera.PreviewFps = Math.Clamp(s.Camera.PreviewFps, 1, 30);
        s.Camera.DetectIntervalMs = Math.Clamp(s.Camera.DetectIntervalMs, 50, 2000);
        s.Weighing.StableCount = Math.Clamp(s.Weighing.StableCount, 1, 50);
        s.Weighing.StableSpreadKg = Math.Max(0, s.Weighing.StableSpreadKg);
        s.Weighing.ToleranceKg = Math.Max(0, s.Weighing.ToleranceKg);
        s.Weighing.TolerancePercent = Math.Max(0, s.Weighing.TolerancePercent);
        s.Weighing.WaitTimeoutSeconds = Math.Clamp(s.Weighing.WaitTimeoutSeconds, 5, 600);
        s.Weighing.MinPlainCodeLength = Math.Max(1, s.Weighing.MinPlainCodeLength);
        s.Weighing.ConfidenceThreshold = Math.Clamp(s.Weighing.ConfidenceThreshold, 0.01f, 0.99f);
        s.Ui.PageSize = new[] { 5, 10, 15, 20 }.Contains(s.Ui.PageSize) ? s.Ui.PageSize : 10;
        return s;
    }
}
