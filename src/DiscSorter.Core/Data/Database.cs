using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DiscSorter.Core.Data
{
    /// <summary>
    /// SQLite 数据库文件，以及几个写 SQL 时常用的小工具。
    /// 用法：每次操作都 using 一个新连接，用完自动关闭，不长期占用数据库文件。
    /// </summary>
    public class Database
    {
        /// <summary>数据库里时间的存储格式，例如 2026-10-27 19:08:15.123（本地时间）。</summary>
        public const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

        public Database(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; private set; }

        /// <summary>打开一个新连接。调用方用 using 包起来，用完自动关闭。</summary>
        public SqliteConnection OpenConnection()
        {
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder();
            builder.DataSource = FilePath;
            builder.Mode = SqliteOpenMode.ReadWriteCreate;
            builder.ForeignKeys = true;   // 启用外键约束
            builder.Pooling = false;      // 不使用连接池，关闭连接后文件立即释放

            SqliteConnection connection = new SqliteConnection(builder.ToString());
            connection.Open();
            return connection;
        }

        /// <summary>创建一条 SQL 命令。transaction 可以为 null（不在事务中执行）。</summary>
        public static SqliteCommand CreateCommand(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }

        /// <summary>添加一个 SQL 参数。C# 的 null 要转成数据库的 DBNull。</summary>
        public static void AddParameter(SqliteCommand command, string name, object value)
        {
            if (value == null)
            {
                value = DBNull.Value;
            }
            command.Parameters.AddWithValue(name, value);
        }

        public static string FormatTime(DateTime time)
        {
            return time.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }

        public static DateTime ParseTime(string text)
        {
            DateTime time;
            if (DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                return time;
            }
            // 兼容早期版本写入的 ISO 格式时间（例如 2026-10-27T11:08:15.1230000Z）
            return DateTime.Parse(text, CultureInfo.InvariantCulture);
        }
    }
}
