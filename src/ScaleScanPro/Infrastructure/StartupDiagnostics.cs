using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ScaleScanPro.Infrastructure;

/// <summary>Kiểm tra môi trường lúc khởi động và ghi vào log; trả về danh sách vấn đề để báo cho người dùng.</summary>
internal static class StartupDiagnostics
{
    public static IReadOnlyList<string> Run()
    {
        var problems = new List<string>();
        var baseDir = AppContext.BaseDirectory;

        AppLog.Info("=== Khởi động Scale Scan Pro " + typeof(StartupDiagnostics).Assembly.GetName().Version + " ===");
        AppLog.Info($"OS: {RuntimeInformation.OSDescription} | Process: {RuntimeInformation.ProcessArchitecture} | {RuntimeInformation.FrameworkDescription}");
        AppLog.Info("BaseDirectory: " + baseDir);
        AppLog.Info("Thư mục dữ liệu: " + AppPaths.Root);

        try
        {
            AppLog.Info("Model nhận diện mã: OK (" + AppPaths.ModelPath + ")");
        }
        catch (Exception ex)
        {
            const string msg = "Không chuẩn bị được model nhận diện mã (best.onnx).";
            AppLog.Error(msg, ex);
            problems.Add(msg + " " + ex.Message);
        }

        Check(problems, "onnxruntime.dll", new[]
        {
            Path.Combine(baseDir, "onnxruntime.dll"),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", "onnxruntime.dll")
        });
        Check(problems, "OpenCvSharpExtern.dll", new[]
        {
            Path.Combine(baseDir, "OpenCvSharpExtern.dll"),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", "OpenCvSharpExtern.dll")
        });

        if (!HasVcRuntime())
        {
            const string msg = "Chưa cài Microsoft Visual C++ Redistributable 2015-2022 (x64) - cần cho OpenCV và ONNX Runtime.";
            AppLog.Warn(msg);
            problems.Add(msg);
        }
        else
        {
            AppLog.Info("Visual C++ Redistributable: OK");
        }

        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var probe = Path.Combine(AppPaths.Root, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            var msg = $"Không ghi được vào thư mục dữ liệu {AppPaths.Root}: {ex.Message}";
            AppLog.Error(msg);
            problems.Add(msg);
        }

        return problems;
    }

    private static void Check(List<string> problems, string label, string[] candidates)
    {
        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null)
        {
            AppLog.Info($"File {label}: OK");
            return;
        }

        var msg = $"Thiếu file {label}";
        AppLog.Error(msg + " (đã tìm: " + string.Join("; ", candidates) + ")");
        problems.Add(msg);
    }

    private static bool HasVcRuntime()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64")
                            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            return key?.GetValue("Installed") is int installed && installed == 1;
        }
        catch
        {
            return true; // không đọc được registry thì không kết luận là thiếu
        }
    }
}
