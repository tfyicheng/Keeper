using System;
using System.Data.SQLite;
using Keeper.Helpers;

namespace Keeper.Data
{
    /// <summary>
    /// SQLite 数据库入口：负责连接与建表。数据文件固定放在程序所在目录。
    /// </summary>
    public static class Database
    {
        private static string _connectionString;
        private static bool _initialized;

        public static string DatabasePath
        {
            get { return AppPaths.DatabasePath; }
        }

        /// <summary>
        /// 初始化数据库文件与表结构（幂等）。
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = AppPaths.DatabasePath,
                ForeignKeys = true,
                FailIfMissing = false,
                Pooling = true
            };

            _connectionString = builder.ToString();

            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS Settings (
    Key   TEXT NOT NULL PRIMARY KEY,
    Value TEXT
);

CREATE TABLE IF NOT EXISTS Snapshots (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name      TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS SnapshotItems (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    SnapshotId     INTEGER NOT NULL,
    ExecutablePath TEXT NOT NULL,
    Arguments      TEXT,
    DisplayName    TEXT,
    FOREIGN KEY (SnapshotId) REFERENCES Snapshots (Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_SnapshotItems_SnapshotId ON SnapshotItems (SnapshotId);

CREATE TABLE IF NOT EXISTS Whitelist (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    ExecutablePath TEXT NOT NULL UNIQUE,
    DisplayName    TEXT,
    CreatedAt      TEXT NOT NULL
);
";
                command.ExecuteNonQuery();

                // 旧版本数据库没有 LaunchTarget 列（打包应用的启动目标），这里做一次迁移
                EnsureColumn(connection, "SnapshotItems", "LaunchTarget", "TEXT");
            }

            _initialized = true;
        }

        /// <summary>
        /// 表缺少指定列时补上，用于兼容旧数据库文件。
        /// </summary>
        private static void EnsureColumn(SQLiteConnection connection, string table, string column, string type)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(" + table + ");";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "ALTER TABLE " + table + " ADD COLUMN " + column + " " + type + ";";
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 打开一个已连接的数据库连接。
        /// </summary>
        public static SQLiteConnection Open()
        {
            if (string.IsNullOrEmpty(_connectionString))
            {
                throw new InvalidOperationException("数据库尚未初始化。");
            }

            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}
