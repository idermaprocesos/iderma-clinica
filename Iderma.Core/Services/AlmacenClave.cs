using System.Runtime.InteropServices;

namespace Iderma.Core.Services;

internal static class AlmacenClave
{
    private const string Nombre = "Iderma.Cuentas.Clave";
    private const int TipoGenerica = 1;
    private const int PersistirEnEquipo = 2;

    public static byte[]? Leer()
    {
        if (!CredRead(Nombre, TipoGenerica, 0, out var puntero))
        {
            return null;
        }

        try
        {
            var credencial = Marshal.PtrToStructure<CredencialNativa>(puntero);
            if (credencial.CredentialBlobSize < 32 || credencial.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var datos = new byte[32];
            Marshal.Copy(credencial.CredentialBlob, datos, 0, datos.Length);
            return datos;
        }
        finally
        {
            CredFree(puntero);
        }
    }

    public static void Guardar(byte[] clave)
    {
        if (clave.Length != 32)
        {
            throw new InvalidOperationException("La clave de cuentas no es válida.");
        }

        var blob = Marshal.AllocHGlobal(clave.Length);
        try
        {
            Marshal.Copy(clave, 0, blob, clave.Length);
            var credencial = new CredencialNativa
            {
                Type = TipoGenerica,
                TargetName = Nombre,
                Comment = string.Empty,
                CredentialBlobSize = clave.Length,
                CredentialBlob = blob,
                Persist = PersistirEnEquipo,
                TargetAlias = string.Empty,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credencial, 0))
            {
                throw new InvalidOperationException("No se pudo guardar la clave de cuentas en Windows.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref CredencialNativa credencial, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr cred);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CredencialNativa
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
