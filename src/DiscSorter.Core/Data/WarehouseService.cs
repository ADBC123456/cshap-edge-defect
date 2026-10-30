using System;
using System.Collections.Generic;
using DiscSorter.Core.Models;
using Microsoft.Data.Sqlite;

namespace DiscSorter.Core.Data
{
    /// <summary>
    /// 仓储服务：库位、列映射、入库任务、仓储日志的读写都在这里。
    ///
    /// 入库分两步：
    ///   1. Reserve()  在该类别对应的列里找行号最小的空闲库位，标为"执行中"，得到目标库位号；
    ///   2. Complete() PLC 反馈放好以后调用，库位改为"已占用"。
    /// 每个写操作都在一个事务里完成，要么全部成功，要么全部不改。
    /// 写成功后触发 Changed 事件，界面收到后重新读取并刷新。
    /// </summary>
    public class WarehouseService
    {
        private readonly Database database;

        // 写操作加锁，保证同一时刻只有一个写操作在执行（防止两件工件被分到同一个库位）
        private readonly object writeLock = new object();

        public WarehouseService(Database database)
        {
            this.database = database;
        }

        /// <summary>库位、映射或日志发生了变化。</summary>
        public event EventHandler Changed;

        // ======================================================================
        // 读取
        // ======================================================================

