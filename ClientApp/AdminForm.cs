using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using MySql.Data.MySqlClient;
using Shared;

namespace ClientApp
{
    public partial class AdminForm : Form
    {
        private readonly string connStr = DatabaseManager.ConnectionString;
        private readonly ChatClient _client;
        private readonly string _username;
        private bool _isLoggingOut = false;

        public AdminForm(ChatClient client, string username)
        {
            InitializeComponent();
            _client = client;
            _username = username;

            StyleGrid(dgvUsers);

            this.FormClosing += AdminForm_FormClosing;
            txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    btnSearch.PerformClick();
                }
            };

            if (_client != null)
            {
                _client.OnPacketReceived -= HandlePacket;
                _client.OnPacketReceived += HandlePacket;
            }

            LoadUsers();
        }

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
                case Command.AdminUsersResult:
                    {
                        dynamic d = p.Data;
                        DataTable dt = new DataTable();
                        dt.Columns.Add("ID", typeof(int));
                        dt.Columns.Add("Tài khoản", typeof(string));
                        dt.Columns.Add("Mật khẩu", typeof(string));
                        dt.Columns.Add("Vai trò", typeof(string));
                        dt.Columns.Add("Trạng thái", typeof(string));

                        if (d?.users != null)
                        {
                            foreach (var u in d.users)
                            {
                                int id = (int)u.Id;
                                string name = (string)u.Username;
                                string pass = (string)u.Password;
                                string role = (string)u.Role;
                                bool online = (bool)u.IsOnline;

                                dt.Rows.Add(id, name, pass, role, online ? "🟢 Online" : "⚪ Offline");
                            }
                        }

                        dgvUsers.DataSource = dt;
                        lblUserInfo.Text = $"👤 Tổng tài khoản: {dgvUsers.Rows.Count}";
                        break;
                    }

                case Command.AdminActionResult:
                    {
                        dynamic d = p.Data;
                        bool ok = (bool)(d?.success ?? false);
                        string msg = (string)d?.message ?? "";
                        LoadUsers(txtSearch.Text.Trim());
                        MessageBox.Show((ok ? "✅ " : "❌ ") + msg, "Thông báo",
                            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                        break;
                    }

                case Command.UpdateFriendList:
                    {
                        // Tự động cập nhật trạng thái Online/Offline khi có user đăng nhập/thoát
                        LoadUsers(txtSearch.Text.Trim());
                        break;
                    }
            }
        }

        // === STYLE GRID ===
        private void StyleGrid(DataGridView grid)
        {
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Color.FromArgb(250, 252, 255);
            grid.GridColor = Color.FromArgb(210, 210, 210);
            grid.EnableHeadersVisualStyles = false;

            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.RowHeadersVisible = false;

            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;

            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 237, 240);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            grid.AdvancedColumnHeadersBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
            grid.AdvancedColumnHeadersBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedColumnHeadersBorderStyle.Left = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedColumnHeadersBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.None;

            grid.ColumnHeadersHeight = 42;

            grid.DefaultCellStyle.BackColor = Color.FromArgb(250, 252, 255);
            grid.DefaultCellStyle.ForeColor = Color.Black;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 10.5F);
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 230, 240);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 255);
        }

        // === LOAD USERS ===
        private async void LoadUsers(string keyword = "")
        {
            if (_client != null && _client.IsConnected)
            {
                await _client.SendAsync(new Packet(Command.AdminGetUsers, new { keyword = keyword ?? "" }));
                return;
            }

            try
            {
                using (MySqlConnection conn = new MySqlConnection(connStr))
                {
                    conn.Open();
                    string sql = "SELECT id AS 'ID', username AS 'Tài khoản', password AS 'Mật khẩu', role AS 'Vai trò' FROM users";
                    if (!string.IsNullOrEmpty(keyword))
                        sql += " WHERE username LIKE @kw";
                    sql += " ORDER BY id ASC";

                    using (MySqlCommand cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@kw", "%" + keyword + "%");
                        using (MySqlDataAdapter adp = new MySqlDataAdapter(cmd))
                        {
                            DataTable dt = new DataTable();
                            adp.Fill(dt);
                            dgvUsers.DataSource = dt;
                        }
                    }
                }

                lblUserInfo.Text = $"👤 Tổng tài khoản: {dgvUsers.Rows.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("❌ Lỗi tải danh sách:\n" + ex.Message);
            }
        }

        // === SEARCH USER ===
        private void btnSearch_Click(object sender, EventArgs e)
        {
            LoadUsers(txtSearch.Text.Trim());
        }

        // === REFRESH ===
        private void btnRefresh_Click(object sender, EventArgs e)
        {
            txtSearch.Clear();
            LoadUsers();
        }

        // === ADD USER ===
        private async void btnAdd_Click(object sender, EventArgs e)
        {
            string username = Prompt.ShowDialog("Nhập tên tài khoản mới:", "Thêm user");
            if (string.IsNullOrWhiteSpace(username)) return;

            string password = Prompt.ShowDialog("Nhập mật khẩu:", "Thêm user");
            if (string.IsNullOrWhiteSpace(password)) return;

            string role = Prompt.ShowDialog("Nhập vai trò (admin/user):", "Thêm user");
            if (string.IsNullOrWhiteSpace(role)) role = "user";

            if (_client != null && _client.IsConnected)
            {
                await _client.SendAsync(new Packet(Command.AdminAddUser, new
                {
                    username = username.Trim(),
                    password = password,
                    role = role.Trim().ToLower()
                }));
                return;
            }

            try
            {
                using (MySqlConnection conn = new MySqlConnection(connStr))
                {
                    conn.Open();
                    using (MySqlCommand cmd = new MySqlCommand("INSERT INTO users (username, password, role) VALUES (@u, @p, @r)", conn))
                    {
                        cmd.Parameters.AddWithValue("@u", username.Trim());
                        cmd.Parameters.AddWithValue("@p", password);
                        cmd.Parameters.AddWithValue("@r", role.ToLower());
                        cmd.ExecuteNonQuery();
                    }
                }

                LoadUsers();
                MessageBox.Show("✅ Thêm user thành công!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("❌ Lỗi thêm user:\n" + ex.Message);
            }
        }

        // === EDIT USER ===
        private async void btnEdit_Click(object sender, EventArgs e)
        {
            if (dgvUsers.SelectedRows.Count == 0)
            {
                MessageBox.Show("⚠️ Hãy chọn 1 user để sửa!");
                return;
            }

            string username = dgvUsers.SelectedRows[0].Cells["Tài khoản"].Value.ToString();
            string newPass = Prompt.ShowDialog("Nhập mật khẩu mới (để trống nếu giữ nguyên):", "Sửa user");
            string newRole = Prompt.ShowDialog("Nhập vai trò mới (admin/user, để trống nếu giữ nguyên):", "Sửa user");

            if (string.IsNullOrWhiteSpace(newPass) && string.IsNullOrWhiteSpace(newRole))
                return;

            if (_client != null && _client.IsConnected)
            {
                await _client.SendAsync(new Packet(Command.AdminUpdateUser, new
                {
                    username = username,
                    password = newPass,
                    role = newRole
                }));
                return;
            }

            try
            {
                using (MySqlConnection conn = new MySqlConnection(connStr))
                {
                    conn.Open();

                    string sql = "UPDATE users SET ";
                    if (!string.IsNullOrEmpty(newPass)) sql += "password=@p, ";
                    if (!string.IsNullOrEmpty(newRole)) sql += "role=@r, ";
                    sql = sql.TrimEnd(',', ' ') + " WHERE username=@u";

                    using (MySqlCommand cmd = new MySqlCommand(sql, conn))
                    {
                        if (!string.IsNullOrEmpty(newPass)) cmd.Parameters.AddWithValue("@p", newPass);
                        if (!string.IsNullOrEmpty(newRole)) cmd.Parameters.AddWithValue("@r", newRole.ToLower());
                        cmd.Parameters.AddWithValue("@u", username);
                        cmd.ExecuteNonQuery();
                    }
                }

                LoadUsers();
                MessageBox.Show("✅ Sửa thông tin user thành công!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("❌ Lỗi sửa user:\n" + ex.Message);
            }
        }

        // === DELETE USER ===
        private async void btnDelete_Click(object sender, EventArgs e)
        {
            if (dgvUsers.SelectedRows.Count == 0)
            {
                MessageBox.Show("⚠️ Hãy chọn ít nhất một user để xóa!");
                return;
            }

            var confirm = MessageBox.Show("Bạn có chắc muốn xóa user này?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            if (_client != null && _client.IsConnected)
            {
                foreach (DataGridViewRow row in dgvUsers.SelectedRows)
                {
                    string username = row.Cells["Tài khoản"].Value.ToString();
                    await _client.SendAsync(new Packet(Command.AdminDeleteUser, new { username = username }));
                }
                return;
            }

            try
            {
                using (MySqlConnection conn = new MySqlConnection(connStr))
                {
                    conn.Open();

                    foreach (DataGridViewRow row in dgvUsers.SelectedRows)
                    {
                        string username = row.Cells["Tài khoản"].Value.ToString();
                        if (username.ToLower() == "admin" || username == _username)
                        {
                            MessageBox.Show($"⛔ Không thể xóa tài khoản '{username}'!");
                            continue;
                        }

                        using (MySqlCommand cmd = new MySqlCommand("DELETE FROM users WHERE username=@u", conn))
                        {
                            cmd.Parameters.AddWithValue("@u", username);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                LoadUsers();
                MessageBox.Show("✅ Đã xóa user thành công!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("❌ Lỗi khi xóa user:\n" + ex.Message);
            }
        }

        // === LOGOUT ===
        private void btnLogout_Click(object sender, EventArgs e)
        {
            _isLoggingOut = true;
            if (_client != null)
            {
                _client.OnPacketReceived -= HandlePacket;
                _client.Close();
            }
            new LoginForm().Show();
            this.Close();
        }

        private void AdminForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_client != null)
            {
                _client.OnPacketReceived -= HandlePacket;
                _client.Close();
            }

            if (!_isLoggingOut)
            {
                Application.Exit();
            }
        }

        private void dgvUsers_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
        }
    }

    // --- Class hỗ trợ nhập input nhỏ ---
    public static class Prompt
    {
        public static string ShowDialog(string text, string caption)
        {
            Form prompt = new Form()
            {
                Width = 400,
                Height = 180,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = caption,
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(18, 22, 27),
                ForeColor = Color.White,
                MaximizeBox = false,
                MinimizeBox = false
            };

            Label lblText = new Label() { Left = 20, Top = 20, Text = text, Width = 340 };
            TextBox txtInput = new TextBox() { Left = 20, Top = 50, Width = 340, ForeColor = Color.White, BackColor = Color.FromArgb(30, 35, 40) };
            Button confirmation = new Button() { Text = "OK", Left = 140, Width = 100, Top = 90, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(0, 200, 150), FlatStyle = FlatStyle.Flat };

            confirmation.FlatAppearance.BorderSize = 0;
            prompt.Controls.Add(lblText);
            prompt.Controls.Add(txtInput);
            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;

            return prompt.ShowDialog() == DialogResult.OK ? txtInput.Text : "";
        }
    }
}
