using System.Text.Encodings.Web;
using System.Text.Json;
using Iderma.Core.Models;

namespace Iderma.Core.Services;

public sealed class ActiveSessionState
{
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public DateTime? StartedAt { get; set; }
    public string? ForcedLogoutUserId { get; set; }
    public long ForcedLogoutStamp { get; set; }
    public long CierreVisto { get; set; }
}

public sealed class SessionStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Mutex FileMutex = new(false, @"Local\Iderma.SessionFile");

    private readonly string _filePath;
    private readonly object _sync = new();
    private readonly FileSystemWatcher _watcher;
    private CancellationTokenSource? _debounce;
    private ActiveSessionState _state = new();
    private RegistroSesiones _registro = new();
    private DateTime _loadedUtc = DateTime.MinValue;
    private bool _nubeLista;
    private int _pidiendoNube;
    private int _operacion;
    private bool _disposed;

    public SessionStore()
    {
        Directory.CreateDirectory(UserStore.DataDirectory);
        _filePath = Path.Combine(UserStore.DataDirectory, "session.json");
        _watcher = new FileSystemWatcher(UserStore.DataDirectory, "session.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnDiskChanged;
        _watcher.Created += OnDiskChanged;
        _watcher.Renamed += OnDiskChanged;
        SesionesNube.Recibido += OnNube;
    }

    public event Action? Changed;

    public ActiveSessionState Snapshot()
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            return Clone(_state);
        }
    }

    public bool IsOnline(string userId) => EquiposDe(userId).Count > 0;

    public IReadOnlyList<string> EquiposDe(string userId)
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            AsegurarNubeUnlocked();
            var nombres = _registro.Equipos
                .Where(item => item.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Equipo)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!string.IsNullOrWhiteSpace(_state.UserId) &&
                _state.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase) &&
                !nombres.Contains(Environment.MachineName, StringComparer.OrdinalIgnoreCase))
            {
                nombres.Add(Environment.MachineName);
            }

            return nombres;
        }
    }

    public void SignIn(UserAccount user)
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            _state.UserId = user.Id;
            _state.Username = user.Username;
            _state.DisplayName = user.DisplayName;
            _state.StartedAt = DateTime.Now;
            if (string.Equals(_state.ForcedLogoutUserId, user.Id, StringComparison.OrdinalIgnoreCase))
            {
                _state.ForcedLogoutUserId = null;
            }

            _state.CierreVisto = CierreDe(user.Id);
            SaveUnlocked();
        }

        var fila = new FilaSesion
        {
            Equipo = Environment.MachineName,
            UserId = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            StartedAt = DateTime.Now
        };
        var operacion = Interlocked.Increment(ref _operacion);
        _ = Task.Run(() => PublicarEquipo(fila, operacion));
    }

    public void SignOut()
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            _state.UserId = null;
            _state.Username = null;
            _state.DisplayName = null;
            _state.StartedAt = null;
            _state.CierreVisto = 0;
            SaveUnlocked();
        }

        var operacion = Interlocked.Increment(ref _operacion);
        _ = Task.Run(() => QuitarEsteEquipo(operacion));
    }

    public bool ForceLogout(string userId)
    {
        var equipos = EquiposDe(userId);
        if (equipos.Count == 0)
        {
            return false;
        }

        var cierre = DateTime.UtcNow.Ticks;
        var registro = Esperar(SesionesNube.MutarAsync(actual =>
        {
            actual.Cierres[userId] = cierre;
            return true;
        }));
        lock (_sync)
        {
            _registro = registro;
            ReloadIfStaleUnlocked();
            if (!string.IsNullOrWhiteSpace(_state.UserId) &&
                _state.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase))
            {
                _state.ForcedLogoutUserId = userId;
                _state.ForcedLogoutStamp = cierre;
                SaveUnlocked();
            }
        }

        return true;
    }

    public bool ConsumeForcedLogout(string userId)
    {
        var quitar = false;
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            if (string.IsNullOrWhiteSpace(_state.ForcedLogoutUserId) ||
                !_state.ForcedLogoutUserId.Equals(userId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _state.ForcedLogoutUserId = null;
            _state.UserId = null;
            _state.Username = null;
            _state.DisplayName = null;
            _state.StartedAt = null;
            _state.CierreVisto = 0;
            SaveUnlocked();
            quitar = true;
        }

        if (quitar)
        {
            var operacion = Interlocked.Increment(ref _operacion);
            _ = Task.Run(() => QuitarEsteEquipo(operacion));
        }

        return quitar;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SesionesNube.Recibido -= OnNube;
        _debounce?.Cancel();
        _debounce?.Dispose();
        _watcher.Dispose();
    }

    private void OnDiskChanged(object sender, FileSystemEventArgs e)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _debounce, cts);
        previous?.Cancel();
        previous?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cts.Token);
                var changed = false;
                lock (_sync)
                {
                    var before = _loadedUtc;
                    ReloadIfStaleUnlocked();
                    changed = _loadedUtc != before;
                }

                if (changed)
                {
                    Changed?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void ReloadIfStaleUnlocked()
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
            var json = File.ReadAllText(_filePath);
            _state = JsonSerializer.Deserialize<ActiveSessionState>(json, JsonOptions) ?? new ActiveSessionState();
            _loadedUtc = File.GetLastWriteTimeUtc(_filePath);
        });
    }

    private void SaveUnlocked()
    {
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

    private static ActiveSessionState Clone(ActiveSessionState state) => new()
    {
        UserId = state.UserId,
        Username = state.Username,
        DisplayName = state.DisplayName,
        StartedAt = state.StartedAt,
        ForcedLogoutUserId = state.ForcedLogoutUserId,
        ForcedLogoutStamp = state.ForcedLogoutStamp,
        CierreVisto = state.CierreVisto
    };

    private long CierreDe(string userId) =>
        _registro.Cierres.TryGetValue(userId, out var cierre) ? cierre : 0;

    private void AsegurarNubeUnlocked()
    {
        if (_nubeLista || Volatile.Read(ref _pidiendoNube) == 1)
        {
            return;
        }

        if (Interlocked.Exchange(ref _pidiendoNube, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var registro = Esperar(SesionesNube.LeerAsync());
                lock (_sync)
                {
                    _nubeLista = true;
                    if (registro is not null)
                    {
                        _registro = registro;
                        AplicarCierreSiCorresponde();
                    }
                }

                SesionesNube.AsegurarEscucha();
                Changed?.Invoke();
            }
            catch
            {
                Interlocked.Exchange(ref _pidiendoNube, 0);
            }
        });
    }

    private void OnNube(RegistroSesiones registro)
    {
        if (_disposed)
        {
            return;
        }

        var changed = false;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _registro = registro;
            _nubeLista = true;
            changed = true;
            AplicarCierreSiCorresponde();
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private bool AplicarCierreSiCorresponde()
    {
        if (string.IsNullOrWhiteSpace(_state.UserId))
        {
            return false;
        }

        var cierre = CierreDe(_state.UserId);
        if (cierre <= _state.CierreVisto ||
            string.Equals(_state.ForcedLogoutUserId, _state.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _state.ForcedLogoutUserId = _state.UserId;
        _state.ForcedLogoutStamp = cierre;
        SaveUnlocked();
        return true;
    }

    private void QuitarEsteEquipo(int operacion)
    {
        try
        {
            var registro = Esperar(SesionesNube.MutarAsync(actual =>
            {
                if (Volatile.Read(ref _operacion) != operacion)
                {
                    return false;
                }

                var antes = actual.Equipos.Count;
                actual.Equipos.RemoveAll(item => item.Equipo.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
                return actual.Equipos.Count != antes;
            }));
            lock (_sync)
            {
                if (Volatile.Read(ref _operacion) == operacion)
                {
                    _registro = registro;
                }
            }
        }
        catch
        {
        }
    }

    private void PublicarEquipo(FilaSesion fila, int operacion)
    {
        try
        {
            SesionesNube.AsegurarEscucha();
            var registro = Esperar(SesionesNube.MutarAsync(actual =>
            {
                if (Volatile.Read(ref _operacion) != operacion)
                {
                    return false;
                }

                actual.Equipos.RemoveAll(item => item.Equipo.Equals(fila.Equipo, StringComparison.OrdinalIgnoreCase));
                actual.Equipos.Add(fila);
                return true;
            }));
            lock (_sync)
            {
                if (Volatile.Read(ref _operacion) != operacion)
                {
                    return;
                }

                _registro = registro;
                _nubeLista = true;
                AplicarCierreSiCorresponde();
            }
        }
        catch
        {
        }
    }

    private static T Esperar<T>(Task<T> tarea)
    {
        var trabajo = Task.Run(() => tarea);
        if (!trabajo.Wait(TimeSpan.FromSeconds(20)))
        {
            throw new TimeoutException("Firebase no respondió a tiempo.");
        }

        return trabajo.GetAwaiter().GetResult();
    }
}
