using System.Text.Encodings.Web;
using System.Text.Json;
using Iderma.Core.Catalog;
using Iderma.Core.Models;

namespace Iderma.Core.Services;

public sealed class UserStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Mutex FileMutex = new(false, @"Local\Iderma.UsersFile");

    private readonly string _filePath;
    private readonly object _sync = new();
    private readonly FileSystemWatcher _watcher;
    private CancellationTokenSource? _debounce;
    private List<UserAccount> _users = [];
    private long _rev;
    private DateTime _loadedUtc = DateTime.MinValue;
    private int _formato = CifradoCuentas.VersionActual;
    private bool _initialized;
    private int _nubeProgramada;
    private bool _disposed;

    public UserStore() : this(DefaultFilePath)
    {
    }

    public UserStore(string filePath)
    {
        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        _watcher = CreateWatcher();
        CuentasNube.Recibido += OnNube;
    }

    public event Action? Changed;

    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData",
            "Local",
            "Iderma");

    public static string DefaultFilePath => Path.Combine(DataDirectory, "users.json");

    public IReadOnlyList<UserAccount> GetAll()
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            return _users
                .Select(Clone)
                .OrderBy(user => user.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public UserAccount? FindByUsername(string username)
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            var user = _users.FirstOrDefault(item =>
                item.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase));
            return user is null ? null : Clone(user);
        }
    }

    public UserAccount? FindById(string id)
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            var user = _users.FirstOrDefault(item => item.Id == id);
            return user is null ? null : Clone(user);
        }
    }

    public UserAccount? Authenticate(string username, string password)
    {
        VigilanciaAcceso.Comprobar();
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            var user = _users.FirstOrDefault(item =>
                item.IsActive &&
                item.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase));

            if (user is null || !PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt, user.PasswordIterations))
            {
                return null;
            }

            return Clone(user);
        }
    }

    public UserAccount Create(
        string username,
        string displayName,
        string password,
        string role,
        bool isActive,
        bool canOpenSettings,
        bool canExportLogins,
        bool notifyLogistica,
        bool notifyClinica,
        bool notifyBaseDeDatos,
        IEnumerable<string> appIds,
        bool mustChangePassword = false)
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            ValidateUsername(username, excludeId: null);
            ValidatePassword(password);

            var (hash, salt, iterations) = PasswordHasher.Hash(password);
            var user = new UserAccount
            {
                Username = username.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? username.Trim() : displayName.Trim(),
                Role = string.IsNullOrWhiteSpace(role) ? "Usuario" : role.Trim(),
                PasswordHash = hash,
                PasswordSalt = salt,
                PasswordIterations = iterations,
                MustChangePassword = mustChangePassword,
                IsActive = isActive,
                CanOpenSettings = canOpenSettings,
                CanExportLogins = canExportLogins,
                NotifyLogistica = notifyLogistica,
                NotifyClinica = notifyClinica,
                NotifyBaseDeDatos = notifyBaseDeDatos,
                AppIds = NormalizeAppIds(appIds)
            };

            _users.Add(user);
            SaveUnlocked();
            return Clone(user);
        }
    }

    public UserAccount Update(
        string id,
        string username,
        string displayName,
        string? newPassword,
        string role,
        bool isActive,
        bool canOpenSettings,
        bool canExportLogins,
        bool notifyLogistica,
        bool notifyClinica,
        bool notifyBaseDeDatos,
        IEnumerable<string> appIds)
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            var user = _users.FirstOrDefault(item => item.Id == id)
                ?? throw new InvalidOperationException("El usuario no existe.");

            ValidateUsername(username, excludeId: id);

            user.Username = username.Trim();
            user.DisplayName = string.IsNullOrWhiteSpace(displayName) ? username.Trim() : displayName.Trim();
            user.Role = string.IsNullOrWhiteSpace(role) ? "Usuario" : role.Trim();
            user.IsActive = isActive;
            user.CanOpenSettings = canOpenSettings;
            user.CanExportLogins = canExportLogins;
            user.NotifyLogistica = notifyLogistica;
            user.NotifyClinica = notifyClinica;
            user.NotifyBaseDeDatos = notifyBaseDeDatos;
            user.AppIds = NormalizeAppIds(appIds);

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                ValidatePassword(newPassword);
                var (hash, salt, iterations) = PasswordHasher.Hash(newPassword);
                user.PasswordHash = hash;
                user.PasswordSalt = salt;
                user.PasswordIterations = iterations;
                user.MustChangePassword = false;
                BorrarAvisoClaveInicial();
            }

            SaveUnlocked();
            return Clone(user);
        }
    }

    public UserAccount ChangePassword(string id, string currentPassword, string newPassword)
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            var user = _users.FirstOrDefault(item => item.Id == id)
                ?? throw new InvalidOperationException("El usuario no existe.");

            if (!PasswordHasher.Verify(currentPassword, user.PasswordHash, user.PasswordSalt, user.PasswordIterations))
            {
                throw new InvalidOperationException("La contraseña actual no es correcta.");
            }

            ValidatePassword(newPassword);
            AplicarClave(user, newPassword);
            try
            {
                SaveUnlocked();
            }
            catch (InvalidOperationException)
            {
                user = _users.FirstOrDefault(item => item.Id == id)
                    ?? throw new InvalidOperationException("El usuario no existe.");
                AplicarClave(user, newPassword);
                SaveUnlocked();
            }

            BorrarAvisoClaveInicial();
            return Clone(user);
        }
    }

    public UserAccount SetPhoto(string id, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new InvalidOperationException("No se encontró la imagen.");
        }

        var extension = Path.GetExtension(sourcePath);
        if (!AllowedPhotoExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Usa una imagen JPG, PNG o WebP.");
        }

        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            var user = _users.FirstOrDefault(item => item.Id == id)
                ?? throw new InvalidOperationException("El usuario no existe.");

            var destination = Path.Combine(PhotosDirectory(), user.Id + extension.ToLowerInvariant());
            var temporary = destination + ".tmp";
            File.Copy(sourcePath, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: true);

            var previous = user.PhotoPath;
            user.PhotoPath = destination;
            SaveUnlocked();
            TryDeletePhoto(previous, destination);
            return Clone(user);
        }
    }

    public UserAccount ClearPhoto(string id)
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            var user = _users.FirstOrDefault(item => item.Id == id)
                ?? throw new InvalidOperationException("El usuario no existe.");

            var previous = user.PhotoPath;
            user.PhotoPath = string.Empty;
            SaveUnlocked();
            TryDeletePhoto(previous, keep: null);
            return Clone(user);
        }
    }

    public void Delete(string id)
    {
        lock (_sync)
        {
            ReloadIfStaleUnlocked();
            var user = _users.FirstOrDefault(item => item.Id == id)
                ?? throw new InvalidOperationException("El usuario no existe.");

            var adminCount = _users.Count(item =>
                item.Role.Equals("Administrador", StringComparison.OrdinalIgnoreCase));

            if (user.Role.Equals("Administrador", StringComparison.OrdinalIgnoreCase) && adminCount <= 1)
            {
                throw new InvalidOperationException("No se puede eliminar el último administrador.");
            }

            var photo = user.PhotoPath;
            _users.Remove(user);
            SaveUnlocked();
            TryDeletePhoto(photo, keep: null);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CuentasNube.Recibido -= OnNube;
        _debounce?.Cancel();
        _debounce?.Dispose();
        _watcher.Dispose();
    }

    private void EnsureInitializedUnlocked()
    {
        if (_initialized)
        {
            return;
        }

        if (File.Exists(_filePath))
        {
            ReadUnlocked();
        }

        _initialized = true;
        ProgramarNube();
    }

    private void ProgramarNube()
    {
        if (Interlocked.Exchange(ref _nubeProgramada, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400);
                CompletarNube();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        });
    }

    private void CompletarNube()
    {
        var nube = LeerNube();
        var changed = false;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            var subir = false;
            if (_users.Count == 0 && !File.Exists(_filePath))
            {
                if (nube.EnLinea && nube.Sobre is not null)
                {
                    AplicarRemoto(nube.Sobre.Value);
                    changed = true;
                }
                else
                {
                    SeedUnlocked();
                    subir = true;
                }
            }
            else if (nube.EnLinea && nube.Sobre is not null && nube.Sobre.Value.Rev > _rev)
            {
                AplicarRemoto(nube.Sobre.Value);
                changed = true;
            }
            else if (nube.EnLinea && (nube.Sobre is null || nube.Sobre.Value.Rev < _rev))
            {
                subir = _users.Count > 0;
            }

            if (_users.Count == 0 && _rev == 0 && (!nube.EnLinea || nube.Sobre is null))
            {
                SeedUnlocked();
                subir = true;
            }

            if (RestoreMissingPhotos()
                | ConcederCuentasSiFalta()
                | MarcarClavesConocidas()
                | _formato < CifradoCuentas.VersionActual)
            {
                subir = true;
            }

            if (subir)
            {
                SaveUnlocked(confirmarNube: false);
                changed = true;
            }

            CuentasNube.AsegurarEscucha();
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private void ReloadIfStaleUnlocked()
    {
        EnsureInitializedUnlocked();
        if (!File.Exists(_filePath))
        {
            return;
        }

        var stamp = File.GetLastWriteTimeUtc(_filePath);
        if (stamp <= _loadedUtc)
        {
            return;
        }

        ReadUnlocked();
        if (RestoreMissingPhotos())
        {
            SaveUnlocked(confirmarNube: false);
        }
    }

    private void ReadUnlocked()
    {
        WithFileLock(() =>
        {
            var json = File.ReadAllText(_filePath);
            if (EsSobre(json))
            {
                var sobre = JsonSerializer.Deserialize<SobreLocal>(json, JsonOptions)
                    ?? throw new InvalidOperationException("No se pudo leer el archivo de cuentas.");
                var claro = CifradoCuentas.Abrir(sobre.N, sobre.C, sobre.V <= 0 ? 1 : sobre.V);
                _users = JsonSerializer.Deserialize<List<UserAccount>>(claro, JsonOptions) ?? [];
                _rev = sobre.Rev;
                _formato = sobre.V <= 0 ? 1 : sobre.V;
            }
            else
            {
                _users = JsonSerializer.Deserialize<List<UserAccount>>(json, JsonOptions) ?? [];
                _rev = 0;
                _formato = 0;
            }

            _loadedUtc = File.GetLastWriteTimeUtc(_filePath);
        });
    }

    private void SaveUnlocked(bool confirmarNube = true)
    {
        var esperada = _rev;
        var nueva = _rev + 1;
        var json = JsonSerializer.Serialize(_users, JsonOptions);
        var (nonce, cifrado) = CifradoCuentas.Sellar(json);
        _rev = nueva;
        _formato = CifradoCuentas.VersionActual;
        EscribirSobre(nueva, CifradoCuentas.VersionActual, nonce, cifrado);

        if (!confirmarNube)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await CuentasNube.GuardarSiActualAsync(esperada, nueva, CifradoCuentas.VersionActual, nonce, cifrado);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            });
            return;
        }

        try
        {
            var tarea = Task.Run(() => CuentasNube.GuardarSiActualAsync(esperada, nueva, CifradoCuentas.VersionActual, nonce, cifrado));
            if (!tarea.Wait(TimeSpan.FromSeconds(20)))
            {
                return;
            }

            if (!tarea.GetAwaiter().GetResult())
            {
                var remoto = LeerNube();
                if (remoto.Sobre is not null)
                {
                    AplicarRemoto(remoto.Sobre.Value);
                }

                if (confirmarNube)
                {
                    throw new InvalidOperationException("Las cuentas cambiaron en otro equipo. Se recargaron; vuelve a guardar.");
                }

                return;
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (AggregateException ex) when (ex.InnerException is InvalidOperationException invalid)
        {
            throw invalid;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void OnNube(SobreCuentas sobre)
    {
        if (_disposed)
        {
            return;
        }

        var changed = false;
        lock (_sync)
        {
            if (_disposed || sobre.Rev <= _rev)
            {
                return;
            }

            try
            {
                AplicarRemoto(sobre);
                if (sobre.Version < CifradoCuentas.VersionActual)
                {
                    SaveUnlocked(confirmarNube: false);
                }

                changed = true;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private static LecturaNube LeerNube()
    {
        try
        {
            var tarea = Task.Run(CuentasNube.LeerAsync);
            return tarea.Wait(TimeSpan.FromSeconds(20)) ? tarea.GetAwaiter().GetResult() : new LecturaNube(false, null);
        }
        catch
        {
            return new LecturaNube(false, null);
        }
    }

    private void AplicarRemoto(SobreCuentas sobre)
    {
        var version = sobre.Version <= 0 ? 1 : sobre.Version;
        var claro = CifradoCuentas.Abrir(sobre.Nonce, sobre.Cifrado, version);
        _users = JsonSerializer.Deserialize<List<UserAccount>>(claro, JsonOptions) ?? [];
        _rev = sobre.Rev;
        _formato = version;
        EscribirSobre(sobre.Rev, version, sobre.Nonce, sobre.Cifrado);
    }

    private void EscribirSobre(long rev, int version, string nonce, string cifrado)
    {
        var sobre = new SobreLocal { V = version, Rev = rev, N = nonce, C = cifrado };
        WithFileLock(() =>
        {
            var json = JsonSerializer.Serialize(sobre, JsonOptions);
            File.WriteAllText(_filePath, json);
            _loadedUtc = File.GetLastWriteTimeUtc(_filePath);
        });
    }

    private static bool EsSobre(string json)
    {
        var texto = json.TrimStart();
        return texto.StartsWith('{') && texto.Contains("\"c\"", StringComparison.Ordinal) && texto.Contains("\"n\"", StringComparison.Ordinal);
    }

    private sealed class SobreLocal
    {
        public int V { get; set; } = 1;
        public long Rev { get; set; }
        public string N { get; set; } = string.Empty;
        public string C { get; set; } = string.Empty;
    }

    private FileSystemWatcher CreateWatcher()
    {
        var watcher = new FileSystemWatcher(Path.GetDirectoryName(_filePath)!, Path.GetFileName(_filePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        watcher.Changed += OnDiskChanged;
        watcher.Created += OnDiskChanged;
        watcher.Renamed += OnDiskChanged;
        return watcher;
    }

    private void OnDiskChanged(object sender, FileSystemEventArgs e) => ScheduleExternalReload();

    private void ScheduleExternalReload()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _debounce, cts);
        previous?.Cancel();
        previous?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
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

    public static string AvisoClaveInicial => Path.Combine(DataDirectory, "administrador-inicial.txt");

    private void SeedUnlocked()
    {
        var password = CrearClaveTemporal();
        var (hash, salt, iterations) = PasswordHasher.Hash(password);
        _users =
        [
            new UserAccount
            {
                Username = "admin",
                DisplayName = "Administrador",
                Role = "Administrador",
                PasswordHash = hash,
                PasswordSalt = salt,
                PasswordIterations = iterations,
                MustChangePassword = true,
                IsActive = true,
                CanOpenSettings = true,
                CanExportLogins = true,
                AppIds = [.. AppCatalog.AllIds]
            }
        ];

        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(
            AvisoClaveInicial,
            $"Usuario: admin{Environment.NewLine}Contraseña temporal: {password}{Environment.NewLine}Cámbiala en el primer inicio de sesión. Este archivo se borra al cambiarla.{Environment.NewLine}");
    }

    private static string CrearClaveTemporal()
    {
        const string letras = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz";
        const string numeros = "23456789";
        const string todos = letras + numeros;
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(14);
        var chars = new char[14];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = todos[bytes[i] % todos.Length];
        }

        chars[0] = letras[bytes[0] % letras.Length];
        chars[1] = numeros[bytes[1] % numeros.Length];
        return new string(chars);
    }

    private static void BorrarAvisoClaveInicial()
    {
        try
        {
            if (File.Exists(AvisoClaveInicial))
            {
                File.Delete(AvisoClaveInicial);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static readonly string[] ClavesConocidas =
    [
        "Admin123",
        "Dev123",
        "Doctor123",
        "Logi123",
        "Mkt123",
        "Gerencia123",
        "Recepcion123"
    ];

    private bool MarcarClavesConocidas()
    {
        var changed = false;
        foreach (var user in _users)
        {
            if (user.PasswordIterations > 0)
            {
                continue;
            }

            var conocida = ClavesConocidas.Any(clave =>
                PasswordHasher.Verify(clave, user.PasswordHash, user.PasswordSalt, PasswordHasher.LegacyIterations));
            user.PasswordIterations = PasswordHasher.LegacyIterations;
            if (conocida)
            {
                user.MustChangePassword = true;
            }

            changed = true;
        }

        return changed;
    }

    private void ValidateUsername(string username, string? excludeId)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("El usuario es obligatorio.");
        }

        var exists = _users.Any(item =>
            item.Id != excludeId &&
            item.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            throw new InvalidOperationException("Ya existe una cuenta con ese usuario.");
        }
    }

    private static void AplicarClave(UserAccount user, string newPassword)
    {
        var (hash, salt, iterations) = PasswordHasher.Hash(newPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.PasswordIterations = iterations;
        user.MustChangePassword = false;
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 10 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new InvalidOperationException("La contraseña debe tener al menos 10 caracteres, con una letra y un número.");
        }

        if (ClavesConocidas.Contains(password, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Esa contraseña ya no se puede usar. Elige otra.");
        }
    }

    private bool ConcederCuentasSiFalta()
    {
        var changed = false;
        foreach (var user in _users)
        {
            if (user.AppIds.Contains(AppCatalog.Cuentas, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var teniaPrograma = user.AppIds.Contains(AppCatalog.Administracion, StringComparer.OrdinalIgnoreCase);
            var teniaTodo = AppCatalog.AllIds
                .Where(id => !id.Equals(AppCatalog.Cuentas, StringComparison.OrdinalIgnoreCase))
                .All(id => user.AppIds.Contains(id, StringComparer.OrdinalIgnoreCase));
            if (!teniaPrograma && !teniaTodo)
            {
                continue;
            }

            user.AppIds.Add(AppCatalog.Cuentas);
            changed = true;
        }

        return changed;
    }

    private bool ConcederSiTieneElResto(params string[] appIds)
    {
        var pending = new HashSet<string>(appIds, StringComparer.OrdinalIgnoreCase);
        var resto = AppCatalog.AllIds
            .Where(id => !pending.Contains(id))
            .ToArray();
        var changed = false;

        foreach (var user in _users)
        {
            if (!resto.All(id => user.AppIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var appId in appIds)
            {
                if (user.AppIds.Contains(appId, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                user.AppIds.Add(appId);
                changed = true;
            }
        }

        return changed;
    }

    private static readonly HashSet<string> AllowedPhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private bool RestoreMissingPhotos()
    {
        var changed = false;
        if (!Directory.Exists(Path.Combine(DataDirectory, "photos")))
        {
            return false;
        }

        foreach (var user in _users)
        {
            if (!string.IsNullOrWhiteSpace(user.PhotoPath) && File.Exists(user.PhotoPath))
            {
                continue;
            }

            var found = FindStoredPhoto(user.Id);
            if (found is null)
            {
                if (!string.IsNullOrWhiteSpace(user.PhotoPath))
                {
                    user.PhotoPath = string.Empty;
                    changed = true;
                }

                continue;
            }

            if (!string.Equals(user.PhotoPath, found, StringComparison.OrdinalIgnoreCase))
            {
                user.PhotoPath = found;
                changed = true;
            }
        }

        return changed;
    }

    private static string? FindStoredPhoto(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var folder = Path.Combine(DataDirectory, "photos");
        foreach (var extension in AllowedPhotoExtensions)
        {
            var path = Path.Combine(folder, id + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string PhotosDirectory()
    {
        var folder = Path.Combine(DataDirectory, "photos");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void TryDeletePhoto(string? path, string? keep)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(keep) &&
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var root = Path.GetFullPath(PhotosDirectory());
        var full = Path.GetFullPath(path);
        var inside = full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!inside || !File.Exists(full))
        {
            return;
        }

        try
        {
            File.Delete(full);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static List<string> NormalizeAppIds(IEnumerable<string> appIds)
    {
        var allowed = new HashSet<string>(AppCatalog.AllIds, StringComparer.OrdinalIgnoreCase);
        return appIds
            .Where(id => allowed.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => id.ToLowerInvariant())
            .ToList();
    }

    private static UserAccount Clone(UserAccount user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        DisplayName = user.DisplayName,
        PhotoPath = user.PhotoPath,
        Role = user.Role,
        PasswordHash = user.PasswordHash,
        PasswordSalt = user.PasswordSalt,
        PasswordIterations = user.PasswordIterations,
        MustChangePassword = user.MustChangePassword,
        IsActive = user.IsActive,
        CanOpenSettings = user.CanOpenSettings,
        CanExportLogins = user.CanExportLogins,
        NotifyLogistica = user.NotifyLogistica,
        NotifyClinica = user.NotifyClinica,
        NotifyBaseDeDatos = user.NotifyBaseDeDatos,
        AppIds = [.. user.AppIds],
        CreatedAt = user.CreatedAt
    };
}
