using Iderma.Core.Models;

namespace Iderma.Core.Catalog;

public static class AppCatalog
{
    public const string Administracion = "administracion";
    public const string BaseDeDatos = "basededatos";
    public const string Cuentas = "cuentas";
    public const string Clinica = "clinica";
    public const string Contabilidad = "contabilidad";
    public const string Clientes = "clientes";
    public const string Desarrollador = "desarrollador";
    public const string Gerencia = "gerencia";
    public const string Hardware = "hardware";
    public const string Incidencias = "incidencias";
    public const string Historial = "historial";
    public const string Logistica = "logistica";
    public const string Mantenimiento = "mantenimiento";
    public const string Marketing = "marketing";
    public const string Procesos = "procesos";
    public const string Reclamos = "reclamos";
    public const string Ti = "ti";

    public static IReadOnlyList<AppDefinition> All { get; } =
    [
        new()
        {
            Id = Logistica,
            Name = "Logística",
            Description = "Inventario, insumos, compras y distribución de la clínica.",
            Status = AppStatus.Beta,
            LogoFileName = "logistica.png"
        },
        new()
        {
            Id = Clinica,
            Name = "Clínica",
            Description = "Pedidos del área de enfermería al almacén.",
            Status = AppStatus.Beta,
            LogoFileName = "clinica.png"
        },
        new()
        {
            Id = Incidencias,
            Name = "Incidencias",
            Description = "Reportes de equipos averiados y falta de insumos o recursos.",
            Status = AppStatus.Beta,
            LogoFileName = "incidencias.png"
        },
        new()
        {
            Id = Clientes,
            Name = "Clientes",
            Description = "Clientes, ventas, cuidados y operaciones de la clínica.",
            Status = AppStatus.Alpha,
            LogoFileName = "clientes.png"
        },
        new()
        {
            Id = Historial,
            Name = "Historial",
            Description = "Expediente clínico, evolución de tratamientos y registro de procedimientos.",
            Status = AppStatus.Alpha,
            LogoFileName = "historial.png"
        },
        new()
        {
            Id = Mantenimiento,
            Name = "Mantenimiento",
            Description = "Equipos, calibraciones y mantenimientos de la clínica.",
            Status = AppStatus.Alpha,
            LogoFileName = "mantenimiento.png"
        },
        new()
        {
            Id = Hardware,
            Name = "Hardware",
            Description = "Registro de equipos informáticos de oficina.",
            Status = AppStatus.Beta,
            LogoFileName = "hardware.png"
        },
        new()
        {
            Id = Ti,
            Name = "TI",
            Description = "Mesa de ayuda: tickets, equipos y seguimiento del área de TI.",
            Status = AppStatus.Alpha,
            LogoFileName = "ti.png"
        },
        new()
        {
            Id = Administracion,
            Name = "Administración",
            Description = "Contratos, postulaciones y organigrama de la clínica.",
            Status = AppStatus.Alpha,
            LogoFileName = "administracion.png"
        },
        new()
        {
            Id = Gerencia,
            Name = "Gerencia",
            Description = "Personal, agenda, finanzas, inventario y seguimiento de la clínica.",
            Status = AppStatus.Alpha,
            LogoFileName = "gerencia.png"
        },
        new()
        {
            Id = Contabilidad,
            Name = "Contabilidad",
            Description = "Ingresos, egresos, comprobantes y cierre contable de la clínica.",
            Status = AppStatus.Alpha,
            LogoFileName = "contabilidad.png"
        },
        new()
        {
            Id = Marketing,
            Name = "Marketing",
            Description = "Bitácora de grabaciones.",
            Status = AppStatus.Alpha,
            LogoFileName = "marketing.png"
        },
        new()
        {
            Id = Procesos,
            Name = "Procesos",
            Description = "Flujos operativos, checklists y estandarización de protocolos Iderma.",
            Status = AppStatus.Alpha,
            LogoFileName = "procesos.png"
        },
        new()
        {
            Id = Reclamos,
            Name = "Reclamos",
            Description = "Libro de reclamaciones: hojas, respuesta y plazo de los reclamos.",
            Status = AppStatus.Alpha,
            LogoFileName = "reclamos.png"
        },
        new()
        {
            Id = BaseDeDatos,
            Name = "Base de datos",
            Description = "Autoriza borrados y otras acciones sensibles de las aplicaciones de Iderma.",
            Status = AppStatus.Private,
            LogoFileName = "basededatos.png"
        },
        new()
        {
            Id = Desarrollador,
            Name = "Desarrollador",
            Description = "Herramientas internas de diagnóstico, registros y mantenimiento del ecosistema.",
            Status = AppStatus.Private,
            LogoFileName = "desarrollador.png"
        },
        new()
        {
            Id = Cuentas,
            Name = "Cuentas",
            Description = "Usuarios internos, sedes, permisos y configuración general de Iderma.",
            Status = AppStatus.Private,
            LogoFileName = "cuentas.png"
        }
    ];

    public static AppDefinition? Find(string id) =>
        All.FirstOrDefault(app => app.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string GetLogoUri(string fileName)
    {
        var loose = Path.Combine(AppContext.BaseDirectory, "Assets", "Apps", fileName);
        return File.Exists(loose)
            ? new Uri(loose).AbsoluteUri
            : $"ms-appx:///Assets/Apps/{fileName}";
    }

    public static string StatusLabel(AppStatus status) => status switch
    {
        AppStatus.Alpha => "Alpha",
        AppStatus.Beta => "Beta",
        AppStatus.InImplementation => "En implementación",
        AppStatus.Private => "Privado",
        _ => status.ToString()
    };

    public static int DisplayOrder(AppStatus status) => status switch
    {
        AppStatus.InImplementation => 0,
        AppStatus.Beta => 1,
        AppStatus.Alpha => 2,
        AppStatus.Private => 3,
        _ => 9
    };

    public static IReadOnlyList<string> AllIds { get; } = All.Select(app => app.Id).ToArray();
}
