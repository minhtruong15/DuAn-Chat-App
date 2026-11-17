using System.Configuration;
using System.Data;
using MySql.Data.MySqlClient;

namespace ClientApp
{
    public static class DatabaseManager
    {
        public static readonly string ConnectionString =
            ConfigurationManager.ConnectionStrings["ChatDb"]?.ConnectionString
            ?? "Server=127.0.0.1;Port=3306;Database=chatdb;Uid=root;Pwd=1582005;Charset=utf8mb4;";

        public static DataTable ExecuteQuery(string sql)
        {
            using (var conn = new MySqlConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    var adp = new MySqlDataAdapter(cmd);
                    var dt = new DataTable();
                    adp.Fill(dt);
                    return dt;
                }
            }
        }

        public static int ExecuteNonQuery(string sql)
        {
            using (var conn = new MySqlConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    return cmd.ExecuteNonQuery();
                }
            }
        }
    }
}
