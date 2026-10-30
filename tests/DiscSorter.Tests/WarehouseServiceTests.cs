using System;
using System.Collections.Generic;
using System.IO;
using DiscSorter.Core.Data;
using DiscSorter.Core.Models;
using Xunit;

namespace DiscSorter.Tests
{
    public class WarehouseServiceTests : IDisposable
    {
        private readonly TestDatabase testDatabase = new TestDatabase();

        public void Dispose()
        {
            testDatabase.Dispose();
        }

        [Fact]
        public void NewDatabaseHasDefaultMappingAndTwelveEmptySlots()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();

            List<StorageSlot> slots = warehouse.GetSlots();
            Assert.Equal(12, slots.Count);
            for (int i = 0; i < 12; i++)
            {
                Assert.Equal(i + 1, slots[i].SlotNo);
                Assert.Equal(SlotState.Empty, slots[i].State);
            }

            List<ColumnMapping> mappings = warehouse.GetMappings();
            Assert.Equal(DefectCategory.Q, mappings[0].Category);
            Assert.Equal(DefectCategory.H, mappings[1].Category);
            Assert.Equal(DefectCategory.K, mappings[2].Category);
            Assert.Equal(DefectCategory.W, mappings[3].Category);

            // 再初始化一次不会出错，也不会重复插入数据
            new DatabaseInitializer(testDatabase.Database).Initialize();
            Assert.Equal(12, warehouse.GetSlots().Count);
        }

        [Fact]
        public void SameCategoryFillsRowsOneByOneThenAlarmsWithSlotZero()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();

            for (int row = 1; row <= 3; row++)
            {
                AllocationResult result = warehouse.Reserve(DefectCategory.H, row);
                Assert.True(result.Success);
                Assert.Equal(3 + row, result.TargetSlot);   // H 在列 2：库位 4、5、6
                Assert.True(warehouse.Complete(result.TaskId, result.TargetSlot));
            }

