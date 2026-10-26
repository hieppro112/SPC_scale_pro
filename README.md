# Scale Scan Pro

Ứng dụng WPF (.NET 8) cho trạm đóng gói: camera đọc mã barcode/QR chứa mã PO, tra PO từ SQL Server,
đọc số kg từ cân điện tử qua cổng COM, so với khối lượng chuẩn, lưu lịch sử và cảnh báo nếu lệch.

Dự án này được viết mới hoàn toàn, **không sửa gì trong mã nguồn cũ (`scancode`)**. Model `best.onnx` được sao chép từ bản cũ.

## Chạy thử ngay (không cần camera, cân hay SQL)

```powershell
dotnet run --project src/ScaleScanPro -- --demo
```

Chế độ `--demo` dùng video có mã QR giả lập, cân giả và SQL giả trong bộ nhớ. Mã `0000...` luôn trả về "không tìm thấy PO";
mỗi mã QR trong video chạy một vòng: quét → tra PO → cân → lưu → báo kết quả.

Tự kiểm tra không người trông (mã thoát 0 = đạt): `ScaleScanPro.exe --selftest=30`

## Cấu trúc

| Project | Vai trò | Phụ thuộc |
|---|---|---|
| `ScaleScanPro.Core` | Nghiệp vụ thuần: phân tích mã PO, khối lượng chuẩn + dung sai, phát hiện số cân ổn định, NMS, phân trang, **workflow cân** | không |
| `ScaleScanPro.Vision` | Camera (luồng riêng, tự nối lại), YOLO ONNX, đọc mã ZXing, pipeline preview/nhận diện | OpenCvSharp, ONNX Runtime, ZXing |
| `ScaleScanPro` | Ứng dụng WPF (MVVM): giao diện, SQL, cổng COM, hàng đợi lưu tạm, Excel, cài đặt, log | WPF, SqlClient, ClosedXML |
| `ScaleScanPro.Tests` | 87 test (xUnit) | |

## Luồng nghiệp vụ

1. Camera chạy liên tục; YOLO tìm mã, ZXing đọc nội dung (crop vùng mã + 25% lề, phóng 2x).
2. Mã hợp lệ: chuỗi có dấu phẩy và hơn 2 phần → PO là phần tử thứ 2; hoặc chuỗi từ 11 ký tự → cả chuỗi là PO.
   Cùng một mã không được xử lý lại cho tới khi bấm **Reset** hoặc quét mã khác.
3. Tra PO trong SQL (giữ nguyên quy tắc ghép mã khách hàng của hệ thống cũ) rồi bắt đầu hỏi cân (lệnh `SP` mỗi 500 ms).
4. Khi **N lần đọc liên tiếp ổn định** (mặc định 5 lần, lệch ≤ 0.005 kg, ≥ 0.01 kg) thì lưu **một lần**. Nút *Xác nhận cân* lưu ngay số hiện tại.
5. `INSERT ... WHERE NOT EXISTS` (khóa UPDLOCK/HOLDLOCK): cùng PO không bao giờ lưu hai lần, báo "đã cân trước đó".
6. Khối lượng chuẩn = `round(NW/Qty, 3) × số lượng`. Lệch quá `max(dung sai kg, dung sai %)` thì cảnh báo và ghi file JSON vào thư mục log.
7. Mất kết nối SQL khi lưu: bản ghi được **lưu tạm trên máy** (`pending.jsonl`) và tự đồng bộ mỗi 30 giây khi có mạng lại.

## Cài đặt (nút ⚙ hoặc `%LocalAppData%\ScaleScanPro\settings.json`)

Máy chủ/CSDL/tài khoản SQL (mật khẩu mã hóa DPAPI, không lưu chữ thường), cổng COM/baud/lệnh/chu kỳ hỏi cân, camera, dung sai,
độ ổn định, thời gian chờ cân, ngưỡng tin cậy YOLO (**mặc định 0.15 như hệ thống cũ**, chỉ đổi sau khi kiểm thử độ chính xác), thư mục log lệch cân.
Cài đặt áp dụng ngay, không cần mở lại app.

Tài khoản SQL để trống nghĩa là dùng đăng nhập Windows. **Mật khẩu SQL không còn nằm trong mã nguồn** — nhập ở màn hình Cài đặt trên từng máy.

## Thu nhỏ góc màn hình

