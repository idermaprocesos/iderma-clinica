using System.Net.NetworkInformation;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace Iderma.Core.Services;

public sealed class ResultadoAccesoBase
{
    public bool Correcto { get; init; }
    public string Resumen { get; init; } = "";
    public string Error { get; init; } = "";
    public string Solucion { get; init; } = "";
}

public static class DiagnosticoAccesoBase
{
    private const string Coleccion = "boveda";
    private const string Documento = "cuentas";

    public static async Task<ResultadoAccesoBase> EjecutarAsync(CancellationToken cancelacion = default)
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return Fallo(
                "Este equipo no tiene red.",
                "Windows no detecta ninguna conexión de red activa.",
                "Conecta el equipo a Internet (cable o Wi-Fi), comprueba que el navegador abre páginas y vuelve a pulsar Comprobar acceso.");
        }

        var ruta = ConexionFirebase.BuscarArchivoCredenciales();
        var esperada = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdermaCapilar",
            "firebase-adminsdk.json");
        if (ruta is null)
        {
            return Fallo(
                "No está el archivo de acceso a la base de datos.",
                "No se encontró firebase-adminsdk.json ni en los datos de la aplicación ni en una carpeta cred junto al programa.",
                "Pide a quien administra Iderma el archivo de la cuenta de servicio del proyecto iderma-db-2026 y cópialo en:\n" + esperada
                + "\nCierra el launcher y ábrelo de nuevo. El archivo no debe compartirse por correo ni quedar en el escritorio.");
        }

        JsonElement raiz;
        try
        {
            await using var flujo = File.OpenRead(ruta);
            using var doc = await JsonDocument.ParseAsync(flujo, cancellationToken: cancelacion);
            raiz = doc.RootElement.Clone();
        }
        catch (UnauthorizedAccessException)
        {
            return Fallo(
                "Windows bloquea el archivo de acceso.",
                "No se puede leer " + ruta,
                "Clic derecho en el archivo, Propiedades, y quita Solo lectura. Si está en una carpeta protegida, cópialo a:\n" + esperada
                + "\nInicia sesión en Windows con un usuario que pueda leer esa carpeta.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            return Fallo(
                "El archivo de acceso está dañado o incompleto.",
                Texto(ex),
                "Vuelve a descargar la clave de la cuenta de servicio en Google Cloud (proyecto iderma-db-2026) y sustituye el archivo en:\n" + ruta
                + "\nTiene que ser un JSON, no un acceso directo ni un archivo renombrado.");
        }

        var tipo = Leer(raiz, "type");
        var proyecto = Leer(raiz, "project_id");
        var correo = Leer(raiz, "client_email");
        if (!string.Equals(tipo, "service_account", StringComparison.Ordinal))
        {
            return Fallo(
                "El archivo no es una clave de cuenta de servicio.",
                string.IsNullOrEmpty(tipo) ? "Falta el campo type." : "El campo type es «" + tipo + "».",
                "En Google Cloud, IAM, Cuentas de servicio, crea o elige la cuenta de Iderma y descarga una clave JSON nueva. Sustituye " + ruta);
        }

        if (!string.Equals(proyecto, ConexionFirebase.Proyecto, StringComparison.Ordinal))
        {
            return Fallo(
                "La clave pertenece a otro proyecto.",
                "El archivo indica el proyecto «" + (string.IsNullOrEmpty(proyecto) ? "(vacío)" : proyecto) + "» y el programa usa " + ConexionFirebase.Proyecto + ".",
                "Descarga la clave del proyecto " + ConexionFirebase.Proyecto + " y reemplaza " + ruta + ". No mezcles claves de otros proyectos de Google.");
        }

        if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(Leer(raiz, "private_key")))
        {
            return Fallo(
                "A la clave le faltan datos.",
                "Falta client_email o private_key.",
                "Sustituye " + ruta + " por una clave JSON completa descargada desde Google Cloud. No recortes el archivo al copiarlo.");
        }

        GoogleCredential credencial;
        try
        {
            credencial = CredentialFactory.FromFile<ServiceAccountCredential>(ruta)
                .ToGoogleCredential()
                .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            limite.CancelAfter(TimeSpan.FromSeconds(20));
            await ((ITokenAccess)credencial).GetAccessTokenForRequestAsync(cancellationToken: limite.Token);
        }
        catch (Exception ex)
        {
            return Interpretar(ex, ruta, correo);
        }

        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            limite.CancelAfter(TimeSpan.FromSeconds(25));
            var db = await ConexionFirebase.AbrirAsync();
            if (db is null)
            {
                return Fallo(
                    "No se pudo abrir la base de datos.",
                    "La credencial se leyó, pero el cliente de Firestore no se creó.",
                    "Cierra el launcher por completo y vuelve a abrirlo. Si se repite, sustituye la clave en " + ruta);
            }

            await db.Collection(Coleccion).Document(Documento).GetSnapshotAsync(limite.Token);
            return new ResultadoAccesoBase
            {
                Correcto = true,
                Resumen = "La base de datos responde.",
                Solucion = "La cuenta " + correo + " entra en Firestore (" + ConexionFirebase.Proyecto + "). El acceso está bien."
            };
        }
        catch (Exception ex)
        {
            return Interpretar(ex, ruta, correo);
        }
    }

    private static ResultadoAccesoBase Interpretar(Exception ex, string ruta, string correo)
    {
        var texto = Texto(ex);
        var codigo = texto.ToUpperInvariant();

        if (codigo.Contains("UNAUTHENTICATED", StringComparison.Ordinal)
            || codigo.Contains("INVALID_GRANT", StringComparison.Ordinal)
            || codigo.Contains("INVALID_CLIENT", StringComparison.Ordinal)
            || codigo.Contains("ACCOUNT NOT FOUND", StringComparison.Ordinal))
        {
            return Fallo(
                "Google rechazó la clave.",
                texto,
                "La clave de " + correo + " está revocada, vencida o el reloj del equipo está mal. En Google Cloud genera una clave nueva, sustituye " + ruta
                + " y, en Windows, ajusta la fecha y la hora en automático.");
        }

        if (codigo.Contains("PERMISSION_DENIED", StringComparison.Ordinal) || codigo.Contains("PERMISSION DENIED", StringComparison.Ordinal))
        {
            return Fallo(
                "La cuenta entra, pero no tiene permiso para leer la base.",
                texto,
                "En Google Cloud, proyecto " + ConexionFirebase.Proyecto + ", asigna a " + correo
                + " el rol Cloud Datastore User (o Firebase Admin). Espera un minuto y vuelve a comprobar.");
        }

        if (codigo.Contains("NOT_FOUND", StringComparison.Ordinal) && codigo.Contains("DATABASE", StringComparison.Ordinal))
        {
            return Fallo(
                "La base de Firestore no existe en el proyecto.",
                texto,
                "En la consola de Firebase, proyecto " + ConexionFirebase.Proyecto + ", crea la base Firestore en modo nativo. Luego vuelve a comprobar el acceso.");
        }

        if (ex is OperationCanceledException
            || codigo.Contains("DEADLINE", StringComparison.Ordinal)
            || codigo.Contains("TIMEOUT", StringComparison.Ordinal)
            || codigo.Contains("UNAVAILABLE", StringComparison.Ordinal)
            || codigo.Contains("NO SUCH HOST", StringComparison.Ordinal)
            || codigo.Contains("NAME RESOLUTION", StringComparison.Ordinal))
        {
            return Fallo(
                "No hay respuesta de la base de datos.",
                texto,
                "Revisa Internet, el firewall y el proxy. El equipo tiene que poder salir a firestore.googleapis.com y oauth2.googleapis.com (puerto 443). Si usas VPN de la clínica, conéctala y vuelve a comprobar.");
        }

        return Fallo(
            "No se pudo usar la base de datos.",
            texto,
            "Anota este mensaje y compártelo con quien administra Iderma. Comprueba también que " + ruta + " sea la clave vigente del proyecto " + ConexionFirebase.Proyecto + ".");
    }

    private static ResultadoAccesoBase Fallo(string resumen, string error, string solucion) => new()
    {
        Correcto = false,
        Resumen = resumen,
        Error = error,
        Solucion = solucion
    };

    private static string Leer(JsonElement raiz, string nombre) =>
        raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString() ?? ""
            : "";

    private static string Texto(Exception ex)
    {
        var partes = new List<string>();
        for (Exception? actual = ex; actual is not null && partes.Count < 4; actual = actual.InnerException)
        {
            var linea = actual.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (linea.Length == 0 || partes.Contains(linea))
            {
                continue;
            }

            if (linea.Contains("PRIVATE_KEY", StringComparison.OrdinalIgnoreCase)
                || linea.Contains("BEGIN PRIVATE", StringComparison.OrdinalIgnoreCase))
            {
                linea = "La clave privada del archivo no es válida.";
            }

            partes.Add(linea.Length <= 280 ? linea : linea[..280]);
        }

        return partes.Count == 0 ? ex.GetType().Name : string.Join(" ", partes);
    }
}