            AllocationResult full = warehouse.Reserve(DefectCategory.H, 4);
            Assert.False(full.Success);
            Assert.Equal(0, full.TargetSlot);
            Assert.Contains("已满", full.Alarm);
            Assert.Equal("Full", warehouse.GetRecentLogs(1)[0].EventType);
        }

        [Fact]
        public void ReservedSlotIsNotGivenOutTwice()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();

            AllocationResult first = warehouse.Reserve(DefectCategory.Q, 1);
            AllocationResult second = warehouse.Reserve(DefectCategory.Q, 2);

            Assert.Equal(1, first.TargetSlot);
            Assert.Equal(2, second.TargetSlot);
            Assert.Equal(SlotState.Reserved, warehouse.GetSlots()[0].State);
        }

        [Fact]
        public void CategoryWithoutColumnGetsSlotZero()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();

            AllocationResult ng = warehouse.Reserve(DefectCategory.NG, 1);
            Assert.False(ng.Success);
            Assert.Contains("没有分配库区", ng.Alarm);

            // 把列 4 改给 NG 以后，NG 有了库区，W 反而没有
            warehouse.SaveMappings(Mappings(DefectCategory.Q, DefectCategory.H, DefectCategory.K, DefectCategory.NG));
            Assert.Equal(10, warehouse.Reserve(DefectCategory.NG, 2).TargetSlot);
            Assert.Equal(0, warehouse.Reserve(DefectCategory.W, 3).TargetSlot);
        }

        [Fact]
        public void WrongDoneSlotMarksNeedsCheck()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();
            AllocationResult reserved = warehouse.Reserve(DefectCategory.Q, 1);

            bool success = warehouse.Complete(reserved.TaskId, 5);

            Assert.False(success);
            Assert.Equal(SlotState.NeedsCheck, warehouse.GetSlots()[0].State);
            StorageTask task = warehouse.GetRecentTasks(1)[0];
            Assert.Equal(StorageTaskStatus.NeedsCheck, task.Status);
            Assert.Contains("不一致", task.Note);
        }

        [Theory]
        [InlineData(true, SlotState.Occupied, StorageTaskStatus.Completed)]
        [InlineData(false, SlotState.Empty, StorageTaskStatus.Cancelled)]
        public void NeedsCheckIsResolvedByHand(bool placed, SlotState expectedSlot, StorageTaskStatus expectedTask)
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();
            AllocationResult reserved = warehouse.Reserve(DefectCategory.W, 1);
            warehouse.MarkNeedsCheck(reserved.TaskId, "等待完成超时");

            warehouse.ResolveNeedsCheck(reserved.TargetSlot, placed);

            Assert.Equal(expectedSlot, warehouse.GetSlots()[reserved.TargetSlot - 1].State);
            Assert.Equal(expectedTask, warehouse.GetRecentTasks(1)[0].Status);
            Assert.Throws<InvalidOperationException>(() => warehouse.ResolveNeedsCheck(reserved.TargetSlot, placed));
        }

        [Fact]
        public void FailReleasesSlot()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();
            AllocationResult reserved = warehouse.Reserve(DefectCategory.Q, 1);

            warehouse.Fail(reserved.TaskId, "PLC 拒绝任务");

            Assert.Equal(SlotState.Empty, warehouse.GetSlots()[0].State);
            Assert.Equal(StorageTaskStatus.Failed, warehouse.GetRecentTasks(1)[0].Status);
            Assert.Throws<InvalidOperationException>(() => warehouse.Complete(reserved.TaskId, 1));
        }

        [Fact]
        public void MappingRejectsDuplicatesAndMissingColumns()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();

            Assert.Throws<ArgumentException>(() =>
                warehouse.SaveMappings(Mappings(DefectCategory.Q, DefectCategory.Q, DefectCategory.K, DefectCategory.W)));

            List<ColumnMapping> threeColumns = new List<ColumnMapping>();
            threeColumns.Add(new ColumnMapping(1, DefectCategory.Q));
            threeColumns.Add(new ColumnMapping(2, DefectCategory.H));
            threeColumns.Add(new ColumnMapping(3, DefectCategory.K));
            Assert.Throws<ArgumentException>(() => warehouse.SaveMappings(threeColumns));
        }

        [Fact]
        public void MappingChangeRedirectsAllocationAndBusyColumnCannotChange()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();
            warehouse.SaveMappings(Mappings(DefectCategory.H, DefectCategory.Q, DefectCategory.K, DefectCategory.W));

            AllocationResult q = warehouse.Reserve(DefectCategory.Q, 1);
            Assert.Equal(4, q.TargetSlot);   // Q 改到了列 2

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                warehouse.SaveMappings(Mappings(DefectCategory.Q, DefectCategory.H, DefectCategory.K, DefectCategory.W)));
            Assert.Contains("列 2", error.Message);
        }

        [Fact]
        public void ClearingKeepsTaskHistoryAndSkipsUnfinishedSlots()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();
            Store(warehouse, DefectCategory.Q);
            Store(warehouse, DefectCategory.Q);
            Store(warehouse, DefectCategory.H);
            AllocationResult pending = warehouse.Reserve(DefectCategory.K, 4);

            Assert.Equal(1, warehouse.ClearSlot(4));
            Assert.Equal(2, warehouse.ClearColumn(1));
            Assert.Equal(0, warehouse.ClearAll());

            Assert.Equal(SlotState.Reserved, warehouse.GetSlots()[pending.TargetSlot - 1].State);
            Assert.Equal(4, warehouse.GetRecentTasks(10).Count);
        }

        [Fact]
        public void RecoverTurnsReservedIntoNeedsCheck()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();
            warehouse.Reserve(DefectCategory.Q, 1);
            DatabaseInitializer initializer = new DatabaseInitializer(testDatabase.Database);

            Assert.Equal(1, initializer.Recover());

            Assert.Equal(SlotState.NeedsCheck, warehouse.GetSlots()[0].State);
            Assert.Equal(StorageTaskStatus.NeedsCheck, warehouse.GetRecentTasks(1)[0].Status);
            Assert.Equal("Recovery", warehouse.GetRecentLogs(1)[0].EventType);
            Assert.Equal(0, initializer.Recover());
        }

        [Fact]
        public void ChangedEventOnlyFiresWhenSomethingChanged()
        {
            WarehouseService warehouse = testDatabase.CreateWarehouse();
            int count = 0;
            warehouse.Changed += delegate { count++; };

            warehouse.ClearAll();
            warehouse.SaveMappings(Mappings(DefectCategory.Q, DefectCategory.H, DefectCategory.K, DefectCategory.W));
            Assert.Equal(0, count);

            warehouse.Reserve(DefectCategory.Q, 1);
            Assert.Equal(1, count);
        }

        [Fact]
        public void NewerAndDamagedDatabasesAreRejected()
        {
            testDatabase.Execute("CREATE TABLE __SchemaVersion(Id INTEGER PRIMARY KEY, Version INTEGER); INSERT INTO __SchemaVersion VALUES(1, 99);");
            Assert.Throws<InvalidOperationException>(() => new DatabaseInitializer(testDatabase.Database).Initialize());

            using (TestDatabase other = new TestDatabase())
            {
                other.CreateWarehouse();
                other.Execute("DELETE FROM StorageSlots WHERE SlotNo = 12");
                Assert.Throws<InvalidDataException>(() => new DatabaseInitializer(other.Database).Initialize());
            }
        }

        [Fact]
        public void VersionOneDatabaseIsUpgradedWithoutLosingData()
        {
            testDatabase.Execute(VersionOneSql);

            WarehouseService warehouse = testDatabase.CreateWarehouse();

            // 数据原样保留；映射是 v1 默认值且列 4 空闲，列 4 自动改成 W
            List<ColumnMapping> mappings = warehouse.GetMappings();
            Assert.Equal(DefectCategory.W, mappings[3].Category);
            StorageSlot slot1 = warehouse.GetSlots()[0];
            Assert.Equal(SlotState.Occupied, slot1.State);
            Assert.Equal(DefectCategory.Q, slot1.Category);
            Assert.Equal(StorageTaskStatus.Completed, warehouse.GetRecentTasks(5)[0].Status);
            Assert.Equal(2L, testDatabase.Scalar("SELECT Version FROM __SchemaVersion"));
            Assert.Equal(1L, testDatabase.Scalar("SELECT COUNT(*) FROM InspectionRecords"));
            Assert.Equal(10, warehouse.Reserve(DefectCategory.W, 2).TargetSlot);
        }

        [Fact]
        public void UpgradeKeepsNgColumnWhenItHoldsParts()
        {
            testDatabase.Execute(VersionOneSql
                + "INSERT INTO StorageTasks(JobId, Category, SlotNo, Status, CreatedAt) VALUES(2, 'NG', 10, 'Completed', '2026-10-27T00:00:00Z');"
                + "UPDATE StorageSlots SET State = 'Occupied', TaskId = 2, Category = 'NG' WHERE SlotNo = 10;");

            WarehouseService warehouse = testDatabase.CreateWarehouse();

            Assert.Equal(DefectCategory.NG, warehouse.GetMappings()[3].Category);
        }

        [Fact]
        public void SettingsAreSavedAndRead()
        {
            testDatabase.CreateWarehouse();
            SettingsService settings = new SettingsService(testDatabase.Database);

            settings.Set(SettingKeys.SimArrivalMs, "800");
            settings.Set(SettingKeys.SimArrivalMs, "900");

            Assert.Equal(900, settings.GetInt(SettingKeys.SimArrivalMs, 1500));
            Assert.Equal("默认", settings.Get("Missing", "默认"));
            Assert.Equal(1500, settings.GetInt("Missing", 1500));
        }

        private static List<ColumnMapping> Mappings(DefectCategory column1, DefectCategory column2, DefectCategory column3, DefectCategory column4)
        {
            List<ColumnMapping> mappings = new List<ColumnMapping>();
            mappings.Add(new ColumnMapping(1, column1));
            mappings.Add(new ColumnMapping(2, column2));
            mappings.Add(new ColumnMapping(3, column3));
            mappings.Add(new ColumnMapping(4, column4));
            return mappings;
        }

        private static void Store(WarehouseService warehouse, DefectCategory category)
        {
            AllocationResult result = warehouse.Reserve(category, null);
            warehouse.Complete(result.TaskId, result.TargetSlot);
        }

        /// <summary>v1 数据库：类别只有 Q/H/K/NG，库位 1 有一件已入库的 Q，时间是早期的 ISO 格式。</summary>
        private const string VersionOneSql = @"
            CREATE TABLE __SchemaVersion(Id INTEGER PRIMARY KEY CHECK(Id=1), Version INTEGER NOT NULL);
            CREATE TABLE ColumnMappings(Column INTEGER PRIMARY KEY CHECK(Column BETWEEN 1 AND 4),
                Category TEXT NOT NULL UNIQUE CHECK(Category IN ('Q','H','K','NG')));
            CREATE TABLE StorageTasks(TaskId INTEGER PRIMARY KEY AUTOINCREMENT, JobId INTEGER NULL,
                Category TEXT NOT NULL CHECK(Category IN ('Q','H','K','NG')), SlotNo INTEGER NOT NULL CHECK(SlotNo BETWEEN 1 AND 12),
                Status TEXT NOT NULL CHECK(Status IN ('Reserved','Completed','Failed','Cancelled','NeedsCheck')),
                CreatedAt TEXT NOT NULL, CompletedAt TEXT NULL, Note TEXT NULL);
            CREATE TABLE StorageSlots(SlotNo INTEGER PRIMARY KEY CHECK(SlotNo BETWEEN 1 AND 12),
                Row INTEGER NOT NULL CHECK(Row BETWEEN 1 AND 3), Column INTEGER NOT NULL CHECK(Column BETWEEN 1 AND 4),
                State TEXT NOT NULL CHECK(State IN ('Empty','Reserved','Occupied','NeedsCheck')),
                TaskId INTEGER NULL REFERENCES StorageTasks(TaskId),
                Category TEXT NULL CHECK(Category IS NULL OR Category IN ('Q','H','K','NG')), UpdatedAt TEXT NULL,
                UNIQUE(Row, Column), CHECK(SlotNo = (Column - 1) * 3 + Row),
                CHECK((State='Empty' AND TaskId IS NULL AND Category IS NULL) OR (State<>'Empty' AND TaskId IS NOT NULL AND Category IS NOT NULL)));
            CREATE TABLE InspectionRecords(Id INTEGER PRIMARY KEY AUTOINCREMENT, JobId INTEGER NULL, CapturedAt TEXT NOT NULL,
                ImagePath TEXT NULL, AlgorithmLabel TEXT NULL, Category TEXT NOT NULL CHECK(Category IN ('Q','H','K','NG')),
                CategorySource TEXT NOT NULL CHECK(CategorySource IN ('Algorithm','FileName')), ExpectedLabel TEXT NULL,
                IsCorrect INTEGER NULL, TargetSlot INTEGER NOT NULL CHECK(TargetSlot BETWEEN 0 AND 12),
                TaskId INTEGER NULL REFERENCES StorageTasks(TaskId), CircleFound INTEGER NOT NULL, CircleRow REAL NULL,
                CircleColumn REAL NULL, Radius REAL NULL, DetectMs REAL NULL, TotalMs REAL NULL, Result TEXT NULL);
            CREATE TABLE OperationLogs(Id INTEGER PRIMARY KEY AUTOINCREMENT, OccurredAt TEXT NOT NULL, EventType TEXT NOT NULL,
                Message TEXT NOT NULL, TaskId INTEGER NULL);
            CREATE TABLE AppSettings(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
            INSERT INTO __SchemaVersion VALUES(1, 1);
            INSERT INTO ColumnMappings VALUES(1,'Q'),(2,'H'),(3,'K'),(4,'NG');
            INSERT INTO StorageTasks(JobId, Category, SlotNo, Status, CreatedAt, CompletedAt)
                VALUES(1,'Q',1,'Completed','2026-10-27T00:00:00Z','2026-10-27T00:00:01Z');
            INSERT INTO StorageSlots(SlotNo, Row, Column, State, TaskId, Category) VALUES(1,1,1,'Occupied',1,'Q');
            INSERT INTO StorageSlots(SlotNo, Row, Column, State) VALUES
                (2,2,1,'Empty'),(3,3,1,'Empty'),(4,1,2,'Empty'),(5,2,2,'Empty'),(6,3,2,'Empty'),
                (7,1,3,'Empty'),(8,2,3,'Empty'),(9,3,3,'Empty'),(10,1,4,'Empty'),(11,2,4,'Empty'),(12,3,4,'Empty');
            INSERT INTO InspectionRecords(JobId, CapturedAt, Category, CategorySource, TargetSlot, TaskId, CircleFound)
                VALUES(1,'2026-10-27T00:00:00Z','Q','FileName',1,1,1);";
    }
}
