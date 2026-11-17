using Shared;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClientApp
{
    public partial class LoginForm : Form
    {
        private readonly ChatClient _client = new ChatClient();
        private bool _isNavigating = false;

        public LoginForm()
        {
            InitializeComponent();
            this.AcceptButton = btnLogin;

            _client.OnPacketReceived += HandlePacket;
            _client.OnDisconnected += HandleDisconnected;
            this.FormClosing += LoginForm_FormClosing;

            NotificationHelper.Initialize();
        }

        private void HandleDisconnected()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                this.Invoke((MethodInvoker)(() =>
                {
                    lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                    lblStatus.Text = "Mất kết nối server!";
                    btnLogin.Enabled = true;
                }));
            }
            catch { }
        }

        private async void btnLogin_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtUser.Text) || string.IsNullOrWhiteSpace(txtPass.Text))
            {
                MessageBox.Show("Vui lòng nhập tài khoản và mật khẩu.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnLogin.Enabled = false;
            lblStatus.ForeColor = Color.FromArgb(99, 102, 241);
            lblStatus.Text = "Đang kết nối server...";

            if (!await _client.EnsureConnectedAsync())
            {
                lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                lblStatus.Text = "Không thể kết nối server!";
                btnLogin.Enabled = true;

                NotificationHelper.ShowErrorNotification("Lỗi kết nối",
                    "Không thể kết nối đến server. Hãy kiểm tra ServerApp đã khởi động chưa.");

                return;
            }

            var packet = new Packet(Command.AuthLogin, new
            {
                username = txtUser.Text.Trim(),
                password = txtPass.Text
            });

            await _client.SendAsync(packet);
        }

        private void HandlePacket(Packet p)
        {
            if (IsDisposed || !IsHandleCreated) return;

            this.Invoke((MethodInvoker)(async () =>
            {
                switch (p.Cmd)
                {
                    case Command.AuthOk:
                        dynamic d = p.Data;
                        string username = d.username;
                        string role = d.role;

                        lblStatus.ForeColor = Color.FromArgb(16, 185, 129);
                        lblStatus.Text = "Đăng nhập thành công!";
                        NotificationHelper.ShowSuccessNotification("Đăng nhập thành công", $"Chào mừng {username}!");

                        // Gỡ event của LoginForm trước khi chuyển sang Form chính
                        _client.OnPacketReceived -= HandlePacket;
                        _client.OnDisconnected -= HandleDisconnected;

                        await Task.Delay(200);

                        _isNavigating = true;
                        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
                            new AdminForm(_client, username).Show();
                        else
                            new RoomForm(_client, username).Show();

                        this.Close();
                        break;

                    case Command.AuthFail:
                        dynamic failData = p.Data;
                        string reason = failData?.reason ?? "Sai tài khoản hoặc mật khẩu!";
                        lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                        lblStatus.Text = reason;
                        btnLogin.Enabled = true;

                        NotificationHelper.ShowErrorNotification("Đăng nhập thất bại", reason);
                        break;
                }
            }));
        }

        private void linkRegister_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            _client.OnPacketReceived -= HandlePacket;
            _client.OnDisconnected -= HandleDisconnected;
            _client.Close();

            _isNavigating = true;
            new RegisterForm().Show();
            this.Close();
        }

        private void chkShowPassword_CheckedChanged(object sender, EventArgs e)
        {
            txtPass.PasswordChar = chkShowPassword.Checked ? '\0' : '●';
        }

        private void LoginForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_isNavigating)
            {
                _client.OnPacketReceived -= HandlePacket;
                _client.OnDisconnected -= HandleDisconnected;
                _client.Close();
                Application.Exit();
            }
        }
    }
}
