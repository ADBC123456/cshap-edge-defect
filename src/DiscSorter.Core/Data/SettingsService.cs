using Microsoft.Data.Sqlite;

namespace DiscSorter.Core.Data
{
    /// <summary>程序设置，存在数据库 AppSettings 表里（键 → 值）。有哪些键见 SettingKeys。</summary>
    public class SettingsService
    {
        private readonly Database database;

        public SettingsService(Database database)
        {
            this.database = database;
        }

        /// <summary>读取一个设置，没有保存过时返回 defaultValue。</summary>
        public string Get(string key, string defaultValue)
        {
            using (SqliteConnection connection = database.OpenConnection())
            {
                SqliteCommand command = Database.CreateCommand(connection, null, "SELECT Value FROM AppSettings WHERE Key = $key");
                Database.AddParameter(command, "$key", key);
                object value = command.ExecuteScalar();
                if (value == null)
                {
                    return defaultValue;
                }
                return (string)value;
            }
        }

        /// <summary>读取一个整数设置，没有保存过或不是整数时返回 defaultValue。</summary>
        public int GetInt(string key, int defaultValue)
        {
            string text = Get(key, null);
            int value;
            if (text != null && int.TryParse(text, out value))
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>保存一个设置（已有则覆盖）。</summary>
        public void Set(string key, string value)
        {
            using (SqliteConnection connection = database.OpenConnection())
            {
                SqliteCommand command = Database.CreateCommand(connection, null,
                    "INSERT INTO AppSettings(Key, Value) VALUES($key, $value) " +
                    "ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value");
                Database.AddParameter(command, "$key", key);
                Database.AddParameter(command, "$value", value);
                command.ExecuteNonQuery();
            }
        }
    }
}
