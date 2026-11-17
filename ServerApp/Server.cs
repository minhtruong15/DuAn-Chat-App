using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ServerApp
{
    public class Server
    {
        private TcpListener _listener;
        private bool _isRunning = false;

        public bool IsRunning => _isRunning;
        public static event Action<string> OnServerLog;

        // ======================================================
        // START SERVER
        // ======================================================
        public async void Start(int port = 5000)
        {
            if (_isRunning) return;

            if (Database.EnsureDatabaseInitialized(out string dbErr))
            {
                Log("✅ Kết nối & khởi tạo MySQL Database (chatdb) thành công.");
            }
            else
            {
                Log($"⚠️ Cảnh báo MySQL: {dbErr}");
            }

            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _isRunning = true;

                Log($"🚀 Server started at port {port}");
                Log("⏳ Waiting for clients...");
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Log($"❌ Không thể mở cổng {port}: {ex.Message}");
                return;
            }

            while (_isRunning)
            {
                try
                {
                    TcpClient c = await _listener.AcceptTcpClientAsync();
                    AttachEvents(new ClientHandler(c));
                }
                catch (ObjectDisposedException)
                {
                    // Listener bị đóng khi gọi Stop()
                    break;
                }
                catch (SocketException)
                {
                    if (!_isRunning) break;
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                        Log($"⚠️ Lỗi nhận kết nối: {ex.Message}");
                }
            }
        }

        // ======================================================
        // GẮN SỰ KIỆN TỪ CLIENT → SERVER LOG
        // ======================================================
        private void AttachEvents(ClientHandler handler)
        {
            handler.OnLoginSuccess = (user, role) =>
            {
                Log($"🟢 [LOGIN] {user} logged in as {role}.");
            };

            handler.OnMessage = (from, to, msg) =>
            {
                Log($"💬 [MESSAGE] {from} → {to}: {msg}");
            };

            handler.OnDisconnected = (user) =>
            {
                Log($"🔴 [LEFT] {user} disconnected.");
            };
        }

        // ======================================================
        // STOP SERVER
        // ======================================================
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            try
            {
                _listener?.Stop();
            }
            catch { }

            ClientHandler.DisconnectAll();
            Log("🛑 Server stopped.");
        }

        private void Log(string msg)
        {
            Console.WriteLine(msg);
            OnServerLog?.Invoke(msg);
        }
    }
}
