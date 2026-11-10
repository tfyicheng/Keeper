using System;
using System.Globalization;

namespace Keeper.Data
{
    /// <summary>
    /// 应用配置的键值存储。
    /// </summary>
    public class SettingsRepository
    {
        public string Get(string key, string defaultValue = null)
        {
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Value FROM Settings WHERE Key = @key;";
                command.Parameters.AddWithValue("@key", key);
                var value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                {
                    return defaultValue;
                }

                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        public void Set(string key, string value)
        {
            using (var connection = Database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT OR REPLACE INTO Settings (Key, Value) VALUES (@key, @value);";
                command.Parameters.AddWithValue("@key", key);
                command.Parameters.AddWithValue("@value", value ?? string.Empty);
                command.ExecuteNonQuery();
            }
        }

        public bool GetBool(string key, bool defaultValue)
        {
            var raw = Get(key);
            if (string.IsNullOrEmpty(raw))
            {
                return defaultValue;
            }

            bool parsed;
            return bool.TryParse(raw, out parsed) ? parsed : defaultValue;
        }

        public void SetBool(string key, bool value)
        {
            Set(key, value ? "true" : "false");
        }
    }
}
