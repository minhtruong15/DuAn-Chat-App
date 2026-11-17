using System;
using System.Drawing;
using System.Windows.Forms;
using Shared;

namespace ClientApp
{
    public partial class RegisterForm : Form
    {
        private readonly ChatClient _client = new ChatClient();
        private bool _isNavigating = false;

        public RegisterForm()
        {
            InitializeComponent();
            this.AcceptButton = btnRegister;

            _client.OnPacketReceived += HandlePacket;
            this.FormClosing += RegisterForm_FormClosing;

            NotificationHelper.Initialize();
        }

        private async void btnRegister_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtUser.Text) || string.IsNullOrWhiteSpace(txtPass.Text))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ tài khoản và mật khẩu.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnRegister.Enabled = false;
            lblStatus.ForeColor = Color.FromArgb(99, 102, 241);
            lblStatus.Text = "Đang gửi yêu cầu đăng ký...";

            if (!await _client.EnsureConnectedAsync())
            {
                lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                lblStatus.Text = "Không thể kết nối server!";
                btnRegister.Enabled = true;
                NotificationHelper.ShowErrorNotification("Lỗi kết nối",
                    "Không thể kết nối đến server. Vui lòng kiểm tra ServerApp đã chạy chưa.");
                return;
            }

            var packet = new Packet(Command.AuthRegister, new
            {
                username = txtUser.Text.Trim(),
                password = txtPass.Text
            });
            await _client.SendAsync(packet);
        }

        private void HandlePacket(Packet p)
        {
            if (IsDisposed || !IsHandleCreated) return;

            this.Invoke((MethodInvoker)(() =>
            {
                switch (p.Cmd)
                {
                    case Command.AuthOk:
                        NotificationHelper.ShowSuccessNotification("Đăng ký thành công", "Bạn có thể đăng nhập ngay bây giờ!");
                        MessageBox.Show("Đăng ký tài khoản thành công!", "Thành công",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);

                        NavigateToLogin();
                        break;

                    case Command.AuthFail:
                        dynamic data = p.Data;
                        string reason = data?.reason ?? "Tên người dùng đã tồn tại!";
                        lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                        lblStatus.Text = reason;
                        btnRegister.Enabled = true;

                        NotificationHelper.ShowErrorNotification("Đăng ký thất bại", reason);
                        break;
                }
            }));
        }

        private void linkBack_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            NavigateToLogin();
        }

        private void NavigateToLogin()
        {
            _client.OnPacketReceived -= HandlePacket;
            _client.Close();

            _isNavigating = true;
            new LoginForm().Show();
            this.Close();
        }

        private void chkShowPassword_CheckedChanged(object sender, EventArgs e)
        {
            txtPass.PasswordChar = chkShowPassword.Checked ? '\0' : '●';
        }

        private void RegisterForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_isNavigating)
            {
                _client.OnPacketReceived -= HandlePacket;
                _client.Close();
                Application.Exit();
            }
        }
    }
}
