using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Shared;

namespace ClientApp
{
    public partial class RoomForm : Form
    {
        private const string SearchPlaceholder = "Nhập tên người dùng...";

        private readonly ChatClient _client;
        private readonly string _username;
        private readonly Dictionary<string, ChatForm> _openChats = new Dictionary<string, ChatForm>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _allUsers = new List<string>();
        private readonly HashSet<string> _onlineUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unreadUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _isLoggingOut = false;

        public RoomForm(ChatClient client, string username)
        {
            InitializeComponent();
            _client = client;
            _username = username;

            lblWelcome.Text = "Xin chào, " + _username;

            // Bảo đảm không đăng ký trùng sự kiện
            _client.OnPacketReceived -= HandlePacket;
            _client.OnPacketReceived += HandlePacket;
            _client.OnDisconnected -= HandleDisconnected;
            _client.OnDisconnected += HandleDisconnected;

            this.FormClosing += RoomForm_FormClosing;

            // Gửi yêu cầu danh sách bạn bè
            _ = _client.SendAsync(new Packet(Command.RequestFriendList, new { }));

            // Sự kiện mở cửa sổ chat khi double click
            lstUsers.DoubleClick += lstUsers_DoubleClick;

            // Placeholder cho ô tìm kiếm
            txtSearch.Text = SearchPlaceholder;
            txtSearch.ForeColor = Color.Gray;
            txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    RefreshUserListDisplay();
                }
            };

