using System.Security.Cryptography;
using System.Text;

namespace Iderma.Core.Services;

internal static class CifradoCuentas
{
    public const int VersionActual = 2;

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] ClaveAnterior = Convert.FromHexString(
        "a7c3e91b04d58f2c6ab017e4d9c83f5a1b6e0d47c2f8a953e0b14d7c6a28f0e1");

    private static readonly object Candado = new();
    private static byte[]? _clave;
    private static int _publicando;
    private static bool _publicada;

    public static (string Nonce, string Cifrado) Sellar(string json)
    {
        var clave = ExigirParaCifrar();
        return SellarCon(clave, json);
    }

    public static string Abrir(string nonce, string cifrado, int version)
    {
        var clave = version <= 1 ? ClaveAnterior : ExigirParaAbrir();
        return AbrirCon(clave, nonce, cifrado);
    }

    private static byte[] ExigirParaAbrir()
    {
        var clave = Buscar();
        if (clave is null)
        {
            throw new CryptographicException("Este equipo no tiene la clave de cuentas.");
        }

        return clave;
    }

    private static byte[] ExigirParaCifrar()
    {
        var clave = Buscar();
        if (clave is not null)
        {
            IntentarPublicar(clave);
            return clave;
        }

        lock (Candado)
        {
            if (_clave is { Length: 32 })
            {
                IntentarPublicar(_clave);
                return _clave;
            }

            var nueva = RandomNumberGenerator.GetBytes(32);
            if (!ClaveNube.PublicarSiVacia(nueva))
            {
                var ajena = ClaveNube.Leer();
                if (ajena is { Length: 32 })
                {
                    AlmacenClave.Guardar(ajena);
                    _clave = ajena;
                    _publicada = true;
                    return ajena;
                }
            }
            else
            {
                _publicada = true;
            }

            AlmacenClave.Guardar(nueva);
            _clave = nueva;
            return nueva;
        }
    }

    private static byte[]? Buscar()
    {
        if (_clave is { Length: 32 })
        {
            return _clave;
        }

        lock (Candado)
        {
            if (_clave is { Length: 32 })
            {
                return _clave;
            }

            var local = AlmacenClave.Leer() ?? ImportarArchivo();
            var desdeNube = false;
            if (local is null)
            {
                local = ClaveNube.Leer();
                desdeNube = local is { Length: 32 };
            }

            if (local is not { Length: 32 })
            {
                return null;
            }

            AlmacenClave.Guardar(local);
            _clave = local;
            _publicada = desdeNube;
            return local;
        }
    }

    private static void IntentarPublicar(byte[] clave)
    {
        if (_publicada || Interlocked.Exchange(ref _publicando, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                if (ClaveNube.PublicarSiVacia(clave) || MismaClave(ClaveNube.Leer(), clave))
                {
                    _publicada = true;
                }
            }
            catch
            {
            }
            finally
            {
                if (!_publicada)
                {
                    Interlocked.Exchange(ref _publicando, 0);
                }
            }
        });
    }

    private static bool MismaClave(byte[]? izquierda, byte[] derecha) =>
        izquierda is { Length: 32 } && CryptographicOperations.FixedTimeEquals(izquierda, derecha);

    private static byte[]? ImportarArchivo()
    {
        var ruta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdermaCapilar",
            "cuentas.key");
        if (!File.Exists(ruta))
        {
            return null;
        }

        try
        {
            var texto = File.ReadAllText(ruta).Trim();
            var clave = texto.Length == 64
                ? Convert.FromHexString(texto)
                : Convert.FromBase64String(texto);
            if (clave.Length != 32)
            {
                return null;
            }

            File.Delete(ruta);
            return clave;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static (string Nonce, string Cifrado) SellarCon(byte[] clave, string json)
    {
        var claro = Encoding.UTF8.GetBytes(json);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cifrado = new byte[claro.Length];
        var etiqueta = new byte[TagSize];
        using var aes = new AesGcm(clave, TagSize);
        aes.Encrypt(nonce, claro, cifrado, etiqueta);

        var paquete = new byte[cifrado.Length + etiqueta.Length];
        cifrado.CopyTo(paquete, 0);
        etiqueta.CopyTo(paquete, cifrado.Length);
        return (Convert.ToBase64String(nonce), Convert.ToBase64String(paquete));
    }

    private static string AbrirCon(byte[] clave, string nonce, string cifrado)
    {
        var nonceBytes = Convert.FromBase64String(nonce);
        var paquete = Convert.FromBase64String(cifrado);
        if (nonceBytes.Length != NonceSize || paquete.Length < TagSize)
        {
            throw new CryptographicException("El paquete de cuentas no es válido.");
        }

        var cuerpo = paquete.Length - TagSize;
        var etiqueta = paquete.AsSpan(cuerpo, TagSize);
        var datos = paquete.AsSpan(0, cuerpo);
        var claro = new byte[cuerpo];
        using var aes = new AesGcm(clave, TagSize);
        aes.Decrypt(nonceBytes, datos, etiqueta, claro);
        return Encoding.UTF8.GetString(claro);
    }
}
