using System.Text.Encodings.Web;
using System.Text.Json;
using Iderma.Core.Models;

namespace Iderma.Core.Services;

public sealed class LoginAttempt
{
    public string Username { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public sealed class SecurityFile
{
    public SecurityPolicy Policy { get; set; } = new();
    public List<LoginAttempt> Attempts { get; set; } = [];
    public List<SecurityEvent> Events { get; set; } = [];
    public Dictionary<string, DateTime> LastSignIn { get; set; } = new();
    public Dictionary<string, DateTime> PreviousSignIn { get; set; } = new();
    public long Rev { get; set; }
}

public sealed class SecurityCenter
{
    private const int MaxEvents = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Mutex FileMutex = new(false, @"Local\Iderma.SecurityFile");

    private readonly string _filePath;
    private readonly object _sync = new();
    private SecurityFile _state = new();
    private DateTime _loadedUtc = DateTime.MinValue;
    private DateTime _nubeUtc = DateTime.MinValue;
    private int _trayendo;

    public SecurityCenter()
    {
        Directory.CreateDirectory(UserStore.DataDirectory);
        _filePath = Path.Combine(UserStore.DataDirectory, "security.json");
    }

    public SecurityPolicy GetPolicy()
    {
        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                return Copy(_state.Policy);
            }
        }
        catch
        {
            return new SecurityPolicy();
        }
    }

