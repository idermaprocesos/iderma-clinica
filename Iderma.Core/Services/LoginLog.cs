using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Iderma.Core;
using Iderma.Core.Models;

namespace Iderma.Core.Services;

public sealed class LoginLogFile
{
    public List<LoginRecord> Records { get; set; } = [];
}

public sealed class LoginLog
{
    private const int MaxRecords = 2000;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Mutex FileMutex = new(false, @"Local\Iderma.LoginLogFile");

    private readonly string _filePath;
    private readonly object _sync = new();
    private LoginLogFile _state = new();
    private DateTime _loadedUtc = DateTime.MinValue;

    public LoginLog()
    {
        Directory.CreateDirectory(UserStore.DataDirectory);
        _filePath = Path.Combine(UserStore.DataDirectory, "logins.json");
    }

    public LoginRecord Record(UserAccount user)
    {
        var record = new LoginRecord
        {
            UserId = user.Id,
            Username = user.Username?.Trim() ?? string.Empty,
            DisplayName = user.DisplayName?.Trim() ?? string.Empty,
            Role = user.Role?.Trim() ?? string.Empty,
            At = DateTime.Now,
            MachineName = Environment.MachineName,
            WindowsAccount = WindowsAccount(),
            OperatingSystem = RuntimeInformation.OSDescription,
            AppVersion = AppInfo.Version,
            Culture = CultureInfo.CurrentUICulture.Name
        };

        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                record.Code = NewCodeUnlocked();
                _state.Records.Add(Clone(record));
                if (_state.Records.Count > MaxRecords)
                {
                    _state.Records.RemoveRange(0, _state.Records.Count - MaxRecords);
                }

                SaveUnlocked();
            }
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(record.Code))
            {
                record.Code = CreateCode();
            }
        }

        return record;
    }

    public IReadOnlyList<LoginRecord> List(string username, bool allUsers)
    {
        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                IEnumerable<LoginRecord> records = _state.Records;
                if (!allUsers)
                {
                    var key = Key(username);
                    records = records.Where(item => Key(item.Username) == key);
                }

                return records
                    .OrderByDescending(item => item.At)
                    .Select(Clone)
                    .ToList();
            }
        }
        catch
        {
            return [];
        }
    }

    public string ToCsv(IEnumerable<LoginRecord> records)
    {
        var builder = new StringBuilder();
        builder.Append('\uFEFF');
        builder.AppendLine("Codigo;IdUsuario;Usuario;Nombre;Rol;Fecha;Equipo;CuentaWindows;Sistema;Version;Idioma");
        foreach (var item in records.OrderBy(record => record.At))
        {
            builder
                .Append(Cell(item.Code)).Append(';')
                .Append(Cell(item.UserId)).Append(';')
                .Append(Cell(item.Username)).Append(';')
                .Append(Cell(item.DisplayName)).Append(';')
                .Append(Cell(item.Role)).Append(';')
                .Append(Cell(item.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(';')
                .Append(Cell(item.MachineName)).Append(';')
                .Append(Cell(item.WindowsAccount)).Append(';')
                .Append(Cell(item.OperatingSystem)).Append(';')
                .Append(Cell(item.AppVersion)).Append(';')
                .Append(Cell(item.Culture))
                .AppendLine();
        }

        return builder.ToString();
    }

    private string NewCodeUnlocked()
    {
        var existing = new HashSet<string>(
            _state.Records.Select(item => item.Code),
            StringComparer.OrdinalIgnoreCase);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = CreateCode();
            if (existing.Add(code))
            {
                return code;
            }
        }

        return CreateCode();
    }

    private static string CreateCode()
    {
        Span<char> buffer = stackalloc char[14];
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        var index = 0;
        for (var i = 0; i < 12; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                buffer[index++] = '-';
            }

            buffer[index++] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(buffer);
    }

    private void ReloadIfStale()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var stamp = File.GetLastWriteTimeUtc(_filePath);
        if (stamp <= _loadedUtc)
        {
            return;
        }

        WithFileLock(() =>
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var parsed = JsonSerializer.Deserialize<LoginLogFile>(json, JsonOptions);
                if (parsed is not null)
                {
                    _state = parsed;
                }
            }
            catch (JsonException)
            {
            }

            _state.Records ??= [];
            _loadedUtc = File.GetLastWriteTimeUtc(_filePath);
        });
    }

    private void SaveUnlocked()
    {
        _state.Records ??= [];
        WithFileLock(() =>
        {
            var json = JsonSerializer.Serialize(_state, JsonOptions);
            File.WriteAllText(_filePath, json);
            _loadedUtc = File.GetLastWriteTimeUtc(_filePath);
        });
    }

    private static void WithFileLock(Action action)
    {
        try
        {
            FileMutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
        }

        try
        {
            action();
        }
        finally
        {
            FileMutex.ReleaseMutex();
        }
    }

    private static LoginRecord Clone(LoginRecord record) => new()
    {
        Code = record.Code,
        UserId = record.UserId,
        Username = record.Username,
        DisplayName = record.DisplayName,
        Role = record.Role,
        At = record.At,
        MachineName = record.MachineName,
        WindowsAccount = record.WindowsAccount,
        OperatingSystem = record.OperatingSystem,
        AppVersion = record.AppVersion,
        Culture = record.Culture
    };

    private static string WindowsAccount()
    {
        var name = Environment.UserName;
        var domain = Environment.UserDomainName;
        return string.IsNullOrWhiteSpace(domain) ? name : $"{domain}\\{name}";
    }

    private static string Key(string username) => username.Trim().ToLowerInvariant();

    private static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Contains(';') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
        {
            return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return text;
    }
}
