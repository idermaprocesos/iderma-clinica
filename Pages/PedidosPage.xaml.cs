using IdermaClinica.Models;
using IdermaClinica.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace IdermaClinica.Pages;

public sealed partial class PedidosPage : Page
{
    private readonly PedidoService _pedido = PedidoService.Instance;
    private readonly CatalogoService _catalogo = CatalogoService.Instance;
    private readonly EnfermeraService _enfermeras = EnfermeraService.Instance;

    private DateTime _ultimoAgregadoUtc;
    private string? _ultimaClaveAgregada;
    private int _indiceSugerencia = -1;
    private TextBox? _cajaBusqueda;
    private readonly KeyEventHandler _tecladoBuscador;

    public PedidosPage()
    {
        _tecladoBuscador = Buscador_PreviewKeyDown;
        InitializeComponent();
        ListaPedido.ItemsSource = _pedido.Items;
        ComboEnfermeras.ItemsSource = _enfermeras.Enfermeras;
        _pedido.PedidoActualizado += Pedido_Actualizado;
        _catalogo.CatalogoActualizado += Catalogo_Actualizado;
        Loaded += PedidosPage_Loaded;
        Unloaded += PedidosPage_Unloaded;
    }

    private void PedidosPage_Loaded(object sender, RoutedEventArgs e)
    {
        ActualizarResumen();
        ActualizarAvisoCatalogo();
        if (ComboEnfermeras.SelectedItem is null && _enfermeras.Enfermeras.Count > 0)
        {
            ComboEnfermeras.SelectedIndex = 0;
        }

        if (_pedido.AvisoPendiente is { } pendiente)
        {
            _pedido.AvisoPendiente = null;
            MostrarResultado(pendiente);
        }

        ConectarTecladoBuscador();
        BuscadorProductos.Focus(FocusState.Programmatic);
    }

