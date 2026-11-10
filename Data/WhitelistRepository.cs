using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using Keeper.Models;

namespace Keeper.Data
{
    /// <summary>
    /// 白名单数据访问：白名单中的程序在保存快照时不会被记录。
    /// </summary>
    public class WhitelistRepository
    {
        /// <summary>
        /// 读取全部白名单条目。
        /// </summary>
        public IList<WhitelistEntry> GetAll()
        {
            var result = new List<WhitelistEntry>();
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT Id, ExecutablePath, IFNULL(DisplayName, '')
FROM Whitelist
ORDER BY DisplayName COLLATE NOCASE, Id;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new WhitelistEntry
                        {
                            Id = reader.GetInt64(0),
                            ExecutablePath = reader.GetString(1),
                            DisplayName = reader.GetString(2)
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 加入白名单，已存在时返回 false。
        /// </summary>
        public bool Add(string path, string displayName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT OR IGNORE INTO Whitelist (ExecutablePath, DisplayName, CreatedAt)
VALUES (@path, @name, @createdAt);";
                command.Parameters.AddWithValue("@path", path);
                command.Parameters.AddWithValue("@name", displayName ?? string.Empty);
                command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                return command.ExecuteNonQuery() > 0;
            }
        }

        /// <summary>
        /// 从白名单中移除。
        /// </summary>
        public void Delete(long id)
        {
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM Whitelist WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", id);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 判断指定路径是否命中白名单（完整路径或文件名一致即视为命中）。
        /// </summary>
        public static bool Matches(WhitelistEntry entry, string path)
        {
            if (entry == null || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (string.Equals(entry.ExecutablePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var fileName = Path.GetFileName(entry.ExecutablePath);
            return !string.IsNullOrEmpty(fileName)
                && string.Equals(fileName, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
        }
    }
}
