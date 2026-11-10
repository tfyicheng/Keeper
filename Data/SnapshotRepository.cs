using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using Keeper.Models;

namespace Keeper.Data
{
    /// <summary>
    /// 工作状态快照的数据访问。
    /// </summary>
    public class SnapshotRepository
    {
        public IList<SnapshotInfo> GetAll()
        {
            var result = new List<SnapshotInfo>();
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT s.Id, s.Name, s.CreatedAt, IFNULL(c.Total, 0) AS Total
FROM Snapshots s
LEFT JOIN (SELECT SnapshotId, COUNT(*) AS Total FROM SnapshotItems GROUP BY SnapshotId) c
    ON c.SnapshotId = s.Id
ORDER BY s.CreatedAt DESC, s.Id DESC;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new SnapshotInfo
                        {
                            Id = reader.GetInt64(0),
                            Name = reader.GetString(1),
                            CreatedAt = ParseDate(reader.GetString(2)),
                            ItemCount = (int)reader.GetInt64(3)
                        });
                    }
                }
            }

            return result;
        }

        public SnapshotInfo GetLatest()
        {
            var all = GetAll();
            return all.Count > 0 ? all[0] : null;
        }

        public IList<SnapshotItem> GetItems(long snapshotId)
        {
            var result = new List<SnapshotItem>();
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT Id, SnapshotId, ExecutablePath, IFNULL(Arguments, ''), IFNULL(DisplayName, ''), IFNULL(LaunchTarget, '')
FROM SnapshotItems
WHERE SnapshotId = @snapshotId
ORDER BY Id;";
                command.Parameters.AddWithValue("@snapshotId", snapshotId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new SnapshotItem
                        {
                            Id = reader.GetInt64(0),
                            SnapshotId = reader.GetInt64(1),
                            ExecutablePath = reader.GetString(2),
                            Arguments = reader.GetString(3),
                            DisplayName = reader.GetString(4),
                            LaunchTarget = reader.GetString(5)
                        });
                    }
                }
            }

            return result;
        }

        public long Insert(SnapshotInfo snapshot, IEnumerable<SnapshotItem> items)
        {
            using (var connection = Database.Open())
            using (var transaction = connection.BeginTransaction())
            {
                long snapshotId;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText =
                        "INSERT INTO Snapshots (Name, CreatedAt) VALUES (@name, @createdAt); SELECT last_insert_rowid();";
                    command.Parameters.AddWithValue("@name", snapshot.Name);
                    command.Parameters.AddWithValue("@createdAt", snapshot.CreatedAt.ToString("o", CultureInfo.InvariantCulture));
                    snapshotId = (long)command.ExecuteScalar();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO SnapshotItems (SnapshotId, ExecutablePath, Arguments, DisplayName, LaunchTarget)
VALUES (@snapshotId, @path, @args, @name, @launchTarget);";
                    var idParam = command.Parameters.Add("@snapshotId", System.Data.DbType.Int64);
                    var pathParam = command.Parameters.Add("@path", System.Data.DbType.String);
                    var argsParam = command.Parameters.Add("@args", System.Data.DbType.String);
                    var nameParam = command.Parameters.Add("@name", System.Data.DbType.String);
                    var launchParam = command.Parameters.Add("@launchTarget", System.Data.DbType.String);

                    foreach (var item in items)
                    {
                        idParam.Value = snapshotId;
                        pathParam.Value = item.ExecutablePath;
                        argsParam.Value = item.Arguments ?? string.Empty;
                        nameParam.Value = item.DisplayName ?? string.Empty;
                        launchParam.Value = item.LaunchTarget ?? string.Empty;
                        command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
                return snapshotId;
            }
        }

        public void Delete(long snapshotId)
        {
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM Snapshots WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", snapshotId);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 清空全部历史快照及其启动项。
        /// </summary>
        public void DeleteAll()
        {
            using (var connection = Database.Open())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM SnapshotItems; DELETE FROM Snapshots;";
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        /// <summary>
        /// 重命名快照。
        /// </summary>
        public void UpdateName(long snapshotId, string name)
        {
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE Snapshots SET Name = @name WHERE Id = @id;";
                command.Parameters.AddWithValue("@name", name);
                command.Parameters.AddWithValue("@id", snapshotId);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 向快照追加一个启动项，返回新条目（含数据库主键）。
        /// </summary>
        public SnapshotItem InsertItem(SnapshotItem item)
        {
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO SnapshotItems (SnapshotId, ExecutablePath, Arguments, DisplayName, LaunchTarget)
VALUES (@snapshotId, @path, @args, @name, @launchTarget);
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@snapshotId", item.SnapshotId);
                command.Parameters.AddWithValue("@path", item.ExecutablePath);
                command.Parameters.AddWithValue("@args", item.Arguments ?? string.Empty);
                command.Parameters.AddWithValue("@name", item.DisplayName ?? string.Empty);
                command.Parameters.AddWithValue("@launchTarget", item.LaunchTarget ?? string.Empty);
                item.Id = (long)command.ExecuteScalar();
            }

            return item;
        }

        /// <summary>
        /// 从快照中移除一个启动项。
        /// </summary>
        public void DeleteItem(long itemId)
        {
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM SnapshotItems WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", itemId);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 仅保留最近若干条快照，避免数据文件无限增长。
        /// </summary>
        public void Trim(int keepCount)
        {
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
DELETE FROM Snapshots
WHERE Id NOT IN (
    SELECT Id FROM Snapshots ORDER BY CreatedAt DESC, Id DESC LIMIT @keep
);";
                command.Parameters.AddWithValue("@keep", keepCount);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 指定快照是否仍存在（用于校验自动恢复）。
        /// </summary>
        public SnapshotInfo GetById(long snapshotId)
        {
            foreach (var snapshot in GetAll())
            {
                if (snapshot.Id == snapshotId)
                {
                    return snapshot;
                }
            }

            return null;
        }

        private static DateTime ParseDate(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
            {
                return parsed;
            }

            return DateTime.TryParse(value, out parsed) ? parsed : DateTime.MinValue;
        }
    }
}
