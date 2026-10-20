using System.Collections.ObjectModel;
using IdermaClinica.Models;

namespace IdermaClinica.Services;

public sealed class EnfermeraService
{
    private const string NombreArchivo = "enfermeras.json";

    public static EnfermeraService Instance { get; } = new();

    private EnfermeraService()
    {
    }

    public ObservableCollection<Enfermera> Enfermeras { get; } = [];

    public event EventHandler? EnfermerasActualizadas;

    public async Task CargarAsync()
    {
        var estado = await LocalStore.LeerAsync<EnfermerasEstado>(NombreArchivo);
        Enfermeras.Clear();

        if (estado?.Enfermeras is { Count: > 0 })
        {
            foreach (var enfermera in estado.Enfermeras.Where(e => !string.IsNullOrWhiteSpace(e.Nombre)))
            {
                Enfermeras.Add(enfermera);
            }
        }
        else
        {
            foreach (var nombre in new[]
            {
                "Ana López",
                "María Hernández",
                "Laura Gómez",
                "Patricia Ruiz",
                "Carmen Ortega"
            })
            {
                Enfermeras.Add(new Enfermera { Nombre = nombre });
            }

            await GuardarAsync();
        }

        EnfermerasActualizadas?.Invoke(this, EventArgs.Empty);
    }

    public async Task<Enfermera> AgregarAsync(string nombre)
    {
        var limpio = nombre.Trim();
        if (string.IsNullOrWhiteSpace(limpio))
        {
            throw new InvalidOperationException("Escribe el nombre de la enfermera.");
        }

        if (Enfermeras.Any(e => e.Nombre.Equals(limpio, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Ese nombre ya está en la lista.");
        }

        var enfermera = new Enfermera { Nombre = limpio };
        Enfermeras.Add(enfermera);
        await GuardarAsync();
        EnfermerasActualizadas?.Invoke(this, EventArgs.Empty);
        return enfermera;
    }

    public async Task EditarAsync(Enfermera enfermera, string nombre)
    {
        var limpio = nombre.Trim();
        if (string.IsNullOrWhiteSpace(limpio))
        {
            throw new InvalidOperationException("Escribe el nombre de la enfermera.");
        }

        if (Enfermeras.Any(e => e.Id != enfermera.Id && e.Nombre.Equals(limpio, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Ese nombre ya está en la lista.");
        }

        enfermera.Nombre = limpio;
        await GuardarAsync();
        EnfermerasActualizadas?.Invoke(this, EventArgs.Empty);
    }

    private Task GuardarAsync()
    {
        return LocalStore.GuardarAsync(NombreArchivo, new EnfermerasEstado
        {
            Enfermeras = [.. Enfermeras]
        });
    }
}
