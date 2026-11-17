using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Shared;

namespace ClientApp
{
    public partial class ChatForm : Form
    {
        private const string MessageRowTag = "MESSAGE_ROW";
        private const string BubbleControlTag = "MESSAGE_BUBBLE";
        private const int AvatarSize = 44;

        private readonly ChatClient _client;
        private readonly string _username;
        private readonly string _friend;
        private bool _isFriendOnline;

        private readonly HashSet<string> _pendingSelfMessages = new HashSet<string>();

        public ChatForm(ChatClient client, string username, string friend, bool isFriendOnline = false)
        {
            InitializeComponent();
            DoubleBuffered = true;

            flowMessages.AutoScrollMargin = new Size(0, 60);
            flowMessages.SizeChanged += FlowMessages_SizeChanged;

            ApplyRoundedCorners(messageBox, 18);
            messageBox.Paint += MessageBox_Paint;

            _client = client;
            _username = username;
            _friend = friend;
            _isFriendOnline = isFriendOnline;

            UpdateHeaderStatus();

            // đảm bảo không đăng ký 2 lần
            _client.OnPacketReceived -= HandlePacket;
            _client.OnPacketReceived += HandlePacket;

            _ = _client.SendAsync(new Packet(Command.RequestPrivateHistory, new { with = _friend }));

            txtMessage.KeyDown += TxtMessage_KeyDown;
            NotificationHelper.Initialize();
        }

        private void UpdateHeaderStatus()
        {
            string statusBadge = _isFriendOnline ? "🟢 Đang hoạt động" : "⚪ Ngoại tuyến";
            lblRoom.Text = $"Đang trò chuyện với {_friend}   ({statusBadge})";
            this.Text = $"Trò chuyện với {_friend} - NetChat";
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        // =========================================================
        // TỰ ĐIỀU CHỈNH BUBBLE KHI RESIZE FORM
        // =========================================================
        private void FlowMessages_SizeChanged(object sender, EventArgs e)
        {
            int rowWidth = CalculateRowWidth();
            int bubbleWidth = CalculateBubbleWidth(rowWidth);

            foreach (Control control in flowMessages.Controls)
            {
                if (control.Tag as string == MessageRowTag)
                {
                    control.Width = rowWidth;
                    control.MinimumSize = new Size(rowWidth, 0);
                    control.MaximumSize = new Size(rowWidth, int.MaxValue);
                }

                UpdateBubbleWidth(control, bubbleWidth);
            }

            flowMessages.PerformLayout();
        }

        private void TxtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                btnSend.PerformClick();
            }
        }

        // =========================================================
        // GỬI TIN NHẮN (TEXT & IMAGE)
        // =========================================================
        private async void btnSend_Click(object sender, EventArgs e)
        {
            string msg = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(msg)) return;

