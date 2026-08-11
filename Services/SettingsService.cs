using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Anagnostes.Services;

public sealed class SettingsService
{
    private const string DefaultVoice = "af_heart";
    private readonly string _connectionString;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Anagnostes");
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "settings.db")
        }.ToString();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    public string Voice => Get("voice") ?? DefaultVoice;
    public bool ShareAnonymousLogs => bool.TryParse(Get("shareAnonymousLogs"), out var value) && value;
    public bool AutoSpeak => !bool.TryParse(Get("autoSpeak"), out var value) || value;

    public void SetVoice(string voice) => Set("voice", voice);
    public void SetShareAnonymousLogs(bool enabled) => Set("shareAnonymousLogs", enabled.ToString());
    public void SetAutoSpeak(bool enabled) => Set("autoSpeak", enabled.ToString());

    private string? Get(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private void Set(string key, string value)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($key, $value) " +
                              "ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
        _logger.LogInformation("Setting changed. {SettingKey}", key);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