    private void PedidosPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _pedido.PedidoActualizado -= Pedido_Actualizado;
        _catalogo.CatalogoActualizado -= Catalogo_Actualizado;
        if (_cajaBusqueda is not null)
        {
            _cajaBusqueda.RemoveHandler(UIElement.KeyDownEvent, _tecladoBuscador);
            _cajaBusqueda = null;
        }
    }

    private void Pedido_Actualizado(object? sender, EventArgs e) => ActualizarResumen();

    private void Catalogo_Actualizado(object? sender, EventArgs e)
    {
        ActualizarAvisoCatalogo();
        if (BuscadorProductos.IsSuggestionListOpen && !string.IsNullOrWhiteSpace(BuscadorProductos.Text))
        {
            ActualizarSugerencias(BuscadorProductos.Text);
        }
    }

    private async void ActualizarInventario_Click(object sender, RoutedEventArgs e)
    {
        await _catalogo.SincronizarFirebaseAsync();
        if (!string.IsNullOrWhiteSpace(_catalogo.ErrorNube))
        {
            MostrarAviso(_catalogo.ErrorNube, InfoBarSeverity.Error);
            return;
        }

        MostrarAviso($"Inventario actualizado · {_catalogo.Productos.Count} productos.", InfoBarSeverity.Success);
    }

    private async void CargarNuevaLista_Click(object sender, RoutedEventArgs e)
    {
        if (_pedido.Items.Count > 0)
        {
            var dialogo = new ContentDialog
            {
                Title = "Cargar nueva lista",
                Content = "Se vaciará el pedido actual y se abrirá una lista nueva. El historial no se borra.",
                PrimaryButtonText = "Cargar nueva lista",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            if (await dialogo.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        _pedido.CargarNuevaLista();
        CheckUrgente.IsChecked = false;
        MostrarAviso("Se cargó una lista nueva.", InfoBarSeverity.Informational);
        BuscadorProductos.Text = string.Empty;
        BuscadorProductos.Focus(FocusState.Programmatic);
    }

    private void ConectarTecladoBuscador()
    {
        var caja = BuscarDescendente<TextBox>(BuscadorProductos);
        if (caja is null || ReferenceEquals(caja, _cajaBusqueda))
        {
            return;
        }

        if (_cajaBusqueda is not null)
        {
            _cajaBusqueda.RemoveHandler(UIElement.KeyDownEvent, _tecladoBuscador);
        }

        _cajaBusqueda = caja;
        caja.AddHandler(UIElement.KeyDownEvent, _tecladoBuscador, handledEventsToo: true);
    }

    private void Buscador_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Down or VirtualKey.Up or VirtualKey.Enter))
        {
            return;
        }

        if (e.Key is VirtualKey.Down or VirtualKey.Up)
        {
            if (MoverSugerencia(e.Key == VirtualKey.Down ? 1 : -1))
            {
                e.Handled = true;
            }

            return;
        }

        if (SugerenciaSeleccionada() is Producto producto)
        {
            AgregarProducto(producto);
            e.Handled = true;
        }
    }

    private void Buscador_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        ActualizarSugerencias(sender.Text);
    }

    private void Buscador_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        // La selección con flechas no debe agregar; Enter o clic lo hacen en QuerySubmitted.
    }

    private void Buscador_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is Producto elegido)
        {
            AgregarProducto(elegido);
            return;
        }

        if (SugerenciaSeleccionada() is Producto seleccionado)
        {
            AgregarProducto(seleccionado);
            return;
        }

        var producto = _catalogo.BuscarExactoOUnico(args.QueryText);
        if (producto is not null)
        {
            AgregarProducto(producto);
            return;
        }

        ActualizarSugerencias(args.QueryText);
        MensajeBusqueda.Text = _catalogo.TieneProductos
            ? "Elige una sugerencia con las flechas y pulsa Enter para agregarla."
            : "Espera a que se lea el inventario de Firebase.";
    }

    private void ActualizarSugerencias(string texto)
    {
        if (!_catalogo.TieneProductos)
        {
            BuscadorProductos.ItemsSource = Array.Empty<Producto>();
            BuscadorProductos.IsSuggestionListOpen = false;
            MensajeBusqueda.Text = _catalogo.Sincronizando
                ? "Leyendo la colección de inventario en Firebase…"
                : _catalogo.ErrorNube ?? "No hay inventario en la colección de Firebase.";
            return;
        }

        var sugerencias = _catalogo.Buscar(texto);
        _indiceSugerencia = -1;
        BuscadorProductos.ItemsSource = sugerencias;
        BuscadorProductos.IsSuggestionListOpen = sugerencias.Count > 0;
        MensajeBusqueda.Text = string.IsNullOrWhiteSpace(texto)
            ? "Empieza a escribir para ver sugerencias. Usa flechas y Enter."
            : sugerencias.Count == 0
                ? "No hay coincidencias en el inventario."
                : $"{sugerencias.Count} sugerencias. Recórrelas con las flechas.";
    }

    private bool MoverSugerencia(int desplazamiento)
    {
        if (BuscadorProductos.ItemsSource is not IReadOnlyList<Producto> { Count: > 0 } sugerencias)
        {
            ActualizarSugerencias(BuscadorProductos.Text);
            if (BuscadorProductos.ItemsSource is not IReadOnlyList<Producto> { Count: > 0 } recargadas)
            {
                return false;
            }

            sugerencias = recargadas;
        }

        BuscadorProductos.IsSuggestionListOpen = true;
        if (_indiceSugerencia < 0)
        {
            _indiceSugerencia = desplazamiento > 0 ? 0 : sugerencias.Count - 1;
        }
        else
        {
            _indiceSugerencia = Math.Clamp(_indiceSugerencia + desplazamiento, 0, sugerencias.Count - 1);
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            var lista = ObtenerListaSugerencias();
            if (lista is not null && _indiceSugerencia >= 0 && _indiceSugerencia < lista.Items.Count)
            {
                lista.SelectedIndex = _indiceSugerencia;
                if (lista.SelectedItem is not null)
                {
                    lista.ScrollIntoView(lista.SelectedItem);
                }
            }
        });

        var producto = sugerencias[_indiceSugerencia];
        MensajeBusqueda.Text = $"{_indiceSugerencia + 1}/{sugerencias.Count} · {producto.Nombre}";
        return true;
    }

    private Producto? SugerenciaSeleccionada()
    {
        if (ObtenerListaSugerencias()?.SelectedItem is Producto producto)
        {
            return producto;
        }

        if (BuscadorProductos.ItemsSource is IReadOnlyList<Producto> sugerencias &&
            _indiceSugerencia >= 0 &&
            _indiceSugerencia < sugerencias.Count)
        {
            return sugerencias[_indiceSugerencia];
        }

        return null;
    }

    private ListView? ObtenerListaSugerencias()
    {
        var local = BuscarDescendente<ListView>(BuscadorProductos);
        if (EsListaDeSugerencias(local))
        {
            return local;
        }

        if (BuscadorProductos.XamlRoot is null)
        {
            return null;
        }

        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(BuscadorProductos.XamlRoot))
        {
            if (popup.Child is null)
            {
                continue;
            }

            var lista = BuscarDescendente<ListView>(popup.Child);
            if (EsListaDeSugerencias(lista))
            {
                return lista;
            }
        }

        return null;
    }

    private bool EsListaDeSugerencias(ListView? lista)
    {
        return lista is not null &&
               (ReferenceEquals(lista.ItemsSource, BuscadorProductos.ItemsSource) ||
                lista.Items.OfType<Producto>().Any());
    }

    private static T? BuscarDescendente<T>(DependencyObject raiz) where T : DependencyObject
    {
        var total = VisualTreeHelper.GetChildrenCount(raiz);
        for (var i = 0; i < total; i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T encontrado)
            {
                return encontrado;
            }

            var anidado = BuscarDescendente<T>(hijo);
            if (anidado is not null)
            {
                return anidado;
            }
        }

        return null;
    }

    private void AgregarProducto(Producto producto)
    {
        if (_ultimaClaveAgregada == producto.Clave &&
            DateTime.UtcNow - _ultimoAgregadoUtc < TimeSpan.FromMilliseconds(400))
        {
            return;
        }

        _ultimaClaveAgregada = producto.Clave;
        _ultimoAgregadoUtc = DateTime.UtcNow;
        var resultado = _pedido.AgregarUno(producto);
        BuscadorProductos.Text = string.Empty;
        BuscadorProductos.ItemsSource = Array.Empty<Producto>();
        BuscadorProductos.IsSuggestionListOpen = false;
        MostrarResultado(resultado);
        BuscadorProductos.Focus(FocusState.Programmatic);
    }

    private void Cantidad_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (sender.DataContext is not PedidoItem item ||
            double.IsNaN(args.NewValue) ||
            double.IsNaN(args.OldValue))
        {
            return;
        }

        var nueva = Math.Max(1, (int)Math.Round(args.NewValue));
        if (nueva == item.Cantidad)
        {
            return;
        }

        var resultado = _pedido.IntentarCambiarCantidad(item, nueva);
        if (!resultado.Exito)
        {
            sender.Value = item.Cantidad;
            MostrarResultado(resultado);
        }
    }

    private void Quitar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button boton && boton.DataContext is PedidoItem item)
        {
            _pedido.Quitar(item);
        }
    }

    private async void AgregarEnfermera_Click(object sender, RoutedEventArgs e)
    {
        var nombre = await PedirNombreEnfermeraAsync("Agregar enfermera", string.Empty);
        if (nombre is null)
        {
            return;
        }

        try
        {
            var nueva = await _enfermeras.AgregarAsync(nombre);
            ComboEnfermeras.SelectedItem = nueva;
        }
        catch (Exception ex)
        {
            MostrarAviso(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void EditarEnfermera_Click(object sender, RoutedEventArgs e)
    {
        if (ComboEnfermeras.SelectedItem is not Enfermera enfermera)
        {
            MostrarAviso("Elige una enfermera para editar su nombre.", InfoBarSeverity.Warning);
            return;
        }

        var nombre = await PedirNombreEnfermeraAsync("Editar enfermera", enfermera.Nombre);
        if (nombre is null)
        {
            return;
        }

        try
        {
            await _enfermeras.EditarAsync(enfermera, nombre);
            ComboEnfermeras.SelectedItem = enfermera;
        }
        catch (Exception ex)
        {
            MostrarAviso(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void ProcesarPedido_Click(object sender, RoutedEventArgs e)
    {
        var enfermera = ComboEnfermeras.SelectedItem as Enfermera
            ?? (ComboEnfermeras.SelectedIndex >= 0 && ComboEnfermeras.SelectedIndex < _enfermeras.Enfermeras.Count
                ? _enfermeras.Enfermeras[ComboEnfermeras.SelectedIndex]
                : _enfermeras.Enfermeras.FirstOrDefault());

        if (enfermera is null)
        {
            await MostrarDialogoAsync("Falta la enfermera", "Antes de procesar el pedido elige o agrega a la enfermera.");
            return;
        }

        ComboEnfermeras.SelectedItem = enfermera;

        if (_pedido.Items.Count == 0)
        {
            await MostrarDialogoAsync("Lista vacía", "Agrega productos antes de procesar el pedido.");
            return;
        }

        var urgente = CheckUrgente.IsChecked == true;
        var contenido = urgente
            ? $"Este pedido URGENTE a cargo de {enfermera.Nombre} se enviará como solicitud a logística. Úsalo solo en casos extremos."
            : $"Se enviará la solicitud a logística a cargo de {enfermera.Nombre}. Logística podrá aceptarla o denegarla.";

        var dialogo = new ContentDialog
        {
            Title = urgente ? "Enviar solicitud urgente" : "Enviar solicitud a logística",
            Content = contenido,
            PrimaryButtonText = urgente ? "Enviar urgente" : "Enviar a logística",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialogo.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var resultado = await _pedido.ProcesarAsync(enfermera, urgente);
            CheckUrgente.IsChecked = false;
            MostrarResultado(resultado);

            if (resultado.Exito)
            {
                var ok = new ContentDialog
                {
                    Title = "Solicitud enviada",
                    Content = resultado.Mensaje + " Quedó en Historial y logística recibió la notificación.",
                    PrimaryButtonText = "Ver historial",
                    CloseButtonText = "Seguir aquí",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };

                if (await ok.ShowAsync() == ContentDialogResult.Primary)
                {
                    App.MainAppWindow?.IrAHistorial();
                }
            }
            else
            {
                await MostrarDialogoAsync("No se pudo procesar", resultado.Mensaje);
            }
        }
        catch (Exception ex)
        {
            await MostrarDialogoAsync("No se pudo procesar", ex.Message);
        }
    }

    private async Task MostrarDialogoAsync(string titulo, string mensaje)
    {
        var dialogo = new ContentDialog
        {
            Title = titulo,
            Content = mensaje,
            CloseButtonText = "Cerrar",
            XamlRoot = XamlRoot
        };
        await dialogo.ShowAsync();
    }

    private async Task<string?> PedirNombreEnfermeraAsync(string titulo, string valor)
    {
        var caja = new TextBox
        {
            Text = valor,
            PlaceholderText = "Nombre de la enfermera",
            MinWidth = 280
        };

        var dialogo = new ContentDialog
        {
            Title = titulo,
            Content = caja,
            PrimaryButtonText = "Guardar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        return await dialogo.ShowAsync() == ContentDialogResult.Primary ? caja.Text : null;
    }

    private void ActualizarResumen()
    {
        ResumenListaTexto.Text = _pedido.ResumenLista;
        ResumenPiezasTexto.Text = _pedido.Items.Count == 0
            ? "Sin productos en la lista"
            : $"{_pedido.Items.Count} productos · {_pedido.TotalCantidad} pzas";
        TotalTexto.Text = _pedido.TotalCantidad.ToString();
        EstadoVacio.Visibility = _pedido.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListaPedido.Visibility = _pedido.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ActualizarAvisoCatalogo()
    {
        if (_catalogo.TieneProductos)
        {
            AvisoCatalogo.IsOpen = false;
            return;
        }

        AvisoCatalogo.IsOpen = true;
        if (_catalogo.Sincronizando)
        {
            AvisoCatalogo.Title = "Inventario";
            AvisoCatalogo.Message = "Leyendo la colección de inventario en Firebase…";
            AvisoCatalogo.Severity = InfoBarSeverity.Informational;
            return;
        }

        AvisoCatalogo.Title = "Sin inventario";
        AvisoCatalogo.Message = _catalogo.ErrorNube
            ?? "La clínica solo lee la colección de inventario en Firebase.";
        AvisoCatalogo.Severity = string.IsNullOrWhiteSpace(_catalogo.ErrorNube)
            ? InfoBarSeverity.Informational
            : InfoBarSeverity.Warning;
    }

    private void MostrarResultado(ResultadoPedido resultado)
    {
        MensajeBusqueda.Text = resultado.Mensaje;
        if (string.IsNullOrWhiteSpace(resultado.Mensaje))
        {
            return;
        }

        var severidad = resultado.Exito
            ? (resultado.EsAdvertencia ? InfoBarSeverity.Warning : InfoBarSeverity.Success)
            : (resultado.EsAdvertencia ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
        MostrarAviso(resultado.Mensaje, severidad);
    }

    private void MostrarAviso(string mensaje, InfoBarSeverity severidad)
    {
        if (mensaje.Contains("Ya no hay", StringComparison.OrdinalIgnoreCase))
        {
            AvisoPedido.Title = "Ya no hay";
        }
        else if (severidad == InfoBarSeverity.Success)
        {
            AvisoPedido.Title = "Listo";
        }
        else if (severidad == InfoBarSeverity.Error)
        {
            AvisoPedido.Title = "No se pudo";
        }
        else
        {
            AvisoPedido.Title = "Atención";
        }

        AvisoPedido.Message = mensaje;
        AvisoPedido.Severity = severidad;
        AvisoPedido.IsOpen = true;
    }
}