            var payload = MessageContent.CreateText(msg);
            txtMessage.Clear();
            await SendPayloadAsync(payload);
        }

        private async void btnSendImage_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Chọn hình ảnh để gửi";
                dlg.Filter = "Hình ảnh (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp";

                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    string base64 = EncodeImageToBase64(dlg.FileName, 900);
                    string fileName = Path.GetFileName(dlg.FileName);
                    var payload = MessageContent.CreateImage(fileName, base64);
                    await SendPayloadAsync(payload);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Không thể gửi hình ảnh: " + ex.Message, "Lỗi",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string EncodeImageToBase64(string filePath, int maxDimension)
        {
            using (var original = Image.FromFile(filePath))
            {
                int w = original.Width;
                int h = original.Height;

                if (w > maxDimension || h > maxDimension)
                {
                    double ratio = Math.Min((double)maxDimension / w, (double)maxDimension / h);
                    w = Math.Max(1, (int)(w * ratio));
                    h = Math.Max(1, (int)(h * ratio));
                }

                using (var resized = new Bitmap(w, h))
                using (var g = Graphics.FromImage(resized))
                using (var ms = new MemoryStream())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawImage(original, 0, 0, w, h);
                    resized.Save(ms, ImageFormat.Jpeg);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        private async System.Threading.Tasks.Task SendPayloadAsync(MessageContent content)
        {
            if (content == null) return;

            string serialized = MessageContent.Serialize(content);

            lock (_pendingSelfMessages)
            {
                if (!string.IsNullOrWhiteSpace(content.ClientMessageId))
                    _pendingSelfMessages.Add(content.ClientMessageId);
            }

            // hiển thị ngay để người dùng thấy phản hồi tức thì
            AddMessageBubble(_username, content, DateTime.Now, true);
            SoundHelper.PlaySendSound();

            string previewText = content.IsImage ? $"[Hình ảnh: {content.FileName}]" : content.Text;
            NotificationHelper.ShowSendNotification(_friend, previewText);

            await _client.SendAsync(new Packet(Command.PrivateMessage, new
            {
                to = _friend,
                payload = serialized
            }));
        }

        // =========================================================
        // NHẬN PACKET TỪ SERVER
        // =========================================================
        private void HandlePacket(Packet p)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                Invoke(new Action<Packet>(HandlePacket), p);
                return;
            }

            switch (p.Cmd)
            {
                case Command.UpdateFriendList:
                    {
                        dynamic d = p.Data;
                        bool online = false;
                        if (d?.onlineUsers != null)
                        {
                            foreach (var u in d.onlineUsers)
                            {
                                if (EqualsUser(u?.ToString(), _friend))
                                {
                                    online = true;
                                    break;
                                }
                            }
                        }
                        _isFriendOnline = online;
                        UpdateHeaderStatus();
                        break;
                    }

                case Command.PrivateMessage:
                    {
                        dynamic d = p.Data;
                        string from = d.from;
                        string payload = d.payload;
                        string time = d.time;
                        string toUser = d.to;

                        bool isSelf = EqualsUser(from, _username);
                        bool isFriend = EqualsUser(from, _friend);

                        bool shouldRender =
                            (isSelf && EqualsUser(toUser, _friend)) ||
                            (isFriend && EqualsUser(toUser, _username));

                        if (shouldRender)
                        {
                            var content = MessageContent.Deserialize(payload);
                            DateTime dt = ParseTime(time);

                            if (isSelf && SuppressIfPending(content))
                                break;

                            AddMessageBubble(from, content, dt, isSelf);

                            if (!isSelf)
                            {
                                SoundHelper.PlayReceiveSound();
                                string preview = content.IsImage ? "[Hình ảnh]" : content.Text;
                                NotificationHelper.ShowReceiveNotification(_friend, preview);
                            }
                        }
                    }
                    break;

                case Command.PrivateHistory:
                    {
                        dynamic d = p.Data;
                        if (d?.messages == null) break;

                        flowMessages.SuspendLayout();
                        flowMessages.Controls.Clear();

                        Control lastRow = null;
                        foreach (var m in d.messages)
                        {
                            string from = m.sender;
                            string payload = m.payload;
                            string time = m.time;

                            var content = MessageContent.Deserialize(payload);
                            DateTime dt = ParseTime(time);

                            lastRow = RenderMessageBubble(from, content, dt, EqualsUser(from, _username), scrollImmediately: false);
                        }

                        flowMessages.ResumeLayout();
                        if (lastRow != null)
                            flowMessages.ScrollControlIntoView(lastRow);
                    }
                    break;
            }
        }

        private DateTime ParseTime(string t)
        {
            if (DateTime.TryParse(t, out var dt)) return dt;
            return DateTime.Now;
        }

        private bool SuppressIfPending(MessageContent content)
        {
            if (content == null || string.IsNullOrWhiteSpace(content.ClientMessageId))
                return false;

            lock (_pendingSelfMessages)
            {
                if (_pendingSelfMessages.Remove(content.ClientMessageId))
                    return true;
            }
            return false;
        }

        // =========================================================
        // VẼ BUBBLE
        // =========================================================
        private void AddMessageBubble(string sender, MessageContent content, DateTime at, bool isSelf)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AddMessageBubble(sender, content, at, isSelf)));
                return;
            }

            RenderMessageBubble(sender, content, at, isSelf, scrollImmediately: true);
        }

        private Control RenderMessageBubble(string sender, MessageContent content, DateTime at, bool isSelf, bool scrollImmediately = true)
        {
            int rowWidth = CalculateRowWidth();
            int bubbleWidth = CalculateBubbleWidth(rowWidth);

            var row = new TableLayoutPanel
            {
                Tag = MessageRowTag,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                Width = rowWidth,
                MinimumSize = new Size(rowWidth, 0),
                MaximumSize = new Size(rowWidth, int.MaxValue),
                Margin = new Padding(0, 0, 0, 22),
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };

            PictureBox avatar = BuildAvatar(sender, isSelf);
            Control bubble = BuildBubblePanel(sender, content, at, isSelf, bubbleWidth);
            Control spacer = BuildSpacer();

            if (isSelf)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                row.Controls.Add(spacer, 0, 0);
                row.Controls.Add(bubble, 1, 0);
                row.Controls.Add(avatar, 2, 0);
            }
            else
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

                row.Controls.Add(avatar, 0, 0);
                row.Controls.Add(bubble, 1, 0);
                row.Controls.Add(spacer, 2, 0);
            }

            if (scrollImmediately)
            {
                flowMessages.SuspendLayout();
                flowMessages.Controls.Add(row);
                flowMessages.ResumeLayout();
                flowMessages.ScrollControlIntoView(row);
            }
            else
            {
                flowMessages.Controls.Add(row);
            }

            return row;
        }

        private Control BuildSpacer()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                AutoSize = false
            };
        }

        private PictureBox BuildAvatar(string user, bool isSelf)
        {
            return new PictureBox
            {
                Size = new Size(AvatarSize, AvatarSize),
                Image = AvatarHelper.GetAvatar(user, AvatarSize),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = isSelf
                    ? new Padding(6, 4, 12, 0)
                    : new Padding(12, 4, 6, 0)
            };
        }

        private Control BuildBubblePanel(string sender, MessageContent content, DateTime at, bool isSelf, int maxWidth)
        {
            var bubble = new TableLayoutPanel
            {
                Tag = BubbleControlTag,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximumSize = new Size(maxWidth, 0),
                BackColor = isSelf ? Color.FromArgb(5, 150, 105) : Color.White,
                Padding = new Padding(18, 14, 18, 12),
                Margin = isSelf
                    ? new Padding(0, 0, 4, 0)
                    : new Padding(8, 0, 0, 0),
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            bubble.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            int row = 0;

            if (!isSelf)
            {
                bubble.Controls.Add(new Label
                {
                    Text = sender,
                    AutoSize = true,
                    Font = new Font("Segoe UI Semibold", 9.5F),
                    ForeColor = Color.FromArgb(51, 65, 85),
                    Margin = new Padding(0, 0, 0, 6),
                    BackColor = Color.Transparent
                }, 0, row++);
            }

            if (content != null && content.IsImage && !string.IsNullOrWhiteSpace(content.Base64Data))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(content.Base64Data);
                    using (var ms = new MemoryStream(bytes))
                    {
                        var img = Image.FromStream(ms);
                        var cloneImg = new Bitmap(img);

                        int imgMaxW = Math.Min(360, maxWidth - 40);
                        int imgMaxH = 260;
                        int w = cloneImg.Width;
                        int h = cloneImg.Height;
                        if (w > imgMaxW || h > imgMaxH)
                        {
                            double r = Math.Min((double)imgMaxW / w, (double)imgMaxH / h);
                            w = Math.Max(80, (int)(w * r));
                            h = Math.Max(60, (int)(h * r));
                        }

                        var pic = new PictureBox
                        {
                            Image = cloneImg,
                            Size = new Size(w, h),
                            SizeMode = PictureBoxSizeMode.Zoom,
                            Cursor = Cursors.Hand,
                            Margin = new Padding(0, 0, 0, 8),
                            BackColor = Color.Transparent
                        };
                        pic.Click += (s, e) => ShowFullImagePreview(cloneImg, content.FileName);
                        bubble.Controls.Add(pic, 0, row++);
                    }
                }
                catch
                {
                    bubble.Controls.Add(new Label
                    {
                        Text = $"[Hình ảnh: {content.FileName}]",
                        AutoSize = true,
                        Font = new Font("Segoe UI", 11F, FontStyle.Italic),
                        ForeColor = isSelf ? Color.White : Color.FromArgb(30, 41, 59),
                        Margin = new Padding(0, 0, 0, 8),
                        BackColor = Color.Transparent
                    }, 0, row++);
                }
            }
            else
            {
                bubble.Controls.Add(new Label
                {
                    Text = content?.Text ?? string.Empty,
                    AutoSize = true,
                    MaximumSize = new Size(maxWidth - 40, 0),
                    Font = new Font("Segoe UI", 11F),
                    ForeColor = isSelf ? Color.White : Color.FromArgb(30, 41, 59),
                    Margin = new Padding(0, 0, 0, 8),
                    BackColor = Color.Transparent
                }, 0, row++);
            }

            bubble.Controls.Add(new Label
            {
                Text = at.ToString("HH:mm"),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                ForeColor = isSelf ? Color.FromArgb(209, 250, 229) : Color.FromArgb(148, 163, 184),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            }, 0, row);

            ApplyRoundedCorners(bubble, 18);
            return bubble;
        }

        private void ShowFullImagePreview(Image img, string title)
        {
            var previewForm = new Form
            {
                Text = string.IsNullOrWhiteSpace(title) ? "Xem hình ảnh" : title,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(800, 600),
                BackColor = Color.FromArgb(15, 23, 42)
            };

            var pb = new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = img,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };

            previewForm.Controls.Add(pb);
            previewForm.ShowDialog(this);
        }

        // =========================================================
        // BO GÓC
        // =========================================================
        private void ApplyRoundedCorners(Control control, int radius)
        {
            void Update(object s, EventArgs e)
            {
                if (control.Width <= 0 || control.Height <= 0) return;

                using (var path = CreateRoundedRectanglePath(
                           new Rectangle(0, 0, control.Width, control.Height), radius))
                {
                    var region = new Region(path);
                    var old = control.Region;
                    control.Region = region;
                    old?.Dispose();
                }
            }

            control.Resize += Update;
            Update(control, EventArgs.Empty);
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void UpdateBubbleWidth(Control control, int width)
        {
            if (control == null) return;

            if (control.Tag as string == BubbleControlTag)
            {
                control.MaximumSize = new Size(width, 0);
            }

            foreach (Control c in control.Controls)
                UpdateBubbleWidth(c, width);
        }

        private int CalculateRowWidth()
        {
            int padding = flowMessages.Padding.Left + flowMessages.Padding.Right;
            return Math.Max(360, flowMessages.ClientSize.Width - padding);
        }

        private int CalculateBubbleWidth(int rowWidth)
        {
            int reserved = AvatarSize + 80; // avatar + margin
            return Math.Max(280, rowWidth - reserved);
        }

        private static bool EqualsUser(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        private void MessageBox_Paint(object sender, PaintEventArgs e)
        {
            var r = messageBox.ClientRectangle;
            r.Width--;
            r.Height--;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (var path = CreateRoundedRectanglePath(r, 18))
            using (var brush = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.FromArgb(226, 232, 240)))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _client.OnPacketReceived -= HandlePacket;
            base.OnFormClosed(e);
        }
    }
}
