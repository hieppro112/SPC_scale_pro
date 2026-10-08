using System.IO.Ports;
using System.Windows;
using Microsoft.Win32;
using ScaleScanPro.Core.Settings;
using ScaleScanPro.Infrastructure;
using ScaleScanPro.Services;

namespace ScaleScanPro.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly SettingsForm _form;

    internal SettingsWindow(SettingsService settings)
    {
        InitializeComponent();

        _settings = settings;
        _form = SettingsForm.From(settings.Current);
        DataContext = _form;

        try
        {
            PortCombo.ItemsSource = SerialPort.GetPortNames().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Không liệt kê được cổng COM: " + ex.Message);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Cập nhật giá trị đang gõ dở trong ô nhập
        PortCombo.GetBindingExpression(System.Windows.Controls.ComboBox.TextProperty)?.UpdateSource();

        var updated = SettingsService.Clone(_settings.Current);
        var errors = _form.ApplyTo(updated);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, "Vui lòng sửa các mục sau:\n\n• " + string.Join("\n• ", errors),
                "Cài đặt chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Mật khẩu: để trống nghĩa là giữ nguyên mật khẩu đã lưu
        if (PoPassword.Password.Length > 0)
            updated.Database.PoPasswordProtected = SecretProtector.Protect(PoPassword.Password);
        if (HistoryPassword.Password.Length > 0)
            updated.Database.HistoryPasswordProtected = SecretProtector.Protect(HistoryPassword.Password);

        try
        {
            _settings.Save(updated);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không lưu được cấu hình:\n" + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Chọn thư mục lưu log lệch cân" };
        if (dialog.ShowDialog(this) == true)
        {
            _form.ErrorLogFolder = dialog.FolderName;
            DataContext = null;
            DataContext = _form;
        }
    }
}