            NotificationHelper.Initialize();
        }

        private void HandleDisconnected()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                Invoke(new Action(() =>
                {
                    lblCount.Text = "⚠️ Mất kết nối tới Server!";
                    lblCount.ForeColor = Color.FromArgb(220, 38, 38);
                }));
            }
            catch { }
        }

        // 🎨 Placeholder - focus
        private void TxtSearch_GotFocus(object sender, EventArgs e)
        {
            if (txtSearch.Text == SearchPlaceholder)
            {
                txtSearch.Text = "";
                txtSearch.ForeColor = Color.Black;
            }
        }

        // 🎨 Placeholder - mất focus
        private void TxtSearch_LostFocus(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                txtSearch.Text = SearchPlaceholder;
                txtSearch.ForeColor = Color.Gray;
            }
        }

        // 🧠 Xử lý gói tin đến từ server
        private void HandlePacket(Packet p)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                Invoke(new Action<Packet>(HandlePacket), p);
                return;
            }

            try
            {
                switch (p.Cmd)
                {
                    case Command.UpdateFriendList:
                        {
                            dynamic d = p.Data;
                            _allUsers.Clear();
                            _onlineUsers.Clear();

                            if (d?.onlineUsers != null)
                            {
                                foreach (var onlineObj in d.onlineUsers)
                                {
                                    string onName = onlineObj?.ToString();
                                    if (!string.IsNullOrWhiteSpace(onName))
                                        _onlineUsers.Add(onName);
                                }
                            }

                            if (d?.users != null)
                            {
                                foreach (var userObj in d.users)
                                {
                                    string name = userObj?.ToString();
                                    if (!string.IsNullOrWhiteSpace(name) &&
                                        !string.Equals(name, _username, StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(name, "admin", StringComparison.OrdinalIgnoreCase))
                                    {
                                        _allUsers.Add(name);
                                    }
                                }
                            }

                            // Ưu tiên hiển thị user đang online lên trên, sau đó sắp xếp theo tên
                            _allUsers.Sort((a, b) =>
                            {
                                bool aOn = _onlineUsers.Contains(a);
                                bool bOn = _onlineUsers.Contains(b);
                                if (aOn != bOn) return bOn.CompareTo(aOn);
                                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                            });

                            RefreshUserListDisplay();
                            break;
                        }

                    case Command.PrivateMessage:
                        {
                            dynamic d = p.Data;
                            string from = d.from;
                            string to = d.to;
                            string payload = d.payload ?? d.text;

                            // 🧩 Chỉ xử lý tin nhắn gửi cho mình
                            if (!string.Equals(to, _username, StringComparison.OrdinalIgnoreCase))
                                return;

                            // ✅ Nếu ChatForm với người này đang mở → ChatForm tự phát âm thanh & hiển thị
                            if (_openChats.TryGetValue(from, out var openChat) && openChat != null && !openChat.IsDisposed)
                            {
                                return;
                            }

                            // 🟡 Nếu chưa mở → đánh dấu có tin mới
                            _unreadUsers.Add(from);
                            RefreshUserListDisplay();

                            // Phát âm thanh & thông báo khi nhận tin nhắn
                            SoundHelper.PlayReceiveSound();

                            var content = MessageContent.Deserialize(payload);
                            string shortText = content.IsImage ? "[Hình ảnh]" : content.Text;
                            NotificationHelper.ShowReceiveNotification(from, shortText);
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi xử lý gói tin: " + ex.Message);
            }
        }

        private void RefreshUserListDisplay()
        {
            string keyword = txtSearch.Text.Trim();
            if (keyword == SearchPlaceholder)
                keyword = string.Empty;

            lstUsers.BeginUpdate();
            lstUsers.Items.Clear();

            foreach (var user in _allUsers)
            {
                if (string.IsNullOrEmpty(keyword) ||
                    user.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lstUsers.Items.Add(user);
                }
            }

            lstUsers.EndUpdate();

            int onlineCount = _allUsers.Count(u => _onlineUsers.Contains(u));
            lblCount.ForeColor = Color.FromArgb(71, 85, 105);
            lblCount.Text = $"Tổng số người dùng: {_allUsers.Count}  •  Đang hoạt động: {onlineCount}";
        }

        // 💬 Mở phòng chat khi double click
        private void lstUsers_DoubleClick(object sender, EventArgs e)
        {
            if (lstUsers.SelectedItem == null) return;

            string friend = lstUsers.SelectedItem.ToString();
            if (friend.Equals("admin", StringComparison.OrdinalIgnoreCase)) return;

            _unreadUsers.Remove(friend);
            lstUsers.Invalidate();

            // Nếu chat đã mở thì đưa lên trước
            if (_openChats.TryGetValue(friend, out var existingChat) && existingChat != null && !existingChat.IsDisposed)
            {
                existingChat.Show();
                existingChat.BringToFront();
                return;
            }

            bool isFriendOnline = _onlineUsers.Contains(friend);
            ChatForm chatForm = new ChatForm(_client, _username, friend, isFriendOnline);
            _openChats[friend] = chatForm;

            this.Hide();

            chatForm.FormClosed += delegate
            {
                if (!this.IsDisposed && !_isLoggingOut)
                {
                    this.Show();
                    this.BringToFront();
                    RefreshUserListDisplay();
                }
            };

            chatForm.Show();
        }

        // 🔍 Nút tìm kiếm
        private void btnSearch_Click(object sender, EventArgs e)
        {
            RefreshUserListDisplay();
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            RefreshUserListDisplay();
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            _isLoggingOut = true;

            foreach (var chat in _openChats.Values.ToList())
            {
                if (chat != null && !chat.IsDisposed)
                    chat.Close();
            }
            _openChats.Clear();

            _client.OnPacketReceived -= HandlePacket;
            _client.OnDisconnected -= HandleDisconnected;
            _client.Close();

            new LoginForm().Show();
            this.Close();
        }

        private void RoomForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _client.OnPacketReceived -= HandlePacket;
            _client.OnDisconnected -= HandleDisconnected;

            if (!_isLoggingOut)
            {
                _client.Close();
                Application.Exit();
            }
        }

        private void RoomForm_Load(object sender, EventArgs e)
        {
        }

        private void lstUsers_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= lstUsers.Items.Count) return;

            string displayName = lstUsers.Items[e.Index].ToString();
            bool hasNew = _unreadUsers.Contains(displayName);
            bool isOnline = _onlineUsers.Contains(displayName);

            var bounds = e.Bounds;
            Color baseColor = ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
                ? Color.FromArgb(224, 231, 255)
                : Color.White;

            using (var bgBrush = new SolidBrush(baseColor))
                e.Graphics.FillRectangle(bgBrush, bounds);

            // Đường kẻ phân cách mờ dưới mỗi dòng
            using (var linePen = new Pen(Color.FromArgb(241, 245, 249)))
                e.Graphics.DrawLine(linePen, bounds.Left + 16, bounds.Bottom - 1, bounds.Right - 16, bounds.Bottom - 1);

            var avatar = AvatarHelper.GetAvatar(displayName, 48);
            var avatarRect = new Rectangle(bounds.Left + 16, bounds.Top + 10, 48, 48);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawImage(avatar, avatarRect);

            // Chấm trạng thái Online / Offline ở góc dưới phải avatar
            var dotRect = new Rectangle(avatarRect.Right - 13, avatarRect.Bottom - 13, 13, 13);
            using (var borderBrush = new SolidBrush(baseColor))
            using (var dotBrush = new SolidBrush(isOnline ? Color.FromArgb(16, 185, 129) : Color.FromArgb(148, 163, 184)))
            {
                e.Graphics.FillEllipse(borderBrush, dotRect.X - 2, dotRect.Y - 2, dotRect.Width + 4, dotRect.Height + 4);
                e.Graphics.FillEllipse(dotBrush, dotRect);
            }

            using (var nameBrush = new SolidBrush(Color.FromArgb(30, 41, 59)))
            using (var nameFont = new Font("Segoe UI Semibold", 11F))
            {
                var nameRect = new Rectangle(bounds.Left + 78, bounds.Top + 11, bounds.Width - 210, 24);
                e.Graphics.DrawString(displayName, nameFont, nameBrush, nameRect);
            }

            string statusText = isOnline ? "● Đang hoạt động" : "○ Ngoại tuyến";
            Color statusColor = isOnline ? Color.FromArgb(5, 150, 105) : Color.FromArgb(100, 116, 139);
            using (var statusBrush = new SolidBrush(statusColor))
            using (var statusFont = new Font("Segoe UI", 9F))
            {
                var statusRect = new Rectangle(bounds.Left + 78, bounds.Top + 36, bounds.Width - 210, 20);
                e.Graphics.DrawString(statusText, statusFont, statusBrush, statusRect);
            }

            if (hasNew)
            {
                var badgeRect = new Rectangle(bounds.Right - 120, bounds.Top + 21, 100, 26);
                using (var badgeBrush = new SolidBrush(Color.FromArgb(190, 242, 100)))
                using (var textBrush = new SolidBrush(Color.FromArgb(63, 98, 18)))
                using (var badgeFont = new Font("Segoe UI", 9F, FontStyle.Bold))
                {
                    var radius = 12;
                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(badgeRect.Left, badgeRect.Top, radius, radius, 180, 90);
                        path.AddArc(badgeRect.Right - radius, badgeRect.Top, radius, radius, 270, 90);
                        path.AddArc(badgeRect.Right - radius, badgeRect.Bottom - radius, radius, radius, 0, 90);
                        path.AddArc(badgeRect.Left, badgeRect.Bottom - radius, radius, radius, 90, 90);
                        path.CloseFigure();
                        e.Graphics.FillPath(badgeBrush, path);
                    }
                    var textRect = new RectangleF(badgeRect.Left, badgeRect.Top + 4, badgeRect.Width, badgeRect.Height);
                    var format = new StringFormat { Alignment = StringAlignment.Center };
                    e.Graphics.DrawString("TIN MỚI", badgeFont, textBrush, textRect, format);
                }
            }

            e.DrawFocusRectangle();
        }
    }
}
