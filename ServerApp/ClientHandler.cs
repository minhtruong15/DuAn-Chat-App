using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shared;

namespace ServerApp
{
    public class ClientHandler
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private string _username;
        private string _role;
        private bool _disconnected = false;

        public static readonly List<ClientHandler> Connected = new List<ClientHandler>();

        public Action<object, object> OnLoginSuccess { get; internal set; }
        public Action<object, object, object> OnMessage { get; internal set; }
        public Action<object> OnDisconnected { get; internal set; }

        public ClientHandler(TcpClient c)
        {
            _client = c;
            _stream = c.GetStream();

            lock (Connected)
            {
                Connected.Add(this);
            }

            Task.Run((Func<Task>)Handle);
        }

        // ====== HÀM ĐỌC ĐỦ SỐ BYTE YÊU CẦU (QUAN TRỌNG) ======
        private async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int length)
        {
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset);
                if (read == 0)
                {
                    throw new Exception("Client disconnected");
                }
                offset += read;
            }
        }

        // ======================================================
        // VÒNG LẶP NHẬN PACKET
        // ======================================================
        private async Task Handle()
        {
            try
            {
                while (true)
                {
                    byte[] lenB = new byte[4];
                    await ReadExactAsync(_stream, lenB, 4);

                    int len = BitConverter.ToInt32(lenB, 0);
                    if (len <= 0 || len > 20 * 1024 * 1024)
                        throw new Exception("Invalid packet length");

                    byte[] data = new byte[len];
                    await ReadExactAsync(_stream, data, len);

                    string json = Encoding.UTF8.GetString(data);
                    Packet p = Packet.FromJson(json);

                    if (p != null)
                        await Process(p);
                }
            }
            catch
            {
                // Ngắt kết nối bình thường hoặc lỗi socket
            }
            finally
            {
                Disconnect();
            }
        }

        private async Task Process(Packet p)
        {
            switch (p.Cmd)
            {
                case Command.AuthLogin:
                    {
                        dynamic d = p.Data;
                        string reqUser = ((string)d?.username)?.Trim();
                        string reqPass = (string)d?.password;

                        var u = Database.Authenticate(reqUser, reqPass);

                        if (u != null)
                        {
                            // Nếu tài khoản này đang đăng nhập ở phiên khác thì đóng phiên cũ
                            var existing = FindConnected(u.Item1);
                            if (existing != null && existing != this)
                            {
                                existing.ForceClose();
                            }

                            _username = u.Item1;
                            _role = u.Item2;

                            await Send(new Packet(Command.AuthOk, new
                            {
                                username = _username,
                                role = _role
                            }));

                            OnLoginSuccess?.Invoke(_username, _role);

                            await BroadcastUserListsAsync();
                        }
                        else
                        {
                            await Send(new Packet(Command.AuthFail, new
                            {
                                reason = "Sai tài khoản hoặc mật khẩu"
                            }));
                        }
                        break;
                    }

                case Command.AuthRegister:
                    {
                        dynamic d = p.Data;
                        string reqUser = ((string)d?.username)?.Trim();
                        string reqPass = (string)d?.password;

                        bool ok = Database.Register(reqUser, reqPass);

                        if (ok)
                        {
                            await Send(new Packet(Command.AuthOk, new { username = reqUser, role = "user" }));
                            await BroadcastUserListsAsync();
                        }
                        else
                        {
                            await Send(new Packet(Command.AuthFail, new { reason = "Tên người dùng đã tồn tại hoặc không hợp lệ!" }));
                        }

                        break;
                    }

                case Command.RequestFriendList:
                    {
                        await SendUserList();
                        break;
                    }

                case Command.PrivateMessage:
                    {
                        if (string.IsNullOrEmpty(_username)) break;

                        dynamic d = p.Data;
                        string to = d.to;
                        string payload = d.payload;
                        string time = DateTime.Now.ToString("HH:mm:ss");

                        Database.SavePrivateMessage(_username, to, payload);

                        var messagePacket = new Packet(Command.PrivateMessage, new
                        {
                            from = _username,
                            to = to,
                            payload = payload,
                            time = time
                        });

                        var target = FindConnected(to);
                        if (target != null && target != this)
                        {
                            await target.Send(messagePacket);
                        }

                        await Send(messagePacket);

                        var content = MessageContent.Deserialize(payload);
                        string logPreview = content.IsImage
                            ? $"[Hình ảnh: {content.FileName}]"
                            : content.Text;
                        OnMessage?.Invoke(_username, to, logPreview);

                        break;
                    }

                case Command.RequestPrivateHistory:
                    {
                        if (string.IsNullOrEmpty(_username)) break;

                        dynamic d = p.Data;
                        string withUser = d.with;

                        var history = Database.GetPrivateHistory(_username, withUser);

                        await Send(new Packet(Command.PrivateHistory, new
                        {
                            messages = history
                        }));

                        break;
                    }

                // ==== ADMIN COMMANDS QUA TCP ====
                case Command.AdminGetUsers:
                    {
                        dynamic d = p.Data;
                        string keyword = (string)d?.keyword ?? "";
                        var onlineSet = GetOnlineUsernamesSet();
                        var users = Database.GetAdminUsers(keyword, onlineSet);
                        await Send(new Packet(Command.AdminUsersResult, new { users = users }));
                        break;
                    }

                case Command.AdminAddUser:
                    {
                        dynamic d = p.Data;
                        string u = (string)d?.username;
                        string pass = (string)d?.password;
                        string role = (string)d?.role;

                        bool ok = Database.AddUser(u, pass, role, out string err);
                        await Send(new Packet(Command.AdminActionResult, new
                        {
                            success = ok,
                            message = ok ? "Thêm tài khoản thành công!" : $"Lỗi thêm user: {err}"
                        }));

                        if (ok)
                            await BroadcastUserListsAsync();
                        break;
                    }

                case Command.AdminUpdateUser:
                    {
                        dynamic d = p.Data;
                        string u = (string)d?.username;
                        string pass = (string)d?.password;
                        string role = (string)d?.role;

                        bool ok = Database.UpdateUser(u, pass, role, out string err);
                        await Send(new Packet(Command.AdminActionResult, new
                        {
                            success = ok,
                            message = ok ? "Cập nhật thông tin tài khoản thành công!" : $"Lỗi cập nhật: {err}"
                        }));

                        if (ok)
                            await BroadcastUserListsAsync();
                        break;
                    }

                case Command.AdminDeleteUser:
                    {
                        dynamic d = p.Data;
                        string u = (string)d?.username;

                        if (string.Equals(u, _username, StringComparison.OrdinalIgnoreCase))
                        {
                            await Send(new Packet(Command.AdminActionResult, new
                            {
                                success = false,
                                message = $"Không thể xóa tài khoản đang đăng nhập '{u}'!"
                            }));
                            break;
                        }

                        bool ok = Database.DeleteUser(u, out string err);
                        await Send(new Packet(Command.AdminActionResult, new
                        {
                            success = ok,
                            message = ok ? $"Đã xóa tài khoản '{u}' thành công!" : $"Lỗi xóa user: {err}"
                        }));

                        if (ok)
                        {
                            var target = FindConnected(u);
                            target?.ForceClose();
                            await BroadcastUserListsAsync();
                        }
                        break;
                    }
            }
        }

        private async Task SendUserList()
        {
            List<string> users = Database.GetAllUsers();
            var onlineUsers = GetOnlineUsernamesList();

            users.RemoveAll(u => string.Equals(u, _username, StringComparison.OrdinalIgnoreCase));
            users.RemoveAll(u => u.Equals("admin", StringComparison.OrdinalIgnoreCase));

            await Send(new Packet(Command.UpdateFriendList, new
            {
                users = users,
                onlineUsers = onlineUsers
            }));
        }

        public async Task Send(Packet p)
        {
            if (_disconnected) return;

            string json = p.ToJson();
            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] len = BitConverter.GetBytes(data.Length);

            byte[] frame = new byte[4 + data.Length];
            Buffer.BlockCopy(len, 0, frame, 0, 4);
            Buffer.BlockCopy(data, 0, frame, 4, data.Length);

            await _sendLock.WaitAsync();
            try
            {
                if (!_disconnected && _client.Connected)
                {
                    await _stream.WriteAsync(frame, 0, frame.Length);
                    await _stream.FlushAsync();
                }
            }
            catch
            {
                // Ignore send errors on disconnecting sockets
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void ForceClose()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch { }
        }

        public static void DisconnectAll()
        {
            List<ClientHandler> snapshot;
            lock (Connected)
            {
                snapshot = Connected.ToList();
                Connected.Clear();
            }

            foreach (var h in snapshot)
            {
                h._disconnected = true;
                h.ForceClose();
            }
        }

        private void Disconnect()
        {
            if (_disconnected) return;
            _disconnected = true;

            try
            {
                if (_username != null)
                    OnDisconnected?.Invoke(_username);

                _stream?.Close();
                _client?.Close();
            }
            catch { }

            bool removed = false;
            lock (Connected)
            {
                removed = Connected.Remove(this);
            }

            if (removed)
            {
                _ = BroadcastUserListsAsync();
            }
        }

        private static ClientHandler FindConnected(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            lock (Connected)
            {
                return Connected.FirstOrDefault(x =>
                    string.Equals(x._username, username, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static HashSet<string> GetOnlineUsernamesSet()
        {
            lock (Connected)
            {
                return new HashSet<string>(
                    Connected.Where(x => !string.IsNullOrEmpty(x._username)).Select(x => x._username),
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        private static List<string> GetOnlineUsernamesList()
        {
            lock (Connected)
            {
                return Connected
                    .Where(x => !string.IsNullOrEmpty(x._username))
                    .Select(x => x._username)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public static async Task BroadcastUserListsAsync()
        {
            List<ClientHandler> snapshot;

            lock (Connected)
            {
                snapshot = Connected.ToList();
            }

            if (snapshot.Count == 0)
                return;

            List<string> allUsers;
            try
            {
                allUsers = Database.GetAllUsers();
            }
            catch
            {
                return;
            }

            var onlineUsers = GetOnlineUsernamesList();

            foreach (var handler in snapshot)
            {
                if (handler._username == null) continue;

                var users = allUsers
                    .Where(u => !u.Equals("admin", StringComparison.OrdinalIgnoreCase))
                    .Where(u => !string.Equals(u, handler._username, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                await handler.Send(new Packet(Command.UpdateFriendList, new
                {
                    users = users,
                    onlineUsers = onlineUsers
                }));
            }
        }
    }
}
