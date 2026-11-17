# 💬 NetChatSolution — Ứng dụng Chat Realtime Client–Server (C# WinForms & TCP Socket)

![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=for-the-badge&logo=dotnet)
![C#](https://img.shields.io/badge/Language-C%23-239120?style=for-the-badge&logo=c-sharp)
![MySQL](https://img.shields.io/badge/Database-MySQL-4479A1?style=for-the-badge&logo=mysql&logoColor=white)
![WinForms](https://img.shields.io/badge/UI-Windows%20Forms-0078D6?style=for-the-badge&logo=windows)

**NetChatSolution** là đồ án Lập Trình Mạng xây dựng hệ thống nhắn tin thời gian thực (**Realtime Chat Application**) theo kiến trúc **Client–Server** sử dụng giao thức **TCP Socket (`TcpListener` / `TcpClient`)**, đóng gói dữ liệu JSON có độ dài khung truyền (**Length-Prefixed Framing**), kết hợp lưu trữ cơ sở dữ liệu **MySQL** và giao diện **Windows Forms** hiện đại.

---

## 🌟 Tính năng nổi bật

### 👤 Người dùng (ClientApp)
- **Xác thực tài khoản**: Đăng nhập, Đăng ký tài khoản mới nhanh chóng, hỗ trợ ẩn/hiện mật khẩu và phím tắt `Enter`.
- **Danh sách bạn bè & Trạng thái Realtime**:
  - Tự động cập nhật danh sách người dùng và trạng thái **🟢 Đang hoạt động (Online)** / **⚪ Ngoại tuyến (Offline)** theo thời gian thực.
  - Tự động sinh Avatar chữ cái màu sắc (Initials Avatar) kèm chấm trạng thái Online.
  - Tìm kiếm người dùng tức thì (Real-time search filter) và hiển thị huy hiệu **`TIN MỚI`** khi có tin nhắn chưa đọc.
- **Trò chuyện 1–1 (Private Chat)**:
  - Giao diện bong bóng chat (Chat Bubbles) bo góc hiện đại, tự động co giãn khi thay đổi kích thước cửa sổ.
  - **Gửi văn bản & Hình ảnh (`📷 Ảnh`)**: Hỗ trợ gửi ảnh trực tiếp qua TCP (mã hóa Base64, tối ưu kích thước) và bấm vào ảnh để phóng to xem chi tiết.
  - **Đồng bộ lịch sử trò chuyện**: Tự động tải toàn bộ lịch sử tin nhắn (văn bản & hình ảnh) từ MySQL khi mở cuộc trò chuyện.
- **Âm thanh & Thông báo hệ thống**:
  - Phát hiệu ứng âm thanh (`.wav`) khi gửi và nhận tin nhắn (tự động tạo file WAV dự phòng nếu thiếu).
  - Hiển thị thông báo góc màn hình (**System Tray Balloon Notification**) khi có tin nhắn đến.

### 🛡️ Quản trị viên (Admin Dashboard)
- Quản lý danh sách toàn bộ người dùng thông qua giao thức TCP với Server (hoặc kết nối MySQL trực tiếp).
- Theo dõi trạng thái **Online / Offline** của từng tài khoản theo thời gian thực.
- **Thêm / Sửa / Xóa** tài khoản, phân quyền (`admin` / `user`), tìm kiếm tài khoản và tự động đồng bộ danh sách tới các Client đang kết nối.

### 🖥️ Máy chủ (ServerApp)
- Lắng nghe đa luồng bất đồng bộ (`async/await`) trên cổng TCP `5000`.
- **Tự động khởi tạo Database (`chatdb`)**, bảng `users`, `messages` và tài khoản `admin` mặc định ngay khi nhấn **Khởi động Server**.
- Ghi nhận nhật ký hoạt động (Server Logs) theo thời gian thực: kết nối, đăng nhập, gửi tin nhắn/hình ảnh, ngắt kết nối.

---

## 🏗️ Kiến trúc Hệ thống & Cấu trúc Thư mục

```text
NetChatSolution/
├── 📂 Shared/                  # Thư viện dùng chung giữa Client và Server
│   ├── Command.cs              # Định nghĩa các mã lệnh giao thức (Auth, Chat, Admin...)
│   ├── Packet.cs               # Đóng gói & giải mã gói tin JSON (Cmd + Data)
│   ├── MessageContent.cs       # Cấu trúc nội dung tin nhắn (Text / Image Base64)
│   ├── Models.cs               # Các lớp DTO (UserDto, AdminUserDto, MessageDto...)
│   └── Net.cs                  # Tiện ích truyền/nhận khung dữ liệu qua NetworkStream
│
├── 📂 ServerApp/               # Ứng dụng Máy chủ (TCP Server)
│   ├── ServerMainForm.cs       # Giao diện điều khiển Server & theo dõi Log realtime
│   ├── Server.cs               # Quản lý TcpListener và vòng lặp chấp nhận kết nối
│   ├── ClientHandler.cs        # Xử lý luồng giao tiếp riêng cho từng Client
│   ├── Database.cs             # Kết nối MySQL, tự động tạo bảng & truy vấn dữ liệu
│   └── app.config              # Cấu hình chuỗi kết nối MySQL (ConnectionString)
│
├── 📂 ClientApp/               # Ứng dụng Khách (WinForms Client)
│   ├── LoginForm.cs            # Màn hình Đăng nhập
│   ├── RegisterForm.cs         # Màn hình Đăng ký tài khoản
│   ├── RoomForm.cs             # Trang chủ danh sách người dùng & trạng thái Online
│   ├── ChatForm.cs             # Cửa sổ chat 1-1 (Văn bản & Hình ảnh)
│   ├── AdminForm.cs            # Bảng điều khiển dành cho Quản trị viên (Admin)
│   ├── ChatClient.cs           # Quản lý kết nối TCP bất đồng bộ phía Client
│   ├── AvatarHelper.cs         # Tự động vẽ Avatar theo tên người dùng
│   ├── SoundHelper.cs          # Quản lý hiệu ứng âm thanh thông báo
│   ├── NotificationHelper.cs   # Quản lý thông báo khay hệ thống (NotifyIcon)
│   └── Sounds/                 # Thư mục chứa các file âm thanh (.wav)
│
└── 📄 NetChatSolution.sln      # Tập tin Solution cho Visual Studio
```

---

## 🗄️ Cấu trúc Cơ sở dữ liệu (MySQL)

> 💡 **Lưu ý**: Khi khởi chạy **ServerApp**, hệ thống sẽ **tự động tạo** database `chatdb`, các bảng `users`, `messages` và tài khoản `admin` mặc định nếu chưa tồn tại. Bạn cũng có thể chạy thủ công script SQL dưới đây:

```sql
CREATE DATABASE IF NOT EXISTS chatdb CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE chatdb;

CREATE TABLE IF NOT EXISTS users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    password VARCHAR(100) NOT NULL,
    role VARCHAR(10) DEFAULT 'user'
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS messages (
    id INT AUTO_INCREMENT PRIMARY KEY,
    sender VARCHAR(50),
    receiver VARCHAR(50),
    text MEDIUMTEXT,
    time DATETIME DEFAULT CURRENT_TIMESTAMP
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- Tài khoản Admin mặc định
INSERT IGNORE INTO users (username, password, role) VALUES ('admin', '123456', 'admin');
```

---

## ⚙️ Hướng dẫn Cài đặt & Chạy Dự án

### 1. Yêu cầu hệ thống
- **Hệ điều hành**: Windows 10 / 11
- **IDE**: Visual Studio 2019 / 2022 (hỗ trợ **.NET Framework 4.8**)
- **Cơ sở dữ liệu**: MySQL Server (XAMPP, MySQL Workbench, WampServer...) chạy ở cổng `3306`

### 2. Cấu hình kết nối MySQL
Kiểm tra chuỗi kết nối trong `ServerApp/app.config` và `ClientApp/App.config` (mặc định):
```xml
<connectionStrings>
  <add name="ChatDb" connectionString="Server=127.0.0.1;Port=3306;Database=chatdb;Uid=root;Pwd=1582005;Charset=utf8mb4;" />
</connectionStrings>
```
*(Hãy thay đổi `Uid` và `Pwd` phù hợp với mật khẩu MySQL trên máy của bạn).*

### 3. Khởi chạy ứng dụng
1. Mở file **`NetChatSolution.sln`** bằng **Visual Studio**.
2. Nhấn **Build Solution (`Ctrl + Shift + B`)** để biên dịch toàn bộ project.
3. Chạy **`ServerApp`** trước → Nhấn nút **"Khởi động Server"** (Server sẽ lắng nghe tại port `5000` và tự động khởi tạo CSDL).
4. Chạy một hoặc nhiều cửa sổ **`ClientApp`**:
   - Đăng nhập bằng tài khoản **Admin mặc định**: `admin` / `123456` để mở bảng quản lý người dùng.
   - Hoặc nhấn **"Chưa có tài khoản? Đăng ký"** để tạo các tài khoản `user` mới và bắt đầu nhắn tin, gửi ảnh realtime giữa các cửa sổ!

---

## 📡 Giao thức Truyền thông (Protocol Framing)

Để giải quyết triệt để hiện tượng phân mảnh hoặc dính gói tin trên luồng TCP (**TCP Stream Framing**), mọi gói tin giữa `ClientApp` và `ServerApp` đều tuân theo cấu trúc:

```text
+------------------------+------------------------------------------------+
| Độ dài gói tin (4 Byte)| Nội dung JSON UTF-8 (Cmd + Data)               |
| (Int32 Little-Endian)  | Ví dụ: {"Cmd":12,"Data":{"to":"b","payload":..}}|
+------------------------+------------------------------------------------+
```

---

## 👨‍💻 Thông tin Dự án

- **Repository**: [https://github.com/minhtruong15/DuAn-Chat-App](https://github.com/minhtruong15/DuAn-Chat-App)
- **Môn học**: Lập Trình Mạng — Đồ án Cuối Kỳ
