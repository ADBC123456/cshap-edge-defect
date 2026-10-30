using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace DiscSorter.Core.Data
{
    /// <summary>
    /// 建库、升级、启动恢复。
    /// 原则：已有数据从不覆盖；数据缺损时让程序启动失败并提示，而不是悄悄重建。
    ///
    /// 数据库版本：
    ///   v1：类别只有 Q/H/K/NG。
    ///   v2：加入污渍 W，类别码 1Q 2H 3K 4W 5NG；默认映射 列1=Q 列2=H 列3=K 列4=W。
    /// </summary>
    public class DatabaseInitializer
    {
        public const int SchemaVersion = 2;

        // ---------- 建表 SQL ----------
        // 带类别约束的 4 张表，{0} 处填表名。
        // 新建时填正式表名；从 v1 升级时先建 "表名_v2"，复制数据后再改回正式表名。

        private const string ColumnMappingsSql = @"
            CREATE TABLE {0}(
                Column   INTEGER PRIMARY KEY CHECK(Column BETWEEN 1 AND 4),
                Category TEXT NOT NULL UNIQUE CHECK(Category IN ('Q','H','K','W','NG')))";

        private const string StorageTasksSql = @"
            CREATE TABLE {0}(
                TaskId      INTEGER PRIMARY KEY AUTOINCREMENT,
                JobId       INTEGER NULL,
                Category    TEXT NOT NULL CHECK(Category IN ('Q','H','K','W','NG')),
                SlotNo      INTEGER NOT NULL CHECK(SlotNo BETWEEN 1 AND 12),
                Status      TEXT NOT NULL CHECK(Status IN ('Reserved','Completed','Failed','Cancelled','NeedsCheck')),
                CreatedAt   TEXT NOT NULL,
                CompletedAt TEXT NULL,
                Note        TEXT NULL)";

        private const string StorageSlotsSql = @"
            CREATE TABLE {0}(
                SlotNo    INTEGER PRIMARY KEY CHECK(SlotNo BETWEEN 1 AND 12),
                Row       INTEGER NOT NULL CHECK(Row BETWEEN 1 AND 3),
                Column    INTEGER NOT NULL CHECK(Column BETWEEN 1 AND 4),
                State     TEXT NOT NULL CHECK(State IN ('Empty','Reserved','Occupied','NeedsCheck')),
                TaskId    INTEGER NULL REFERENCES StorageTasks(TaskId),
                Category  TEXT NULL CHECK(Category IS NULL OR Category IN ('Q','H','K','W','NG')),
                UpdatedAt TEXT NULL,
                UNIQUE(Row, Column),
                CHECK(SlotNo = (Column - 1) * 3 + Row),
                CHECK((State = 'Empty' AND TaskId IS NULL AND Category IS NULL) OR
                      (State <> 'Empty' AND TaskId IS NOT NULL AND Category IS NOT NULL)))";

        private const string InspectionRecordsSql = @"
            CREATE TABLE {0}(
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                JobId          INTEGER NULL,
                CapturedAt     TEXT NOT NULL,
                ImagePath      TEXT NULL,
                AlgorithmLabel TEXT NULL,
                Category       TEXT NOT NULL CHECK(Category IN ('Q','H','K','W','NG')),
                CategorySource TEXT NOT NULL CHECK(CategorySource IN ('Algorithm','FileName')),
                ExpectedLabel  TEXT NULL,
                IsCorrect      INTEGER NULL,
                TargetSlot     INTEGER NOT NULL CHECK(TargetSlot BETWEEN 0 AND 12),
                TaskId         INTEGER NULL REFERENCES StorageTasks(TaskId),
                CircleFound    INTEGER NOT NULL,
                CircleRow      REAL NULL,
                CircleColumn   REAL NULL,
                Radius         REAL NULL,
                DetectMs       REAL NULL,
                TotalMs        REAL NULL,
                Result         TEXT NULL)";

        // 其他不带类别约束的表
        private const string OtherTablesSql = @"
            CREATE TABLE __SchemaVersion(
                Id      INTEGER PRIMARY KEY CHECK(Id = 1),
                Version INTEGER NOT NULL);
            CREATE TABLE OperationLogs(
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                OccurredAt TEXT NOT NULL,
                EventType  TEXT NOT NULL,
                Message    TEXT NOT NULL,
                TaskId     INTEGER NULL);
            CREATE TABLE AppSettings(
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL);";

        // 新数据库的默认数据：版本号、默认列映射、12 个空库位
        private const string SeedSql = @"
            INSERT INTO __SchemaVersion(Id, Version) VALUES(1, 2);
            INSERT INTO ColumnMappings(Column, Category) VALUES(1,'Q'), (2,'H'), (3,'K'), (4,'W');
            INSERT INTO StorageSlots(SlotNo, Row, Column, State) VALUES
                (1,1,1,'Empty'),  (2,2,1,'Empty'),  (3,3,1,'Empty'),
                (4,1,2,'Empty'),  (5,2,2,'Empty'),  (6,3,2,'Empty'),
                (7,1,3,'Empty'),  (8,2,3,'Empty'),  (9,3,3,'Empty'),
                (10,1,4,'Empty'), (11,2,4,'Empty'), (12,3,4,'Empty');";

        private readonly Database database;

        public DatabaseInitializer(Database database)
        {
            this.database = database;
        }

        /// <summary>程序启动时调用：没有数据库就新建，是旧版本就升级，最后检查数据是否完整。</summary>
        public void Initialize()
        {
            using (SqliteConnection connection = database.OpenConnection())
            {
                int version = ReadVersion(connection);
                if (version < 0)
                {
                    throw new InvalidDataException("数据库损坏：__SchemaVersion 表缺少版本行");
                }
                if (version > SchemaVersion)
                {
                    throw new InvalidOperationException("数据库版本 " + version + " 高于本程序支持的版本 " + SchemaVersion);
                }

                if (version == 0)
                {
                    CreateNewDatabase(connection);
                }
                else if (version == 1)
                {
                    UpgradeFromVersion1(connection);
                }

                CheckRowCount(connection, "__SchemaVersion", 1);
                CheckRowCount(connection, "ColumnMappings", 4);
                CheckRowCount(connection, "StorageSlots", 12);
            }
        }

        /// <summary>
        /// 启动恢复：上次退出时还处于"执行中"的库位，一律改为"待确认"。
        /// 真机上无法确定机械臂是否已经放下工件，所以不自动释放，交给人工确认。
        /// 返回改动的库位数。
        /// </summary>
        public int Recover()
        {
            using (SqliteConnection connection = database.OpenConnection())
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                string now = Database.FormatTime(DateTime.Now);

                SqliteCommand updateSlots = Database.CreateCommand(connection, transaction,
                    "UPDATE StorageSlots SET State = 'NeedsCheck', UpdatedAt = $now WHERE State = 'Reserved'");
                Database.AddParameter(updateSlots, "$now", now);
                int count = updateSlots.ExecuteNonQuery();

                SqliteCommand updateTasks = Database.CreateCommand(connection, transaction,
                    "UPDATE StorageTasks SET Status = 'NeedsCheck', Note = '程序重启时任务仍在执行中' WHERE Status = 'Reserved'");
                updateTasks.ExecuteNonQuery();

                if (count > 0)
                {
                    AddLog(connection, transaction, "Recovery", "启动恢复：" + count + " 个执行中的库位改为待确认");
                }
                transaction.Commit();
                return count;
            }
        }

        private static void CreateNewDatabase(SqliteConnection connection)
        {
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                Execute(connection, transaction, OtherTablesSql);
                Execute(connection, transaction, string.Format(ColumnMappingsSql, "ColumnMappings"));
                Execute(connection, transaction, string.Format(StorageTasksSql, "StorageTasks"));
                Execute(connection, transaction, string.Format(StorageSlotsSql, "StorageSlots"));
                Execute(connection, transaction, string.Format(InspectionRecordsSql, "InspectionRecords"));
                Execute(connection, transaction, SeedSql);
                transaction.Commit();
            }
        }

        /// <summary>
        /// v1 → v2：放宽类别约束以加入 W。
        /// SQLite 不能直接修改 CHECK 约束，只能重建表：建新表 → 复制数据 → 删旧表 → 新表改名。数据原样保留。
        /// 重建期间要关闭外键检查（只能在事务外切换），提交前用 foreign_key_check 确认引用完整。
        /// </summary>
        private static void UpgradeFromVersion1(SqliteConnection connection)
        {
            Execute(connection, null, "PRAGMA foreign_keys = OFF");
            try
            {
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    RebuildTable(connection, transaction, "ColumnMappings", ColumnMappingsSql);
                    RebuildTable(connection, transaction, "StorageTasks", StorageTasksSql);
                    RebuildTable(connection, transaction, "StorageSlots", StorageSlotsSql);
                    RebuildTable(connection, transaction, "InspectionRecords", InspectionRecordsSql);

                    SqliteCommand check = Database.CreateCommand(connection, transaction, "PRAGMA foreign_key_check");
                    using (SqliteDataReader reader = check.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            throw new InvalidDataException("数据库升级失败：表 " + reader.GetString(0) + " 中有无效的任务引用");
                        }
                    }

                    // 如果映射还是 v1 的默认值（列 4 = NG）并且列 4 是空的，就顺带改成 v2 的默认值（列 4 = W）。
                    // 用户自己改过的映射不动。
                    if (IsVersion1DefaultMapping(connection, transaction) && IsColumnEmpty(connection, transaction, 4))
                    {
                        Execute(connection, transaction, "UPDATE ColumnMappings SET Category = 'W' WHERE Column = 4");
                        AddLog(connection, transaction, "MappingChanged", "升级为 v2 默认映射：列 4 由 NG 改为 W");
                    }

                    Execute(connection, transaction, "UPDATE __SchemaVersion SET Version = 2 WHERE Id = 1");
                    AddLog(connection, transaction, "Migration", "数据库从 v1 升级到 v2：加入污渍 W 类别");
                    transaction.Commit();
                }
            }
            finally
            {
                Execute(connection, null, "PRAGMA foreign_keys = ON");
            }
        }

        private static void RebuildTable(SqliteConnection connection, SqliteTransaction transaction, string tableName, string createSql)
        {
            string newName = tableName + "_v2";
            Execute(connection, transaction, string.Format(createSql, newName));
            Execute(connection, transaction, "INSERT INTO " + newName + " SELECT * FROM " + tableName);
            Execute(connection, transaction, "DROP TABLE " + tableName);
            Execute(connection, transaction, "ALTER TABLE " + newName + " RENAME TO " + tableName);
        }

        private static bool IsVersion1DefaultMapping(SqliteConnection connection, SqliteTransaction transaction)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "SELECT Column, Category FROM ColumnMappings ORDER BY Column");
            string text = "";
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    text = text + reader.GetInt32(0) + reader.GetString(1) + ";";
                }
            }
            return text == "1Q;2H;3K;4NG;";
        }

        private static bool IsColumnEmpty(SqliteConnection connection, SqliteTransaction transaction, int column)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "SELECT COUNT(*) FROM StorageSlots WHERE Column = $column AND State <> 'Empty'");
            Database.AddParameter(command, "$column", column);
            long busy = (long)command.ExecuteScalar();
            return busy == 0;
        }

        /// <summary>读数据库版本：没有版本表返回 0，版本表里没有数据返回 -1。</summary>
        private static int ReadVersion(SqliteConnection connection)
        {
            SqliteCommand tableExists = Database.CreateCommand(connection, null,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__SchemaVersion'");
            if ((long)tableExists.ExecuteScalar() == 0)
            {
                return 0;
            }

            SqliteCommand readVersion = Database.CreateCommand(connection, null, "SELECT Version FROM __SchemaVersion WHERE Id = 1");
            object value = readVersion.ExecuteScalar();
            if (value == null)
            {
                return -1;
            }
            return Convert.ToInt32(value);
        }

        private void CheckRowCount(SqliteConnection connection, string tableName, int expected)
        {
            SqliteCommand command = Database.CreateCommand(connection, null, "SELECT COUNT(*) FROM " + tableName);
            long actual = (long)command.ExecuteScalar();
            if (actual != expected)
            {
                throw new InvalidDataException("数据库表 " + tableName + " 应有 " + expected + " 行，实际 " + actual
                    + " 行，请检查数据文件 " + database.FilePath);
            }
        }

        private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction, sql);
            command.ExecuteNonQuery();
        }

        private static void AddLog(SqliteConnection connection, SqliteTransaction transaction, string eventType, string message)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "INSERT INTO OperationLogs(OccurredAt, EventType, Message) VALUES($time, $type, $message)");
            Database.AddParameter(command, "$time", Database.FormatTime(DateTime.Now));
            Database.AddParameter(command, "$type", eventType);
            Database.AddParameter(command, "$message", message);
            command.ExecuteNonQuery();
        }
    }
}
