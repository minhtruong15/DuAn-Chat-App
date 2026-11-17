using System;
using System.Collections.Generic;
using System.Configuration;
using MySql.Data.MySqlClient;
using Shared;

namespace ServerApp
{
    public static class Database
    {
        public static readonly string ConnectionString =
            ConfigurationManager.ConnectionStrings["ChatDb"]?.ConnectionString
            ?? "Server=127.0.0.1;Port=3306;Database=chatdb;Uid=root;Pwd=1582005;Charset=utf8mb4;";

        // ---- TỰ ĐỘNG KHỞI TẠO DATABASE & BẢNG NẾU CHƯA CÓ ----
        public static bool EnsureDatabaseInitialized(out string errorMessage)
        {
            errorMessage = null;
            try
            {
                var builder = new MySqlConnectionStringBuilder(ConnectionString);
                string dbName = string.IsNullOrWhiteSpace(builder.Database) ? "chatdb" : builder.Database;
                builder.Database = "";

                using (var serverConn = new MySqlConnection(builder.ConnectionString))
                {
                    serverConn.Open();
                    using (var createDbCmd = new MySqlCommand(
                        $"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                        serverConn))
                    {
                        createDbCmd.ExecuteNonQuery();
                    }
                }

                using (var conn = new MySqlConnection(ConnectionString))
                {
                    conn.Open();

                    string createUsersSql = @"
                        CREATE TABLE IF NOT EXISTS users (
                            id INT AUTO_INCREMENT PRIMARY KEY,
                            username VARCHAR(50) UNIQUE NOT NULL,
                            password VARCHAR(100) NOT NULL,
                            role VARCHAR(10) DEFAULT 'user'
                        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";

                    string createMessagesSql = @"
                        CREATE TABLE IF NOT EXISTS messages (
                            id INT AUTO_INCREMENT PRIMARY KEY,
                            sender VARCHAR(50),
                            receiver VARCHAR(50),
                            text MEDIUMTEXT,
                            time DATETIME DEFAULT CURRENT_TIMESTAMP
                        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";

                    using (var cmd1 = new MySqlCommand(createUsersSql, conn))
                        cmd1.ExecuteNonQuery();

                    using (var cmd2 = new MySqlCommand(createMessagesSql, conn))
                        cmd2.ExecuteNonQuery();

                    // Đảm bảo luôn có tài khoản admin mặc định (admin / 123456) nếu chưa có admin nào
                    using (var checkAdmin = new MySqlCommand("SELECT COUNT(*) FROM users WHERE username='admin'", conn))
                    {
                        long count = Convert.ToInt64(checkAdmin.ExecuteScalar());
                        if (count == 0)
                        {
                            using (var insertAdmin = new MySqlCommand(
                                "INSERT INTO users(username, password, role) VALUES('admin', '123456', 'admin')", conn))
                            {
                                insertAdmin.ExecuteNonQuery();
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        // ---- KIỂM TRA KẾT NỐI ----
        public static bool TestConnection()
        {
            try
            {
                using (var conn = new MySqlConnection(ConnectionString))
                {
                    conn.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // ---- ĐĂNG NHẬP ----
        public static Tuple<string, string> Authenticate(string username, string password)
        {
            using (var conn = new MySqlConnection(ConnectionString))
            {
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT username, role FROM users WHERE username=@u AND password=@p", conn);

                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@p", password);

                using (var rd = cmd.ExecuteReader())
                {
                    if (rd.Read())
                        return Tuple.Create(rd.GetString("username"), rd.GetString("role"));
                }
            }
            return null;
        }

        // ---- ĐĂNG KÝ ----
        public static bool Register(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return false;

            try
            {
                using (var conn = new MySqlConnection(ConnectionString))
                {
                    conn.Open();
                    var cmd = new MySqlCommand(
                        "INSERT INTO users(username,password,role) VALUES(@u,@p,'user')", conn);

                    cmd.Parameters.AddWithValue("@u", username.Trim());
                    cmd.Parameters.AddWithValue("@p", password);

                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        // ---- LẤY DANH SÁCH USER (ĐỂ SHOW Ở ROOMFORM) ----
        public static List<string> GetAllUsers()
        {
            var users = new List<string>();

            using (var conn = new MySqlConnection(ConnectionString))
            {
                conn.Open();
                var cmd = new MySqlCommand("SELECT username FROM users ORDER BY username ASC", conn);

                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        users.Add(rd.GetString("username"));
                    }
                }
            }

            return users;
        }

        // ---- LẤY USER TRỪ CHÍNH MÌNH ----
        public static List<string> GetAllUsersExcept(string username)
        {
            var users = new List<string>();

            using (var conn = new MySqlConnection(ConnectionString))
            {
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT username FROM users WHERE username<>@me ORDER BY username ASC", conn);

                cmd.Parameters.AddWithValue("@me", username);

                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        users.Add(rd.GetString("username"));
                    }
                }
            }

            return users;
        }

        // ---- LẤY DANH SÁCH CHI TIẾT USER CHO ADMIN ----
        public static List<AdminUserDto> GetAdminUsers(string keyword = "", HashSet<string> onlineSet = null)
        {
            var list = new List<AdminUserDto>();

            using (var conn = new MySqlConnection(ConnectionString))
            {
                conn.Open();
                string sql = "SELECT id, username, password, role FROM users";
                if (!string.IsNullOrWhiteSpace(keyword))
                    sql += " WHERE username LIKE @kw";
                sql += " ORDER BY id ASC";

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    if (!string.IsNullOrWhiteSpace(keyword))
                        cmd.Parameters.AddWithValue("@kw", "%" + keyword.Trim() + "%");

                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            string u = rd.GetString("username");
                            list.Add(new AdminUserDto
                            {
                                Id = rd.GetInt32("id"),
                                Username = u,
                                Password = rd.GetString("password"),
                                Role = rd.GetString("role"),
                                IsOnline = onlineSet != null && onlineSet.Contains(u)
                            });
                        }
                    }
                }
            }

            return list;
        }

        // ---- ADMIN: THÊM USER ----
        public static bool AddUser(string username, string password, string role, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                error = "Tài khoản và mật khẩu không được để trống.";
                return false;
            }

            string normalizedRole = string.Equals(role?.Trim(), "admin", StringComparison.OrdinalIgnoreCase)
                ? "admin"
                : "user";

            try
            {
                using (var conn = new MySqlConnection(ConnectionString))
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand(
                        "INSERT INTO users (username, password, role) VALUES (@u, @p, @r)", conn))
                    {
                        cmd.Parameters.AddWithValue("@u", username.Trim());
                        cmd.Parameters.AddWithValue("@p", password);
                        cmd.Parameters.AddWithValue("@r", normalizedRole);
                        cmd.ExecuteNonQuery();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ---- ADMIN: SỬA USER ----
        public static bool UpdateUser(string username, string newPass, string newRole, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(username))
            {
                error = "Tài khoản không hợp lệ.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(newPass) && string.IsNullOrWhiteSpace(newRole))
            {
                error = "Không có thông tin nào thay đổi.";
                return false;
            }

            try
            {
                using (var conn = new MySqlConnection(ConnectionString))
                {
                    conn.Open();
                    var updates = new List<string>();
                    if (!string.IsNullOrWhiteSpace(newPass)) updates.Add("password=@p");
                    if (!string.IsNullOrWhiteSpace(newRole)) updates.Add("role=@r");

                    string sql = $"UPDATE users SET {string.Join(", ", updates)} WHERE username=@u";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        if (!string.IsNullOrWhiteSpace(newPass))
                            cmd.Parameters.AddWithValue("@p", newPass);
                        if (!string.IsNullOrWhiteSpace(newRole))
                        {
                            string normalizedRole = string.Equals(newRole.Trim(), "admin", StringComparison.OrdinalIgnoreCase)
                                ? "admin"
                                : "user";
                            cmd.Parameters.AddWithValue("@r", normalizedRole);
                        }
                        cmd.Parameters.AddWithValue("@u", username);
                        cmd.ExecuteNonQuery();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ---- ADMIN: XÓA USER ----
        public static bool DeleteUser(string username, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(username))
            {
                error = "Tài khoản không hợp lệ.";
                return false;
            }

            if (string.Equals(username.Trim(), "admin", StringComparison.OrdinalIgnoreCase))
            {
                error = "Không thể xóa tài khoản quản trị mặc định 'admin'.";
                return false;
            }

            try
            {
                using (var conn = new MySqlConnection(ConnectionString))
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand("DELETE FROM users WHERE username=@u", conn))
                    {
                        cmd.Parameters.AddWithValue("@u", username);
                        cmd.ExecuteNonQuery();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ---- LƯU TIN NHẮN RIÊNG ----
        public static void SavePrivateMessage(string sender, string receiver, string payload)
        {
            using (var conn = new MySqlConnection(ConnectionString))
            {
                conn.Open();
                var cmd = new MySqlCommand(
                    "INSERT INTO messages(sender, receiver, text) VALUES(@s,@r,@t)", conn);

                cmd.Parameters.AddWithValue("@s", sender);
                cmd.Parameters.AddWithValue("@r", receiver);
                cmd.Parameters.AddWithValue("@t", payload);

                cmd.ExecuteNonQuery();
            }
        }

        // ---- LẤY LỊCH SỬ CHAT ----
        public static List<object> GetPrivateHistory(string user1, string user2)
        {
            var messages = new List<object>();

            using (var conn = new MySqlConnection(ConnectionString))
            {
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT sender, receiver, text, time 
                    FROM messages
                    WHERE (sender=@u1 AND receiver=@u2)
                       OR (sender=@u2 AND receiver=@u1)
                    ORDER BY time ASC, id ASC
                ", conn);

                cmd.Parameters.AddWithValue("@u1", user1);
                cmd.Parameters.AddWithValue("@u2", user2);

                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var payload = rd.GetString("text");
                        messages.Add(new
                        {
                            sender = rd.GetString("sender"),
                            receiver = rd.GetString("receiver"),
                            payload = payload,
                            time = rd.GetDateTime("time").ToString("HH:mm:ss")
                        });
                    }
                }
            }

            return messages;
        }
    }
}