    public void SavePolicy(SecurityPolicy policy, string username)
    {
        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                var next = Normalize(policy);
                var current = _state.Policy ?? new SecurityPolicy();
                if (Same(current, next))
                {
                    return;
                }

                _state.Policy = next;
                if (!next.LockoutEnabled)
                {
                    _state.Attempts.Clear();
                }

                AppendUnlocked(SecurityEventKind.PolicyChanged, username, Describe(next));
                SaveUnlocked();
            }
        }
        catch
        {
        }
    }

    public string? LockoutMessage(string username)
    {
        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                if (_state.Policy is not { LockoutEnabled: true })
                {
                    return null;
                }

                return ReadLockoutUnlocked(username);
            }
        }
        catch
        {
            return null;
        }
    }

    public string? RegisterFailure(string username)
    {
        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                if (string.IsNullOrWhiteSpace(username))
                {
                    return null;
                }

                if (_state.Policy is { LockoutEnabled: true })
                {
                    var blocked = ReadLockoutUnlocked(username);
                    if (blocked is not null)
                    {
                        AppendUnlocked(SecurityEventKind.SignInFailed, username, "Intento durante el bloqueo temporal.");
                        SaveUnlocked();
                        return blocked;
                    }
                }

                AppendUnlocked(SecurityEventKind.SignInFailed, username, "Intento rechazado.");

                if (_state.Policy is not { LockoutEnabled: true })
                {
                    SaveUnlocked();
                    return null;
                }

                var attempt = FindAttemptUnlocked(username);
                if (attempt is null)
                {
                    attempt = new LoginAttempt { Username = Key(username) };
                    _state.Attempts.Add(attempt);
                }

                attempt.Count++;
                var max = Math.Clamp(_state.Policy.MaxFailedAttempts, 3, 20);
                if (attempt.Count < max)
                {
                    SaveUnlocked();
                    return null;
                }

                var minutes = Math.Clamp(_state.Policy.LockoutMinutes, 1, 120);
                attempt.LockedUntil = DateTime.Now.AddMinutes(minutes);
                AppendUnlocked(SecurityEventKind.Lockout, username, "Acceso bloqueado temporalmente.");
                SaveUnlocked();
                return ReadLockoutUnlocked(username);
            }
        }
        catch
        {
            return null;
        }
    }

    public void RegisterSuccess(string username, string? loginCode = null)
    {
        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                if (string.IsNullOrWhiteSpace(username))
                {
                    return;
                }

                var key = Key(username);
                if (_state.LastSignIn.TryGetValue(key, out var last))
                {
                    _state.PreviousSignIn[key] = last;
                }

                _state.LastSignIn[key] = DateTime.Now;
                _state.Attempts.RemoveAll(item => Key(item.Username) == key);
                var detail = string.IsNullOrWhiteSpace(loginCode)
                    ? "Inicio de sesión correcto."
                    : $"Inicio de sesión correcto. Código {loginCode}.";
                AppendUnlocked(SecurityEventKind.SignIn, username, detail);
                SaveUnlocked();
            }
        }
        catch
        {
        }
    }

    public void NoteUnlocked(string username)
    {
        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                if (!string.IsNullOrWhiteSpace(username))
                {
                    var key = Key(username);
                    _state.Attempts.RemoveAll(item => Key(item.Username) == key);
                }

                AppendUnlocked(SecurityEventKind.SessionUnlocked, username, "Sesión reanudada.");
                SaveUnlocked();
            }
        }
        catch
        {
        }
    }

    public void Record(string kind, string username, string detail)
    {
        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                AppendUnlocked(kind, username, detail);
                SaveUnlocked();
            }
        }
        catch
        {
        }
    }

    public IReadOnlyList<SecurityEvent> Recent(string username, bool allUsers)
    {
        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                IEnumerable<SecurityEvent> events = _state.Events;
                if (!allUsers)
                {
                    var key = Key(username);
                    events = events.Where(item => Key(item.Username) == key);
                }

                return events
                    .OrderByDescending(item => item.At)
                    .Take(40)
                    .Select(item => new SecurityEvent
                    {
                        At = item.At,
                        Kind = item.Kind,
                        Username = item.Username,
                        Detail = item.Detail
                    })
                    .ToList();
            }
        }
        catch
        {
            return [];
        }
    }

    public DateTime? PreviousSignIn(string username)
    {
        try
        {
            lock (_sync)
            {
                ReloadIfStale();
                return _state.PreviousSignIn.TryGetValue(Key(username), out var at) ? at : null;
            }
        }
        catch
        {
            return null;
        }
    }

    private string? ReadLockoutUnlocked(string username)
    {
        var attempt = FindAttemptUnlocked(username);
        if (attempt?.LockedUntil is not DateTime until)
        {
            return null;
        }

        var remaining = until - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            attempt.Count = 0;
            attempt.LockedUntil = null;
            return null;
        }

        var wait = remaining.TotalMinutes >= 1
            ? $"{Math.Ceiling(remaining.TotalMinutes):0} min"
            : "menos de un minuto";
        return $"Demasiados intentos fallidos. Vuelve a intentarlo en {wait}.";
    }

    private LoginAttempt? FindAttemptUnlocked(string username)
    {
        var key = Key(username);
        return _state.Attempts.FirstOrDefault(item => Key(item.Username) == key);
    }

    private void AppendUnlocked(string kind, string username, string detail)
    {
        _state.Events.Add(new SecurityEvent
        {
            At = DateTime.Now,
            Kind = kind,
            Username = username?.Trim() ?? string.Empty,
            Detail = detail
        });

        if (_state.Events.Count > MaxEvents)
        {
            _state.Events.RemoveRange(0, _state.Events.Count - MaxEvents);
        }
    }

    private void ReloadIfStale()
    {
        if (File.Exists(_filePath))
        {
            var stamp = File.GetLastWriteTimeUtc(_filePath);
            if (stamp > _loadedUtc)
            {
                var plano = false;
                WithFileLock(() =>
                {
                    try
                    {
                        var json = File.ReadAllText(_filePath);
                        if (EsSobre(json))
                        {
                            var sobre = JsonSerializer.Deserialize<SobreSeguridad>(json, JsonOptions);
                            if (sobre is not null && !string.IsNullOrWhiteSpace(sobre.C))
                            {
                                var claro = CifradoCuentas.Abrir(sobre.N, sobre.C, sobre.V <= 0 ? 1 : sobre.V);
                                var parsed = JsonSerializer.Deserialize<SecurityFile>(claro, JsonOptions);
                                if (parsed is not null)
                                {
                                    _state = parsed;
                                }
                            }
                        }
                        else
                        {
                            var parsed = JsonSerializer.Deserialize<SecurityFile>(json, JsonOptions);
                            if (parsed is not null)
                            {
                                _state = parsed;
                                plano = true;
                            }
                        }
                    }
                    catch (JsonException)
                    {
                    }
                    catch (System.Security.Cryptography.CryptographicException)
                    {
                    }

                    NormalizarEstado();
                    _loadedUtc = File.GetLastWriteTimeUtc(_filePath);
                });

                if (plano)
                {
                    SaveUnlocked();
                }
            }
        }

        PedirNube();
    }

    private void SaveUnlocked()
    {
        NormalizarEstado();
        _state.Rev++;
        var json = JsonSerializer.Serialize(_state, JsonOptions);
        var (nonce, cifrado) = CifradoCuentas.Sellar(json);
        var rev = _state.Rev;
        WithFileLock(() =>
        {
            var sobre = new SobreSeguridad
            {
                V = CifradoCuentas.VersionActual,
                Rev = rev,
                N = nonce,
                C = cifrado
            };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(sobre, JsonOptions));
            _loadedUtc = File.GetLastWriteTimeUtc(_filePath);
        });

        _ = PublicarEnSegundoPlanoAsync(rev - 1, rev, nonce, cifrado);
    }

    private void PedirNube()
    {
        if (DateTime.UtcNow - _nubeUtc < TimeSpan.FromSeconds(3))
        {
            return;
        }

        if (Interlocked.Exchange(ref _trayendo, 1) == 1)
        {
            return;
        }

        _nubeUtc = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                await TraerNubeAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _trayendo, 0);
            }
        });
    }

    private async Task PublicarEnSegundoPlanoAsync(long esperada, long nueva, string nonce, string cifrado)
    {
        try
        {
            var guardado = await CuentasNube.GuardarDocumentoAsync(
                "seguridad",
                esperada,
                nueva,
                CifradoCuentas.VersionActual,
                nonce,
                cifrado);
            if (guardado)
            {
                return;
            }

            lock (_sync)
            {
                _nubeUtc = DateTime.MinValue;
            }

            await TraerNubeAsync();
        }
        catch
        {
        }
    }

    private async Task TraerNubeAsync()
    {
        try
        {
            var lectura = await CuentasNube.LeerDocumentoAsync("seguridad");
            if (!lectura.EnLinea || lectura.Sobre is not { } sobre)
            {
                return;
            }

            lock (_sync)
            {
                if (sobre.Rev <= _state.Rev)
                {
                    return;
                }

                var claro = CifradoCuentas.Abrir(sobre.Nonce, sobre.Cifrado, sobre.Version <= 0 ? 1 : sobre.Version);
                var parsed = JsonSerializer.Deserialize<SecurityFile>(claro, JsonOptions);
                if (parsed is null)
                {
                    return;
                }

                _state = parsed;
                NormalizarEstado();
                _state.Rev = Math.Max(_state.Rev, sobre.Rev);
                WithFileLock(() =>
                {
                    var envelope = new SobreSeguridad
                    {
                        V = sobre.Version <= 0 ? 1 : sobre.Version,
                        Rev = sobre.Rev,
                        N = sobre.Nonce,
                        C = sobre.Cifrado
                    };
                    File.WriteAllText(_filePath, JsonSerializer.Serialize(envelope, JsonOptions));
                    _loadedUtc = File.GetLastWriteTimeUtc(_filePath);
                });
            }
        }
        catch
        {
        }
    }

    private static bool EsSobre(string json)
    {
        var texto = json.TrimStart();
        return texto.StartsWith('{') && texto.Contains("\"c\"", StringComparison.Ordinal) && texto.Contains("\"n\"", StringComparison.Ordinal);
    }

    private void NormalizarEstado()
    {
        _state.Policy ??= new SecurityPolicy();
        _state.Attempts ??= [];
        _state.Events ??= [];
        _state.LastSignIn ??= new Dictionary<string, DateTime>();
        _state.PreviousSignIn ??= new Dictionary<string, DateTime>();
    }

    private sealed class SobreSeguridad
    {
        public int V { get; set; } = CifradoCuentas.VersionActual;
        public long Rev { get; set; }
        public string N { get; set; } = string.Empty;
        public string C { get; set; } = string.Empty;
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

    private static SecurityPolicy Normalize(SecurityPolicy policy)
    {
        var idle = policy.IdleLockMinutes <= 0 ? 0 : Math.Clamp(policy.IdleLockMinutes, 1, 240);
        return new SecurityPolicy
        {
            IdleLockMinutes = idle,
            LockoutEnabled = policy.LockoutEnabled,
            MaxFailedAttempts = Math.Clamp(policy.MaxFailedAttempts <= 0 ? 5 : policy.MaxFailedAttempts, 3, 20),
            LockoutMinutes = Math.Clamp(policy.LockoutMinutes <= 0 ? 5 : policy.LockoutMinutes, 1, 120)
        };
    }

    private static bool Same(SecurityPolicy left, SecurityPolicy right) =>
        left.IdleLockMinutes == right.IdleLockMinutes &&
        left.LockoutEnabled == right.LockoutEnabled &&
        left.MaxFailedAttempts == right.MaxFailedAttempts &&
        left.LockoutMinutes == right.LockoutMinutes;

    private static SecurityPolicy Copy(SecurityPolicy? policy) => policy is null
        ? new SecurityPolicy()
        : new SecurityPolicy
        {
            IdleLockMinutes = policy.IdleLockMinutes,
            LockoutEnabled = policy.LockoutEnabled,
            MaxFailedAttempts = policy.MaxFailedAttempts,
            LockoutMinutes = policy.LockoutMinutes
        };

    private static string Describe(SecurityPolicy policy)
    {
        var idle = policy.IdleLockMinutes > 0
            ? $"Bloqueo por inactividad: {policy.IdleLockMinutes} min."
            : "Bloqueo por inactividad desactivado.";
        var lockout = policy.LockoutEnabled
            ? $" Bloqueo por intentos: {policy.MaxFailedAttempts} intentos, {policy.LockoutMinutes} min."
            : " Bloqueo por intentos desactivado.";
        return idle + lockout;
    }

    private static string Key(string username) => username.Trim().ToLowerInvariant();
}