Nút thu nhỏ (hoặc Win+↓) chuyển thành cửa sổ nhỏ luôn nổi ở góc dưới bên phải: chỉ hiện hình camera, số kg và thông báo
"Đã cân X kg" 3 giây. Nhấp đúp hoặc nút ⤢ để mở lại; đóng cửa sổ mini cũng mở lại cửa sổ chính (không để tiến trình chạy ngầm).
Hộp thông báo thường tự đóng sau 3 giây nhưng vẫn có nút **Xác nhận**.

## Build, test, đóng gói

```powershell
dotnet build
dotnet test
.\publish.ps1            # self-contained, tự kiểm tra đủ file native, nén artifacts\ScaleScanPro-win-x64.zip
```

`publish.ps1` kiểm tra `onnxruntime.dll`, `OpenCvSharpExtern.dll`, `Models\best.onnx` có trong gói — đây là các file từng bị thiếu khi cài bằng ClickOnce
trên hệ thống cũ. Máy đích cần **Visual C++ Redistributable 2015-2022 (x64)**; app kiểm tra và báo ngay khi khởi động.

## Máy quét barcode USB (quét một lần cho cả Scale Scan Pro và app khác)

Máy quét USB ở chế độ bàn phím gõ mã vào cửa sổ đang được chọn. Scale Scan Pro dùng Windows Raw Input để **nghe thêm** đúng máy quét đó,
không chặn và không gửi lại phím: app kia (không cần source) nhận mã như người gõ, Scale Scan Pro nhận cùng mã để tra PO và cân.

1. ⚙ Cài đặt → *Máy quét barcode USB* → **Nhận diện máy quét** → quét thử một mã → Lưu.
2. Khi quét, để **app kia là cửa sổ đang được chọn**; Scale Scan Pro nhận mã dù đang thu nhỏ hay nằm phía sau.
3. Bàn phím thường bị bỏ qua hoàn toàn (không đọc, không lưu, không ghi log). Máy quét được nhận ra theo VID/PID nên cắm sang cổng USB khác vẫn nhận.
4. Mã từ máy quét cho phép quét lại đúng mã vừa xử lý (thao tác cố ý), khác với camera nhìn thấy cùng một tem liên tục.

Lưu ý: phần mềm diệt virus/EDR có thể cảnh báo vì app đọc đầu vào bàn phím; chỉ dùng được với máy quét chế độ bàn phím (HID).

Biến môi trường `SCALESCANPRO_DATA` đặt thư mục dữ liệu khác (chạy thử, bản portable) để không đụng cấu hình thật.

## Chẩn đoán

- Log: `%LocalAppData%\ScaleScanPro\logs\app.log` (xoay vòng 1 MB × 4). Ghi môi trường, file thiếu, lỗi chưa xử lý, trạng thái camera/cân.
- Hộp thoại lỗi hiện lỗi gốc kèm đường dẫn log.
- Chỉ cho phép một bản chạy mỗi máy (tránh tranh cổng COM/camera); mở lần hai sẽ báo "đang chạy".

## Giả định đã chọn (có thể đổi trong Cài đặt)

- Cân hỗ trợ cả hai kiểu: gửi liên tục hoặc chỉ trả lời lệnh `SP` (app hỏi mỗi 500 ms khi đang chờ cân, và đọc mọi dòng có số).
- Dung sai: `max(0.05 kg, 2%)` — hệ thống cũ so sánh bằng nhau tuyệt đối nên rất dễ báo lệch giả.
- Tìm lịch sử theo **đầu mã PO** (dùng được index). Bật "tìm theo chuỗi chứa" nếu cần như cũ (chậm hơn trên bảng lớn).
- Danh sách camera được dò theo chỉ số 0..5 (DirectShow) và hiển thị "Camera N", chưa có tên thiết bị.
- Cấu hình lưu riêng từng máy (không đồng bộ tập trung).
- Cân đang hiển thị đơn vị kg; cột cân trong SQL được ghi nguyên giá trị đọc được.

## Chưa kiểm chứng trên thiết bị thật

Đã kiểm thử bằng test tự động và chế độ giả lập (video QR → YOLO → ZXing → workflow → lưu). Phần chỉ chạy được với phần cứng/hệ thống thật cần
thử trên máy trạm: cổng COM + cân thật, camera USB thật, SQL Server thật (các câu lệnh SQL được viết theo schema cũ nhưng chưa chạy với CSDL thật),
và giao diện trực quan (bố cục WPF chưa được xem bằng mắt).
