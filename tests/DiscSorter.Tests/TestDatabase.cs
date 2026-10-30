using System;
using System.IO;
using DiscSorter.Core.Data;
using Microsoft.Data.Sqlite;

namespace DiscSorter.Tests
{
    /// <summary>每个测试用一个临时数据库文件，测试结束后删除。</summary>
    public class TestDatabase : IDisposable
    {
        public TestDatabase()
        {
            Folder = Path.Combine(Path.GetTempPath(), "DiscSorterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            Database = new Database(Path.Combine(Folder, "discsorter.db"));
        }

        public string Folder { get; private set; }

        public Database Database { get; private set; }

        /// <summary>建库并返回仓储服务。</summary>
        public WarehouseService CreateWarehouse()
        {
            new DatabaseInitializer(Database).Initialize();
            return new WarehouseService(Database);
        }

        /// <summary>直接执行一段 SQL（用来准备测试数据）。</summary>
        public void Execute(string sql)
        {
            using (SqliteConnection connection = Database.OpenConnection())
            {
                SqliteCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        public object Scalar(string sql)
        {
            using (SqliteConnection connection = Database.OpenConnection())
            {
                SqliteCommand command = connection.CreateCommand();
                command.CommandText = sql;
                return command.ExecuteScalar();
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Folder, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
