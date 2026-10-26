using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScaleScanPro.Core.Abstractions;
using ScaleScanPro.Core.Logic;
using ScaleScanPro.Core.Models;
using ScaleScanPro.Core.Settings;
using ScaleScanPro.Core.Workflow;
using ScaleScanPro.Infrastructure;
using ScaleScanPro.Services;
using ScaleScanPro.Services.Demo;
using ScaleScanPro.Services.Input;
using ScaleScanPro.Services.Scale;
using ScaleScanPro.Vision;

namespace ScaleScanPro.ViewModels;

internal sealed record CameraOption(int Index, string Name)
{
    public override string ToString() => Name;
}

internal sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static string DemoVideoPath => Path.Combine(AppPaths.Root, "demo-scan.avi");

    private readonly SettingsService _settings;
    private readonly WeighingWorkflow _workflow;
    private readonly ScanPipeline _pipeline;
    private readonly ScaleService _scale;
    private readonly IHistoryRepository _history;
    private readonly PendingQueue _pending;
    private readonly INoticeService _notices;
    private readonly RawInputScanner _scanner;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _toastTimer;
    private CancellationTokenSource? _loadCts;
    private bool _initialized;
    private bool _disposed;

    public MainViewModel(
        SettingsService settings,
        WeighingWorkflow workflow,
        ScanPipeline pipeline,
        ScaleService scale,
        IHistoryRepository history,
        PendingQueue pending,
        INoticeService notices,
        RawInputScanner scanner)
    {
        _scanner = scanner;
        _settings = settings;
        _workflow = workflow;
        _pipeline = pipeline;
        _scale = scale;
        _history = history;
        _pending = pending;
        _notices = notices;
        _ui = Application.Current.Dispatcher;

        Preview = new PreviewSurface(_ui);
        _pipeline.Log = AppLog.Warn;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += async (_, _) =>
        {
            if (_workflow.State == WorkflowState.Idle)
                await LoadPageAsync(CurrentPage);
            UpdatePending();
        };

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(NotifyWindowSeconds) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        Wire();
    }

    private const int NotifyWindowSeconds = 3;

    // ---------- Sự kiện để cửa sổ xử lý ----------
    public event Action? OpenSettingsRequested;
    public event Action? CompactRequested;
    public Func<string?>? PickExportPath { get; set; }

    // ---------- Camera ----------
    public PreviewSurface Preview { get; }
    public WriteableBitmap PreviewImage => Preview.Bitmap;
    public ObservableCollection<CameraOption> Cameras { get; } = new();

    [ObservableProperty] private CameraOption? _selectedCamera;
    [ObservableProperty] private string _cameraStatusText = "Chưa khởi động";
    [ObservableProperty] private bool _isCameraRunning;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanButtonText = "⏸  Tạm dừng";

    // ---------- Cân ----------
    [ObservableProperty] private string _weightText = "0.000";
    [ObservableProperty] private bool _isWeightStable;
    [ObservableProperty] private bool _isScaleConnected;
    [ObservableProperty] private string _scaleStatusText = "Chưa kết nối";
    [ObservableProperty] private string _stateText = "Sẵn sàng - hãy quét mã";

    // ---------- PO hiện tại ----------
    [ObservableProperty] private string _lastCode = "—";
    [ObservableProperty] private string _scanTime = "—";
    [ObservableProperty] private string _poCode = "—";
    [ObservableProperty] private string _phcd = "—";
    [ObservableProperty] private string _phtx = "—";
    [ObservableProperty] private string _pstx = "—";
    [ObservableProperty] private string _quantityText = "—";
    [ObservableProperty] private string _expectedKgText = "—";

    // ---------- Trạng thái chung ----------
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isDbOnline = true;
    [ObservableProperty] private string _dbStatusText = "SQL: chưa kiểm tra";
    [ObservableProperty] private string _pendingText = "";
    [ObservableProperty] private bool _isScannerEnabled;
    [ObservableProperty] private bool _isScannerActive;
    [ObservableProperty] private string _scannerStatusText = "";
    [ObservableProperty] private bool _isCompact;
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private NoticeLevel _toastLevel;

    // ---------- Lịch sử ----------
    public ObservableCollection<WeighingRecord> Rows { get; } = new();
    public int[] PageSizes { get; } = { 5, 10, 15, 20 };

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _selectedPageSize = 10;
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private string _pageSummary = "Trang 1 / 1";
    [ObservableProperty] private string _rangeSummary = "";
    [ObservableProperty] private bool _canGoPrevious;
    [ObservableProperty] private bool _canGoNext;
    private int _totalPages = 1;

    partial void OnIsScanningChanged(bool value)
        => ScanButtonText = value ? "⏸  Tạm dừng" : "▶  Tiếp tục quét";

    partial void OnSelectedCameraChanged(CameraOption? value)
    {
        if (!_initialized || value == null || value.Index == _settings.Current.Camera.Index)
            return;

        Mutate(s => s.Camera.Index = value.Index);
        if (IsScanning)
            _ = RestartScanningAsync();
    }

    partial void OnSelectedPageSizeChanged(int value)
    {
        if (!_initialized)
            return;

        Mutate(s => s.Ui.PageSize = value);
        _ = LoadPageAsync(1);
    }

    // =====================================================================
    // Khởi động
    // =====================================================================
    public async Task InitializeAsync()
    {
        SelectedPageSize = _settings.Current.Ui.PageSize;
        StatusMessage = "Đang khởi động...";

        await LoadCamerasAsync();
        _initialized = true;

        _scanner.Start();
        ApplyScannerSettings();

        _scale.Start();
        await StartScanningAsync();
        await LoadPageAsync(1);
        UpdatePending();
        _refreshTimer.Start();

        if (string.IsNullOrEmpty(StatusMessage) || StatusMessage == "Đang khởi động...")
            StatusMessage = "Sẵn sàng";
    }

    private async Task LoadCamerasAsync()
    {
        var s = _settings.Current;
        bool simulated = _settings.IsDemo || !string.IsNullOrWhiteSpace(s.Camera.VideoFilePath);

        CameraStatusText = "Đang tìm camera...";
        IReadOnlyList<int> found = simulated ? Array.Empty<int>() : await Task.Run(() => CameraProbe.Probe());

        Cameras.Clear();
        if (simulated)
        {
            Cameras.Add(new CameraOption(s.Camera.Index, "Video giả lập"));
        }
        else
        {
            foreach (var index in found)
                Cameras.Add(new CameraOption(index, $"Camera {index}"));

            if (Cameras.All(c => c.Index != s.Camera.Index))
                Cameras.Add(new CameraOption(s.Camera.Index, $"Camera {s.Camera.Index} (chưa phát hiện)"));
        }

        SelectedCamera = Cameras.First(c => c.Index == s.Camera.Index);
    }

    private void Wire()
    {
        _workflow.StateChanged += state => Post(() => StateText = state switch
        {
            WorkflowState.LookingUpPo => "Đang tra cứu PO...",
            WorkflowState.WaitingForWeight => "Đặt hàng lên cân...",
            WorkflowState.Saving => "Đang lưu...",
            _ => "Sẵn sàng - hãy quét mã"
        });
        _workflow.PoLoaded += po => Post(() => ApplyPo(po));
        _workflow.Notice += notice => Post(() => ShowNotice(notice));
        _workflow.WeightChanged += (_, stable) => Post(() => IsWeightStable = stable);
        _workflow.Completed += outcome => Post(() =>
        {
            UpdatePending();
            _ = LoadPageAsync(1);
        });
        _workflow.Cleared += () => Post(ClearPo);

        _scale.ReadingReceived += kg =>
        {
            Post(() => WeightText = kg.ToString("0.000"));
            _ = RunSafe(() => _workflow.OnScaleReadingAsync(kg), "xử lý số cân");
        };
        _scale.ConnectionChanged += (connected, message) => Post(() =>
        {
            IsScaleConnected = connected;
            ScaleStatusText = connected ? "Đã kết nối" : "Chưa kết nối";
            if (!connected)
                IsWeightStable = false;
        });

        _pipeline.PreviewReady += Preview.Push;
        _pipeline.CodeDetected += code =>
        {
            Post(() => LastCode = code.Length > 60 ? code[..60] + "…" : code);
            _ = RunSafe(() => _workflow.HandleCodeAsync(code), "xử lý mã quét");
        };
        _pipeline.CameraStatusChanged += (status, message) => Post(() =>
        {
            CameraStatusText = message;
            IsCameraRunning = status == CameraStatus.Running;
        });

        _scanner.CodeScanned += (code, deviceId) =>
        {
            Post(() => LastCode = code.Length > 60 ? code[..60] + "…" : code);
            // force: quét tay bằng máy quét là thao tác cố ý nên cho phép quét lại đúng mã vừa xử lý
            _ = RunSafe(() => _workflow.HandleCodeAsync(code, force: true), "xử lý mã từ máy quét");
        };

        _pending.Synced += count => Post(() =>
        {
            UpdatePending();
            StatusMessage = $"Đã đồng bộ {count} bản ghi lưu tạm lên SQL";
            _ = LoadPageAsync(CurrentPage);
        });

        _settings.Changed += () => Post(OnSettingsChanged);
    }

    // =====================================================================
    // Quét / dừng
    // =====================================================================
    private string? ResolveVideoPath()
    {
        var s = _settings.Current;
        if (!string.IsNullOrWhiteSpace(s.Camera.VideoFilePath))
            return s.Camera.VideoFilePath;
        return _settings.IsDemo ? DemoVideoPath : null;
    }

    private async Task StartScanningAsync()
    {
        try
        {
            var s = _settings.Current;
            var camera = new CameraSettings
            {
                Index = s.Camera.Index,
                Width = s.Camera.Width,
                Height = s.Camera.Height,
                Fps = s.Camera.Fps,
                PreviewFps = s.Camera.PreviewFps,
                DetectIntervalMs = s.Camera.DetectIntervalMs,
                VideoFilePath = ResolveVideoPath() ?? ""
            };

            if (_settings.IsDemo && !File.Exists(camera.VideoFilePath))
            {
                CameraStatusText = "Đang tạo video giả lập...";
                await Task.Run(() => DemoVideo.Create(camera.VideoFilePath, DemoData.Codes));
            }

            var confidence = s.Weighing.ConfidenceThreshold;
            await Task.Run(() => _pipeline.Start(camera, confidence));
            IsScanning = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Không khởi động được camera/bộ nhận diện", ex);
            IsScanning = false;
            CameraStatusText = "Lỗi khởi động";
            ShowNotice(new WorkflowNotice(NoticeLevel.Error, "Không khởi động được bộ nhận diện mã: " + ex.Message));
        }
    }

    private async Task StopScanningAsync()
    {
        await Task.Run(_pipeline.Stop);
        IsScanning = false;
        IsCameraRunning = false;
        CameraStatusText = "Đã tạm dừng";
        Preview.Clear();
    }

    private async Task RestartScanningAsync()
    {
        await StopScanningAsync();
        await StartScanningAsync();
    }

    [RelayCommand]
    private Task ToggleScanAsync() => IsScanning ? StopScanningAsync() : StartScanningAsync();

    // =====================================================================
    // Nghiệp vụ cân
    // =====================================================================
    [RelayCommand]
    private Task ConfirmAsync() => RunSafe(_workflow.ConfirmAsync, "xác nhận cân");

    [RelayCommand]
    private void Reset()
    {
        _workflow.Reset();
        StatusMessage = "Đã làm mới, sẵn sàng quét";
    }

    private void ApplyPo(PoInfo po)
    {
        PoCode = po.Aufnr;
        Phcd = Dash(po.Phcd);
        Phtx = Dash(po.Phtx);
        Pstx = Dash(po.Pstx);
        QuantityText = po.Quantity.ToString("0.###");
        ScanTime = DateTime.Now.ToString("HH:mm:ss");
        IsWeightStable = false;

        var expected = WeightCalculator.ExpectedKg(po.NetWeight, po.PackQty, po.Quantity);
        ExpectedKgText = expected is double e ? $"{e:0.###} kg" : "Chưa có dữ liệu";
    }

    private void ClearPo()
    {
        LastCode = "—";
        ScanTime = "—";
        PoCode = Phcd = Phtx = Pstx = QuantityText = ExpectedKgText = "—";
        WeightText = "0.000";
        IsWeightStable = false;
    }

    private static string Dash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private void ShowNotice(WorkflowNotice notice)
    {
        StatusMessage = notice.Message;

        if (IsCompact)
        {
            ToastMessage = notice.Message;
            ToastLevel = notice.Level;
            IsToastVisible = true;
            _toastTimer.Stop();
            _toastTimer.Start();
        }
        else
        {
            _notices.Show(notice.Level, notice.Message);
        }
    }

    // =====================================================================
    // Lịch sử
    // =====================================================================
    [RelayCommand]
    private Task SearchAsync() => LoadPageAsync(1);

    [RelayCommand]
    private Task RefreshAsync() => LoadPageAsync(CurrentPage);

    [RelayCommand]
    private Task FirstPageAsync() => LoadPageAsync(1);

    [RelayCommand]
    private Task PreviousPageAsync() => LoadPageAsync(CurrentPage - 1);

    [RelayCommand]
    private Task NextPageAsync() => LoadPageAsync(CurrentPage + 1);

    [RelayCommand]
    private Task LastPageAsync() => LoadPageAsync(_totalPages);

    private async Task LoadPageAsync(int page)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(30, _settings.Current.Database.TimeoutSeconds * 2)));
        Interlocked.Exchange(ref _loadCts, cts)?.Cancel();

        try
        {
            var contains = _settings.Current.Ui.SearchContains;
            int size = SelectedPageSize;
            page = Math.Max(1, page);

            var result = await _history.QueryAsync(SearchText, contains, page, size, cts.Token);
            var info = PageInfo.Create(page, size, result.TotalItems);

            if (info.Page != page)
                result = await _history.QueryAsync(SearchText, contains, info.Page, size, cts.Token);

            Rows.Clear();
            foreach (var row in result.Items)
                Rows.Add(row);

            CurrentPage = info.Page;
            _totalPages = Math.Max(1, info.TotalPages);
            PageSummary = $"Trang {info.Page} / {_totalPages}";
            RangeSummary = info.TotalItems == 0 ? "Chưa có dữ liệu" : $"Hiển thị {info.FromIndex} – {info.ToIndex} trên {info.TotalItems}";
            CanGoPrevious = info.HasPrevious;
            CanGoNext = info.HasNext;
            SetDb(true);
        }
        catch (OperationCanceledException)
        {
            // đã có yêu cầu tải mới hơn
        }
        catch (DataUnavailableException ex)
        {
            AppLog.Warn("Tải lịch sử thất bại (mất kết nối): " + ex.Message);
            SetDb(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Tải lịch sử thất bại", ex);
            SetDb(false, ex.Message);
            StatusMessage = "Không tải được lịch sử: " + ex.Message;
        }
        finally
        {
            Interlocked.CompareExchange(ref _loadCts, null, cts);
        }
    }

    private void SetDb(bool online, string? error = null)
    {
        IsDbOnline = online;
        DbStatusText = online
            ? "SQL: đã kết nối"
            : error == null ? "SQL: mất kết nối" : "SQL: lỗi - " + error;
    }

    private void UpdatePending()
    {
        var count = _pending.Count;
        PendingText = count > 0 ? $"{count} bản ghi chờ đồng bộ" : "";
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        var path = PickExportPath?.Invoke();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            StatusMessage = "Đang xuất Excel...";
            var rows = await _history.QueryAllAsync(SearchText, _settings.Current.Ui.SearchContains, 100_000, CancellationToken.None);
            await Task.Run(() => ExcelExporter.Export(rows, path));
            ShowNotice(new WorkflowNotice(NoticeLevel.Success, $"Đã xuất {rows.Count} dòng ra:\n{path}"));
        }
        catch (Exception ex)
        {
            AppLog.Error("Xuất Excel thất bại", ex);
            ShowNotice(new WorkflowNotice(NoticeLevel.Error, "Xuất Excel thất bại: " + ex.Message));
        }
    }

    // =====================================================================
    // Cửa sổ / cấu hình
    // =====================================================================
    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke();

    [RelayCommand]
    private void Compact() => CompactRequested?.Invoke();

    private void OnSettingsChanged()
    {
        ApplyScannerSettings();
        _ = ApplySettingsAsync();
    }

    private void ApplyScannerSettings()
    {
        var s = _settings.Current.Scanner;
        _scanner.Configure(s);

        IsScannerEnabled = s.Enabled;
        IsScannerActive = s.Enabled && !string.IsNullOrWhiteSpace(s.DeviceId);
        ScannerStatusText = !s.Enabled
            ? ""
            : IsScannerActive
                ? $"Máy quét USB: đang nghe ({s.DeviceId})"
                : "Máy quét USB: chưa nhận diện thiết bị";
    }

    private async Task ApplySettingsAsync()
    {
        await Task.Run(_scale.Restart);   // đóng/mở lại cổng COM có thể mất vài giây, không chặn giao diện

        var s = _settings.Current;
        if (SelectedPageSize != s.Ui.PageSize)
            SelectedPageSize = s.Ui.PageSize;

        if (IsScanning)
            await RestartScanningAsync();

        await LoadPageAsync(1);
        StatusMessage = "Đã áp dụng cấu hình mới";
    }

    private void Mutate(Action<AppSettings> change)
    {
        var copy = SettingsService.Clone(_settings.Current);
        change(copy);
        _settings.Save(copy, notify: false);
    }

    // =====================================================================
    // Tiện ích
    // =====================================================================
    private void Post(Action action)
    {
        if (_disposed)
            return;

        try
        {
            _ui.BeginInvoke(action);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Không đưa được thao tác lên luồng giao diện: " + ex.Message);
        }
    }

    private static async Task RunSafe(Func<Task> action, string what)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Lỗi khi " + what, ex);
        }
    }

    /// <summary>Báo cáo ngắn dùng cho chế độ tự kiểm tra (--selftest).</summary>
    public (bool Ok, string Report) SelfTestReport()
    {
        bool ok = IsCameraRunning && IsScaleConnected && Rows.Count > 0 && IsDbOnline;
        var report = $"camera={(IsCameraRunning ? "chạy" : "không chạy")} ({CameraStatusText}); cân={(IsScaleConnected ? "nối" : "không nối")}; " +
                     $"SQL={(IsDbOnline ? "ok" : "lỗi")}; dòng lịch sử={Rows.Count}; mã vừa quét={LastCode}; PO={PoCode}; trạng thái={StateText}; thông báo cuối={StatusMessage}";
        return (ok, report);
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        _toastTimer.Stop();
        _loadCts?.Cancel();
    }
}
