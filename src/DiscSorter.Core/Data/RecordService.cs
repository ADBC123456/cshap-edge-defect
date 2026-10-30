using System;
using System.Collections.Generic;
using DiscSorter.Core.Models;
using Microsoft.Data.Sqlite;

namespace DiscSorter.Core.Data
{
    /// <summary>检测记录：每检测一件写一条，供监控页显示和论文统计正确率、耗时。</summary>
    public class RecordService
    {
        private readonly Database database;

        public RecordService(Database database)
        {
            this.database = database;
        }

        /// <summary>保存一条检测记录，返回记录号。</summary>
        public long Add(InspectionRecord record)
        {
            using (SqliteConnection connection = database.OpenConnection())
            {
                SqliteCommand command = Database.CreateCommand(connection, null,
                    "INSERT INTO InspectionRecords(JobId, CapturedAt, ImagePath, AlgorithmLabel, Category, CategorySource, " +
                    "    ExpectedLabel, IsCorrect, TargetSlot, TaskId, CircleFound, CircleRow, CircleColumn, Radius, DetectMs, TotalMs) " +
                    "VALUES($job, $time, $path, $label, $category, $source, " +
                    "    $expected, $correct, $slot, $task, $found, $row, $column, $radius, $detect, $total); " +
                    "SELECT last_insert_rowid();");
                Database.AddParameter(command, "$job", record.JobId);
                Database.AddParameter(command, "$time", Database.FormatTime(record.Time));
                Database.AddParameter(command, "$path", record.ImagePath);
                Database.AddParameter(command, "$label", record.AlgorithmLabel);
                Database.AddParameter(command, "$category", record.Category.ToString());
                Database.AddParameter(command, "$source", record.Source.ToString());
                Database.AddParameter(command, "$expected", record.ExpectedLabel);
                if (record.IsCorrect == null)
                {
                    Database.AddParameter(command, "$correct", null);
                }
                else
                {
                    Database.AddParameter(command, "$correct", record.IsCorrect.Value ? 1 : 0);
                }
                Database.AddParameter(command, "$slot", record.TargetSlot);
                Database.AddParameter(command, "$task", record.TaskId);
                Database.AddParameter(command, "$found", record.CircleFound ? 1 : 0);
                Database.AddParameter(command, "$row", record.CircleRow);
                Database.AddParameter(command, "$column", record.CircleColumn);
                Database.AddParameter(command, "$radius", record.Radius);
                Database.AddParameter(command, "$detect", record.DetectMs);
                Database.AddParameter(command, "$total", record.TotalMs);
                long id = (long)command.ExecuteScalar();
                record.Id = id;
                return id;
            }
        }

        /// <summary>读取最近的检测记录，新的在前。</summary>
        public List<InspectionRecord> GetRecent(int count)
        {
            List<InspectionRecord> records = new List<InspectionRecord>();
            using (SqliteConnection connection = database.OpenConnection())
            {
                SqliteCommand command = Database.CreateCommand(connection, null,
                    "SELECT Id, JobId, CapturedAt, ImagePath, AlgorithmLabel, Category, CategorySource, ExpectedLabel, IsCorrect, " +
                    "       TargetSlot, TaskId, CircleFound, CircleRow, CircleColumn, Radius, DetectMs, TotalMs " +
                    "FROM InspectionRecords ORDER BY Id DESC LIMIT $count");
                Database.AddParameter(command, "$count", count);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        InspectionRecord record = new InspectionRecord();
                        record.Id = reader.GetInt64(0);
                        if (!reader.IsDBNull(1))
                        {
                            record.JobId = reader.GetInt64(1);
                        }
                        record.Time = Database.ParseTime(reader.GetString(2));
                        record.ImagePath = ReadText(reader, 3);
                        record.AlgorithmLabel = ReadText(reader, 4);
                        record.Category = (DefectCategory)Enum.Parse(typeof(DefectCategory), reader.GetString(5));
                        record.Source = (CategorySource)Enum.Parse(typeof(CategorySource), reader.GetString(6));
                        record.ExpectedLabel = ReadText(reader, 7);
                        if (!reader.IsDBNull(8))
                        {
                            record.IsCorrect = reader.GetInt32(8) == 1;
                        }
                        record.TargetSlot = reader.GetInt32(9);
                        if (!reader.IsDBNull(10))
                        {
                            record.TaskId = reader.GetInt64(10);
                        }
                        record.CircleFound = reader.GetInt32(11) == 1;
                        record.CircleRow = ReadNumber(reader, 12);
                        record.CircleColumn = ReadNumber(reader, 13);
                        record.Radius = ReadNumber(reader, 14);
                        record.DetectMs = ReadNumber(reader, 15);
                        record.TotalMs = ReadNumber(reader, 16);
                        records.Add(record);
                    }
                }
            }
            return records;
        }

        /// <summary>统计某个时间以后的检测情况（监控页传入今天 0 点）。</summary>
        public InspectionStats GetStats(DateTime since)
        {
            InspectionStats stats = new InspectionStats();
            string sinceText = Database.FormatTime(since);

            using (SqliteConnection connection = database.OpenConnection())
            {
                // 各类别件数
                SqliteCommand countByCategory = Database.CreateCommand(connection, null,
                    "SELECT Category, COUNT(*) FROM InspectionRecords WHERE CapturedAt >= $since GROUP BY Category");
                Database.AddParameter(countByCategory, "$since", sinceText);
                using (SqliteDataReader reader = countByCategory.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DefectCategory category = (DefectCategory)Enum.Parse(typeof(DefectCategory), reader.GetString(0));
                        stats.Counts[category] = reader.GetInt32(1);
                    }
                }

                // 总数、能判断对错的件数、判对件数、平均和最大耗时
                SqliteCommand summary = Database.CreateCommand(connection, null,
                    "SELECT COUNT(*), COUNT(IsCorrect), COALESCE(SUM(IsCorrect), 0), AVG(TotalMs), MAX(TotalMs) " +
                    "FROM InspectionRecords WHERE CapturedAt >= $since");
                Database.AddParameter(summary, "$since", sinceText);
                using (SqliteDataReader reader = summary.ExecuteReader())
                {
                    reader.Read();
                    stats.Total = reader.GetInt32(0);
                    stats.Judged = reader.GetInt32(1);
                    stats.Correct = reader.GetInt32(2);
                    stats.AverageMs = ReadNumber(reader, 3);
                    stats.MaxMs = ReadNumber(reader, 4);
                }
            }
            return stats;
        }

        private static string ReadText(SqliteDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return null;
            }
            return reader.GetString(index);
        }

        private static double? ReadNumber(SqliteDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return null;
            }
            return reader.GetDouble(index);
        }
    }
}