        /// <summary>读取 12 个库位，按库位号排序。</summary>
        public List<StorageSlot> GetSlots()
        {
            List<StorageSlot> slots = new List<StorageSlot>();
            using (SqliteConnection connection = database.OpenConnection())
            {
                SqliteCommand command = Database.CreateCommand(connection, null,
                    "SELECT SlotNo, Row, Column, State, TaskId, Category, UpdatedAt FROM StorageSlots ORDER BY SlotNo");
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        StorageSlot slot = new StorageSlot();
                        slot.SlotNo = reader.GetInt32(0);
                        slot.Row = reader.GetInt32(1);
                        slot.Column = reader.GetInt32(2);
                        slot.State = (SlotState)Enum.Parse(typeof(SlotState), reader.GetString(3));
                        if (!reader.IsDBNull(4))
                        {
                            slot.TaskId = reader.GetInt64(4);
                        }
                        if (!reader.IsDBNull(5))
                        {
                            slot.Category = ParseCategory(reader.GetString(5));
                        }
                        if (!reader.IsDBNull(6))
                        {
                            slot.UpdatedAt = Database.ParseTime(reader.GetString(6));
                        }
                        slots.Add(slot);
                    }
                }
            }
            return slots;
        }

        /// <summary>读取 4 列的映射，按列号排序。</summary>
        public List<ColumnMapping> GetMappings()
        {
            List<ColumnMapping> mappings = new List<ColumnMapping>();
            using (SqliteConnection connection = database.OpenConnection())
            {
                SqliteCommand command = Database.CreateCommand(connection, null,
                    "SELECT Column, Category FROM ColumnMappings ORDER BY Column");
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        mappings.Add(new ColumnMapping(reader.GetInt32(0), ParseCategory(reader.GetString(1))));
                    }
                }
            }
            return mappings;
        }

        /// <summary>读取最近的入库任务，新的在前。</summary>
        public List<StorageTask> GetRecentTasks(int count)
        {
            List<StorageTask> tasks = new List<StorageTask>();
            using (SqliteConnection connection = database.OpenConnection())
            {
                SqliteCommand command = Database.CreateCommand(connection, null,
                    "SELECT TaskId, JobId, Category, SlotNo, Status, CreatedAt, CompletedAt, Note " +
                    "FROM StorageTasks ORDER BY TaskId DESC LIMIT $count");
                Database.AddParameter(command, "$count", count);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        StorageTask task = new StorageTask();
                        task.TaskId = reader.GetInt64(0);
                        if (!reader.IsDBNull(1))
                        {
                            task.JobId = reader.GetInt64(1);
                        }
                        task.Category = ParseCategory(reader.GetString(2));
                        task.SlotNo = reader.GetInt32(3);
                        task.Status = (StorageTaskStatus)Enum.Parse(typeof(StorageTaskStatus), reader.GetString(4));
                        task.CreatedAt = Database.ParseTime(reader.GetString(5));
                        if (!reader.IsDBNull(6))
                        {
                            task.CompletedAt = Database.ParseTime(reader.GetString(6));
                        }
                        if (!reader.IsDBNull(7))
                        {
                            task.Note = reader.GetString(7);
                        }
                        tasks.Add(task);
                    }
                }
            }
            return tasks;
        }

        /// <summary>读取最近的操作日志，新的在前。</summary>
        public List<OperationLog> GetRecentLogs(int count)
        {
            List<OperationLog> logs = new List<OperationLog>();
            using (SqliteConnection connection = database.OpenConnection())
            {
                SqliteCommand command = Database.CreateCommand(connection, null,
                    "SELECT Id, OccurredAt, EventType, Message FROM OperationLogs ORDER BY Id DESC LIMIT $count");
                Database.AddParameter(command, "$count", count);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        OperationLog log = new OperationLog();
                        log.Id = reader.GetInt64(0);
                        log.Time = Database.ParseTime(reader.GetString(1));
                        log.EventType = reader.GetString(2);
                        log.Message = reader.GetString(3);
                        logs.Add(log);
                    }
                }
            }
            return logs;
        }

        // ======================================================================
        // 入库
        // ======================================================================

        /// <summary>
        /// 第 1 步：为一件工件分配库位。
        /// 在该类别对应的列里取行号最小的空闲库位，标为"执行中"，并新建一个入库任务。
        /// 该类别没有分配列、或者那一列已经满了，就不建任务，返回库位 0 和报警文字
        /// （对应 PLC 报警码 5：需要人工取件）。
        /// </summary>
        public AllocationResult Reserve(DefectCategory category, long? jobId)
        {
            AllocationResult result = new AllocationResult();
            result.Category = category;
            string categoryName = DefectCategories.GetName(category);

            lock (writeLock)
            {
                using (SqliteConnection connection = database.OpenConnection())
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    int column = FindColumnOfCategory(connection, transaction, category);
                    if (column == 0)
                    {
                        result.Alarm = categoryName + " 没有分配库区，请人工取件";
                        AddLog(connection, transaction, "Unmapped", result.Alarm, null);
                    }
                    else
                    {
                        int slotNo = FindEmptySlot(connection, transaction, column);
                        if (slotNo == 0)
                        {
                            result.Alarm = categoryName + " 库区（列 " + column + "）已满，请人工取件";
                            AddLog(connection, transaction, "Full", result.Alarm, null);
                        }
                        else
                        {
                            long taskId = InsertTask(connection, transaction, jobId, category, slotNo);
                            SetSlotReserved(connection, transaction, slotNo, taskId, category);
                            AddLog(connection, transaction, "Reserved", categoryName + " 预留" + SlotNumbers.Describe(slotNo), taskId);
                            result.TaskId = taskId;
                            result.TargetSlot = slotNo;
                        }
                    }
                    transaction.Commit();
                }
            }

            RaiseChanged();
            return result;
        }

        /// <summary>
        /// 第 2 步：PLC 反馈放置完成。
        /// 反馈的库位和预留的库位一致 → 库位改为"已占用"，返回 true；
        /// 不一致 → 库位和任务都标为"待确认"，返回 false。
        /// </summary>
        public bool Complete(long taskId, int doneSlot)
        {
            bool success;
            lock (writeLock)
            {
                using (SqliteConnection connection = database.OpenConnection())
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    int slotNo = GetSlotOfReservedTask(connection, transaction, taskId);
                    if (doneSlot == slotNo)
                    {
                        string now = Database.FormatTime(DateTime.Now);

                        SqliteCommand updateSlot = Database.CreateCommand(connection, transaction,
                            "UPDATE StorageSlots SET State = 'Occupied', UpdatedAt = $now WHERE SlotNo = $slot AND TaskId = $task");
                        Database.AddParameter(updateSlot, "$now", now);
                        Database.AddParameter(updateSlot, "$slot", slotNo);
                        Database.AddParameter(updateSlot, "$task", taskId);
                        updateSlot.ExecuteNonQuery();

                        SetTaskStatus(connection, transaction, taskId, StorageTaskStatus.Completed, null);
                        AddLog(connection, transaction, "Completed", "完成入库：" + SlotNumbers.Describe(slotNo), taskId);
                        success = true;
                    }
                    else
                    {
                        string reason = "PLC 反馈库位 " + doneSlot + " 与预留库位 " + slotNo + " 不一致";
                        SetNeedsCheck(connection, transaction, taskId, reason);
                        success = false;
                    }
                    transaction.Commit();
                }
            }

            RaiseChanged();
            return success;
        }

        /// <summary>无法确定工件是否放入（例如等待超时）：库位和任务标为"待确认"，由人工处理。</summary>
        public void MarkNeedsCheck(long taskId, string reason)
        {
            lock (writeLock)
            {
                using (SqliteConnection connection = database.OpenConnection())
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    GetSlotOfReservedTask(connection, transaction, taskId);
                    SetNeedsCheck(connection, transaction, taskId, reason);
                    transaction.Commit();
                }
            }
            RaiseChanged();
        }

        /// <summary>确定工件没有放入（例如 PLC 拒绝了任务）：释放库位，任务记为失败。</summary>
        public void Fail(long taskId, string reason)
        {
            lock (writeLock)
            {
                using (SqliteConnection connection = database.OpenConnection())
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    int slotNo = GetSlotOfReservedTask(connection, transaction, taskId);
                    SetSlotEmpty(connection, transaction, slotNo);
                    SetTaskStatus(connection, transaction, taskId, StorageTaskStatus.Failed, reason);
                    AddLog(connection, transaction, "Failed", "入库失败（库位 " + slotNo + "）：" + reason, taskId);
                    transaction.Commit();
                }
            }
            RaiseChanged();
        }

        /// <summary>
        /// 人工处理"待确认"的库位。
        /// placed = true：确认工件在库位上，库位记为已占用；
        /// placed = false：确认工件不在，释放库位。
        /// </summary>
        public void ResolveNeedsCheck(int slotNo, bool placed)
        {
            lock (writeLock)
            {
                using (SqliteConnection connection = database.OpenConnection())
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    SqliteCommand query = Database.CreateCommand(connection, transaction,
                        "SELECT TaskId FROM StorageSlots WHERE SlotNo = $slot AND State = 'NeedsCheck'");
                    Database.AddParameter(query, "$slot", slotNo);
                    object value = query.ExecuteScalar();
                    if (value == null)
                    {
                        throw new InvalidOperationException("库位 " + slotNo + " 不是待确认状态");
                    }
                    long taskId = (long)value;

                    if (placed)
                    {
                        SqliteCommand updateSlot = Database.CreateCommand(connection, transaction,
                            "UPDATE StorageSlots SET State = 'Occupied', UpdatedAt = $now WHERE SlotNo = $slot");
                        Database.AddParameter(updateSlot, "$now", Database.FormatTime(DateTime.Now));
                        Database.AddParameter(updateSlot, "$slot", slotNo);
                        updateSlot.ExecuteNonQuery();
                        SetTaskStatus(connection, transaction, taskId, StorageTaskStatus.Completed, null);
                        AddLog(connection, transaction, "Resolved", "人工确认库位 " + slotNo + " 已入库", taskId);
                    }
                    else
                    {
                        SetSlotEmpty(connection, transaction, slotNo);
                        SetTaskStatus(connection, transaction, taskId, StorageTaskStatus.Cancelled, null);
                        AddLog(connection, transaction, "Resolved", "人工确认库位 " + slotNo + " 未入库，已释放", taskId);
                    }
                    transaction.Commit();
                }
            }
            RaiseChanged();
        }

        // ======================================================================
        // 出库（人工取走工件后在界面上操作）
        // ======================================================================

        /// <summary>清空一个已占用的库位，返回清空的数量（0 或 1）。</summary>
        public int ClearSlot(int slotNo)
        {
            if (!SlotNumbers.IsValid(slotNo))
            {
                throw new ArgumentOutOfRangeException("slotNo", "库位号必须在 1～12 之间");
            }
            return ClearOccupiedSlots("SlotNo = $value", slotNo, "库位 " + slotNo);
        }

        /// <summary>清空一列中所有已占用的库位。执行中和待确认的库位不受影响。</summary>
        public int ClearColumn(int column)
        {
            if (column < 1 || column > SlotNumbers.ColumnCount)
            {
                throw new ArgumentOutOfRangeException("column", "列号必须在 1～4 之间");
            }
            return ClearOccupiedSlots("Column = $value", column, "列 " + column);
        }

        /// <summary>清空全部已占用的库位。</summary>
        public int ClearAll()
        {
            return ClearOccupiedSlots("1 = $value", 1, "全部库位");
        }

        // ======================================================================
        // 列映射
        // ======================================================================

        /// <summary>
        /// 保存列映射：4 列各选 Q/H/K/W/NG 中的一个类别，同一类别不能分到两列。
        /// 要改动的列里如果还有工件（包括执行中、待确认），拒绝保存。
        /// </summary>
        public void SaveMappings(List<ColumnMapping> mappings)
        {
            CheckMappings(mappings);

            lock (writeLock)
            {
                using (SqliteConnection connection = database.OpenConnection())
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    // 读出当前映射，找出哪些列改了
                    Dictionary<int, DefectCategory> current = new Dictionary<int, DefectCategory>();
                    SqliteCommand query = Database.CreateCommand(connection, transaction, "SELECT Column, Category FROM ColumnMappings");
                    using (SqliteDataReader reader = query.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            current[reader.GetInt32(0)] = ParseCategory(reader.GetString(1));
                        }
                    }

                    List<int> changedColumns = new List<int>();
                    foreach (ColumnMapping mapping in mappings)
                    {
                        if (current[mapping.Column] != mapping.Category)
                        {
                            changedColumns.Add(mapping.Column);
                        }
                    }
                    if (changedColumns.Count == 0)
                    {
                        return;   // 没有改动
                    }

                    foreach (int column in changedColumns)
                    {
                        if (CountBusySlots(connection, transaction, column) > 0)
                        {
                            throw new InvalidOperationException("列 " + column + " 中还有物料或未完成的任务，请先清空该列");
                        }
                    }

                    // 先删再插。ColumnMappings 的 Category 列有 UNIQUE 约束，逐行 UPDATE 交换类别时会冲突
                    SqliteCommand delete = Database.CreateCommand(connection, transaction, "DELETE FROM ColumnMappings");
                    delete.ExecuteNonQuery();
                    string newText = "";
                    foreach (ColumnMapping mapping in mappings)
                    {
                        SqliteCommand insert = Database.CreateCommand(connection, transaction,
                            "INSERT INTO ColumnMappings(Column, Category) VALUES($column, $category)");
                        Database.AddParameter(insert, "$column", mapping.Column);
                        Database.AddParameter(insert, "$category", mapping.Category.ToString());
                        insert.ExecuteNonQuery();
                        newText = newText + " 列" + mapping.Column + "=" + mapping.Category;
                    }
                    AddLog(connection, transaction, "MappingChanged", "列映射改为：" + newText, null);
                    transaction.Commit();
                }
            }
            RaiseChanged();
        }

        // ======================================================================
        // 以下是内部使用的小方法
        // ======================================================================

        private void RaiseChanged()
        {
            if (Changed != null)
            {
                Changed(this, EventArgs.Empty);
            }
        }

        private static DefectCategory ParseCategory(string text)
        {
            return (DefectCategory)Enum.Parse(typeof(DefectCategory), text);
        }

        private static void CheckMappings(List<ColumnMapping> mappings)
        {
            if (mappings == null || mappings.Count != SlotNumbers.ColumnCount)
            {
                throw new ArgumentException("必须配置列 1 至列 4");
            }

            bool[] columnUsed = new bool[SlotNumbers.ColumnCount + 1];
            List<DefectCategory> usedCategories = new List<DefectCategory>();
            foreach (ColumnMapping mapping in mappings)
            {
                if (mapping.Column < 1 || mapping.Column > SlotNumbers.ColumnCount || columnUsed[mapping.Column])
                {
                    throw new ArgumentException("必须配置列 1 至列 4，每列一次");
                }
                if (Array.IndexOf(DefectCategories.All, mapping.Category) < 0)
                {
                    throw new ArgumentException("无效的类别：" + mapping.Category);
                }
                if (usedCategories.Contains(mapping.Category))
                {
                    throw new ArgumentException("同一类别不能分到两列");
                }
                columnUsed[mapping.Column] = true;
                usedCategories.Add(mapping.Category);
            }
        }

        /// <summary>该类别对应哪一列，没有分配列时返回 0。</summary>
        private static int FindColumnOfCategory(SqliteConnection connection, SqliteTransaction transaction, DefectCategory category)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "SELECT Column FROM ColumnMappings WHERE Category = $category");
            Database.AddParameter(command, "$category", category.ToString());
            object value = command.ExecuteScalar();
            if (value == null)
            {
                return 0;
            }
            return Convert.ToInt32(value);
        }

        /// <summary>该列中行号最小的空闲库位，没有空位时返回 0。</summary>
        private static int FindEmptySlot(SqliteConnection connection, SqliteTransaction transaction, int column)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "SELECT SlotNo FROM StorageSlots WHERE Column = $column AND State = 'Empty' ORDER BY Row LIMIT 1");
            Database.AddParameter(command, "$column", column);
            object value = command.ExecuteScalar();
            if (value == null)
            {
                return 0;
            }
            return Convert.ToInt32(value);
        }

        private static long InsertTask(SqliteConnection connection, SqliteTransaction transaction, long? jobId, DefectCategory category, int slotNo)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "INSERT INTO StorageTasks(JobId, Category, SlotNo, Status, CreatedAt) VALUES($job, $category, $slot, 'Reserved', $now); " +
                "SELECT last_insert_rowid();");
            Database.AddParameter(command, "$job", jobId);
            Database.AddParameter(command, "$category", category.ToString());
            Database.AddParameter(command, "$slot", slotNo);
            Database.AddParameter(command, "$now", Database.FormatTime(DateTime.Now));
            return (long)command.ExecuteScalar();
        }

        private static void SetSlotReserved(SqliteConnection connection, SqliteTransaction transaction, int slotNo, long taskId, DefectCategory category)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "UPDATE StorageSlots SET State = 'Reserved', TaskId = $task, Category = $category, UpdatedAt = $now " +
                "WHERE SlotNo = $slot AND State = 'Empty'");
            Database.AddParameter(command, "$task", taskId);
            Database.AddParameter(command, "$category", category.ToString());
            Database.AddParameter(command, "$now", Database.FormatTime(DateTime.Now));
            Database.AddParameter(command, "$slot", slotNo);
            int changed = command.ExecuteNonQuery();
            if (changed != 1)
            {
                throw new InvalidOperationException("库位 " + slotNo + " 状态已改变，请重试");
            }
        }

        private static void SetSlotEmpty(SqliteConnection connection, SqliteTransaction transaction, int slotNo)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "UPDATE StorageSlots SET State = 'Empty', TaskId = NULL, Category = NULL, UpdatedAt = $now WHERE SlotNo = $slot");
            Database.AddParameter(command, "$now", Database.FormatTime(DateTime.Now));
            Database.AddParameter(command, "$slot", slotNo);
            command.ExecuteNonQuery();
        }

        /// <summary>修改任务状态。完成、失败、取消时同时记下完成时间。</summary>
        private static void SetTaskStatus(SqliteConnection connection, SqliteTransaction transaction, long taskId, StorageTaskStatus status, string note)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "UPDATE StorageTasks SET Status = $status, CompletedAt = $now, Note = COALESCE($note, Note) WHERE TaskId = $task");
            Database.AddParameter(command, "$status", status.ToString());
            Database.AddParameter(command, "$now", Database.FormatTime(DateTime.Now));
            Database.AddParameter(command, "$note", note);
            Database.AddParameter(command, "$task", taskId);
            command.ExecuteNonQuery();
        }

        private static void SetNeedsCheck(SqliteConnection connection, SqliteTransaction transaction, long taskId, string reason)
        {
            SqliteCommand updateSlot = Database.CreateCommand(connection, transaction,
                "UPDATE StorageSlots SET State = 'NeedsCheck', UpdatedAt = $now WHERE TaskId = $task AND State = 'Reserved'");
            Database.AddParameter(updateSlot, "$now", Database.FormatTime(DateTime.Now));
            Database.AddParameter(updateSlot, "$task", taskId);
            updateSlot.ExecuteNonQuery();

            SqliteCommand updateTask = Database.CreateCommand(connection, transaction,
                "UPDATE StorageTasks SET Status = 'NeedsCheck', Note = $reason WHERE TaskId = $task");
            Database.AddParameter(updateTask, "$reason", reason);
            Database.AddParameter(updateTask, "$task", taskId);
            updateTask.ExecuteNonQuery();

            AddLog(connection, transaction, "NeedsCheck", reason, taskId);
        }

        /// <summary>查出一个"执行中"任务的库位号。任务不存在或不是执行中时抛出异常。</summary>
        private static int GetSlotOfReservedTask(SqliteConnection connection, SqliteTransaction transaction, long taskId)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "SELECT SlotNo, Status FROM StorageTasks WHERE TaskId = $task");
            Database.AddParameter(command, "$task", taskId);
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    throw new InvalidOperationException("任务 " + taskId + " 不存在");
                }
                string status = reader.GetString(1);
                if (status != StorageTaskStatus.Reserved.ToString())
                {
                    throw new InvalidOperationException("任务 " + taskId + " 当前状态为 " + status + "，不是执行中");
                }
                return reader.GetInt32(0);
            }
        }

        private static long CountBusySlots(SqliteConnection connection, SqliteTransaction transaction, int column)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "SELECT COUNT(*) FROM StorageSlots WHERE Column = $column AND State <> 'Empty'");
            Database.AddParameter(command, "$column", column);
            return (long)command.ExecuteScalar();
        }

        private int ClearOccupiedSlots(string condition, int value, string scope)
        {
            int count;
            lock (writeLock)
            {
                using (SqliteConnection connection = database.OpenConnection())
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    SqliteCommand command = Database.CreateCommand(connection, transaction,
                        "UPDATE StorageSlots SET State = 'Empty', TaskId = NULL, Category = NULL, UpdatedAt = $now " +
                        "WHERE " + condition + " AND State = 'Occupied'");
                    Database.AddParameter(command, "$now", Database.FormatTime(DateTime.Now));
                    Database.AddParameter(command, "$value", value);
                    count = command.ExecuteNonQuery();
                    if (count > 0)
                    {
                        AddLog(connection, transaction, "Cleared", "人工出库：" + scope + "，释放 " + count + " 个库位", null);
                    }
                    transaction.Commit();
                }
            }
            if (count > 0)
            {
                RaiseChanged();
            }
            return count;
        }

        private static void AddLog(SqliteConnection connection, SqliteTransaction transaction, string eventType, string message, long? taskId)
        {
            SqliteCommand command = Database.CreateCommand(connection, transaction,
                "INSERT INTO OperationLogs(OccurredAt, EventType, Message, TaskId) VALUES($time, $type, $message, $task)");
            Database.AddParameter(command, "$time", Database.FormatTime(DateTime.Now));
            Database.AddParameter(command, "$type", eventType);
            Database.AddParameter(command, "$message", message);
            Database.AddParameter(command, "$task", taskId);
            command.ExecuteNonQuery();
        }
    }
}
