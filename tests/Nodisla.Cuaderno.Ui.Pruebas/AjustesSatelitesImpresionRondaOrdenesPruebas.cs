using System.IO;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Seguimiento;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Cada orden de las pestañas Ajustes, Satélites, Imprimir y Ronda, una a una, sobre dobles de
/// mentira: que se pueda pulsar cuando toca, que no cuando no, y que haga lo que dice.
/// </summary>
/// <remarks>
/// Antes todo esto se comprobaba mirando la pantalla con el simulador, nunca boton a boton, y
/// asi se colaron el «Exportar ADIF» que no escribia nada, la ronda cuyo RST no se podia
/// teclear y el «Confirmar trabajado» que metia el mismo contacto dos veces.
/// </remarks>
public sealed class AjustesSatelitesImpresionRondaOrdenesPruebas : IDisposable
{
    private readonly BancoDeAjustes _banco = new();

    /// <inheritdoc />
    public void Dispose() => _banco.Dispose();

    // ── Ajustes · credenciales ─────────────────────────────────────────────

    [Fact]
    public void Una_credencial_se_guarda_se_vacia_la_casilla_y_se_puede_borrar()
    {
        var ajustes = _banco.Configuracion();
        var lotw = ajustes.Secretos.Single(s => s.Clave == ClavesDeCredencial.LotwContrasena);

        lotw.GuardarCommand.CanExecute(null).Should().BeFalse("sin teclear nada no hay qué guardar");
        lotw.BorrarCommand.CanExecute(null).Should().BeFalse("no hay nada guardado que borrar");

        lotw.Nuevo = "secreto";
        lotw.GuardarCommand.CanExecute(null).Should().BeTrue();
        lotw.GuardarCommand.Execute(null);

        _banco.Almacen.Guardadas[ClavesDeCredencial.LotwContrasena].Should().Be("secreto");
        lotw.Nuevo.Should().BeEmpty("la casilla se vacía al guardar");
        lotw.Guardado.Should().BeTrue();
        lotw.Estado.Should().Be("Guardada y cifrada");
        lotw.BorrarCommand.CanExecute(null).Should().BeTrue();

        lotw.BorrarCommand.Execute(null);
        _banco.Almacen.Existe(ClavesDeCredencial.LotwContrasena).Should().BeFalse();
        lotw.Estado.Should().Be("Sin guardar");
    }

    [Fact]
    public void Estan_las_ocho_credenciales_y_cada_una_en_su_clave()
    {
        var ajustes = _banco.Configuracion();

        ajustes.Secretos.Should().HaveCount(8);
        ajustes.Secretos.Select(s => s.Clave).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Volver_a_comprobar_trae_el_motivo_de_lotw_y_las_cifras()
    {
        var ajustes = _banco.Configuracion();

        await ajustes.RefrescarCommand.ExecuteAsync(null);

        ajustes.SePuedeSubirALotw.Should().BeFalse();
        ajustes.MotivoDeNoPoderSubirALotw.Should().Be("Falta TQSL en este equipo.");
        ajustes.ContactosDelCuaderno.Should().Be("300");
    }

    // ── Ajustes · ADIF ─────────────────────────────────────────────────────

    [Fact]
    public async Task Exportar_escribe_el_cuaderno_entero_y_se_puede_volver_a_leer()
    {
        var ajustes = _banco.Configuracion();
        var ruta = Path.Combine(_banco.Carpeta, "exportado.adi");
        ajustes.ElegirFicheroParaExportar = () => ruta;

        ajustes.ExportarAdifCommand.CanExecute(null).Should().BeTrue();
        await ajustes.ExportarAdifCommand.ExecuteAsync(null);

        File.Exists(ruta).Should().BeTrue();
        File.Exists(ruta + ".escribiendo").Should().BeFalse("el temporal no se queda tirado");
        ajustes.ParteDeLaImportacion.Should().Contain("300 contactos exportados");
        ajustes.Ocupado.Should().BeFalse();

        await using var fichero = File.OpenRead(ruta);
        var leido = await new Nodisla.Cuaderno.Adif.LectorAdif().LeerAsync(fichero);
        leido.Qsos.Should().HaveCount(300);
    }

    [Fact]
    public async Task Una_exportacion_que_falla_no_destroza_el_fichero_que_ya_habia()
    {
        var ajustes = new VistaModeloAjustes(
            _banco.Almacen,
            new Aplicacion.CasosDeUso.ImportarAdif(new Nodisla.Cuaderno.Adif.LectorAdif(), _banco.Cuaderno),
            _banco.Cuaderno,
            "aviso",
            () => null,
            escritor: new EscritorQueFalla());
        var ruta = Path.Combine(_banco.Carpeta, "respaldo.adi");
        await File.WriteAllTextAsync(ruta, "respaldo de ayer");

        await ajustes.ExportarAsync(ruta);

        (await File.ReadAllTextAsync(ruta)).Should().Be("respaldo de ayer");
        File.Exists(ruta + ".escribiendo").Should().BeFalse();
        ajustes.ParteDeLaImportacion.Should().Contain("No se ha podido exportar");
    }

    [Fact]
    public void Sin_escritor_de_adif_exportar_sale_apagado_y_lo_dice()
    {
        var ajustes = _banco.Configuracion(conEscritor: false);

        ajustes.SePuedeExportar.Should().BeFalse();
        ajustes.ExportarAdifCommand.CanExecute(null).Should().BeFalse();
        ajustes.ImportarAdifCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Importar_lee_el_fichero_elegido_y_da_el_parte()
    {
        var origen = _banco.Configuracion();
        var ruta = Path.Combine(_banco.Carpeta, "para-importar.adi");
        await origen.ExportarAsync(ruta);

        // Un cuaderno vacio al que se trae ese fichero.
        var vacio = new Desarrollo.RepositorioQsoEnMemoria();
        var ajustes = new VistaModeloAjustes(
            _banco.Almacen,
            new Aplicacion.CasosDeUso.ImportarAdif(new Nodisla.Cuaderno.Adif.LectorAdif(), vacio),
            vacio,
            "aviso",
            () => null)
        {
            ElegirFicheroParaImportar = () => ruta,
        };

        await ajustes.ImportarAdifCommand.ExecuteAsync(null);

        (await vacio.ContarAsync()).Should().Be(300);
        ajustes.ParteDeLaImportacion.Should().Contain("300 registros leídos").And.Contain("300 nuevos");
        ajustes.ContactosDelCuaderno.Should().Be("300");
    }

    [Fact]
    public async Task Cancelar_el_dialogo_no_hace_nada()
    {
        var ajustes = _banco.Configuracion();
        ajustes.ElegirFicheroParaImportar = () => null;
        ajustes.ElegirFicheroParaExportar = () => null;

        await ajustes.ImportarAdifCommand.ExecuteAsync(null);
        await ajustes.ExportarAdifCommand.ExecuteAsync(null);

        ajustes.ParteDeLaImportacion.Should().BeEmpty();
    }

    // ── Ajustes · cluster ──────────────────────────────────────────────────

    [Fact]
    public void El_indicativo_de_acceso_sigue_a_lo_que_se_teclea()
    {
        var cluster = _banco.Cluster();
        var avisos = new List<string?>();
        cluster.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        cluster.Indicativo = "ea8dlf";
        cluster.Sufijo = "2";

        avisos.Should().Contain(nameof(VistaModeloAjustesCluster.IndicativoDeAcceso));
        cluster.IndicativoDeAcceso.Should().Be("EA8DLF-2");
    }

    [Fact]
    public void La_contrasena_del_cluster_se_guarda_cifrada_y_se_borra()
    {
        var cluster = _banco.Cluster();

        cluster.GuardarLaContrasenaCommand.CanExecute(null).Should().BeFalse();
        cluster.BorrarLaContrasenaCommand.CanExecute(null).Should().BeFalse();

        cluster.ContrasenaNueva = "clave";
        cluster.GuardarLaContrasenaCommand.Execute(null);

        _banco.Almacen.Guardadas[ClavesDeCredencial.ClusterContrasena].Should().Be("clave");
        cluster.ContrasenaNueva.Should().BeEmpty();
        cluster.EstadoDeLaContrasena.Should().Be("Guardada y cifrada");

        cluster.BorrarLaContrasenaCommand.Execute(null);
        _banco.Almacen.Existe(ClavesDeCredencial.ClusterContrasena).Should().BeFalse();
    }

    [Fact]
    public async Task Aplicar_el_cluster_sin_servidor_lo_dice_y_con_el_lo_guarda()
    {
        var cluster = _banco.Cluster();
        cluster.NodoSeleccionado!.Servidor = string.Empty;
        cluster.Indicativo = "EA8DLF";

        await cluster.AplicarCommand.ExecuteAsync(null);
        cluster.Fallo.Should().BeTrue();
        cluster.HayParte.Should().BeTrue();
        cluster.Parte.Should().Contain("le falta el servidor");

        cluster.NodoSeleccionado.Servidor = cluster.NodosConocidos[2].Servidor;
        cluster.NodoSeleccionado.Puerto = cluster.NodosConocidos[2].Puerto;

        await cluster.AplicarCommand.ExecuteAsync(null);
        cluster.Fallo.Should().BeFalse();
        cluster.Parte.Should().StartWith("Guardado y aplicado");
        AjustesDelPrograma.Leer(_banco.Carpeta).Cluster.Nodos[0].Servidor.Should().Be(cluster.NodosConocidos[2].Servidor);

        cluster.NodoSeleccionado.Servidor = "otro.ejemplo";
        cluster.DescartarCommand.Execute(null);
        cluster.NodoSeleccionado!.Servidor.Should().Be(cluster.NodosConocidos[2].Servidor, "Descartar vuelve a lo guardado");
        cluster.HayParte.Should().BeFalse();
    }

    [Fact]
    public async Task Se_anaden_nodos_conocidos_con_un_clic_y_se_aplican_todos()
    {
        var cluster = _banco.Cluster();
        cluster.Indicativo = "EA8DLF";

        var rbn = cluster.NodosConocidos.First(n => n.EsSkimmer);
        cluster.NodoConocido = rbn;
        cluster.AnadirConocidoCommand.Execute(null);
        cluster.NodoConocido = cluster.NodosConocidos.First(n => n.Servidor == "dxfun.com");
        cluster.AnadirConocidoCommand.Execute(null);

        cluster.Nodos.Should().HaveCount(3);
        cluster.Nodos[1].EsSkimmer.Should().BeTrue("el RBN viene marcado como escucha automática");
        cluster.Nodos[1].GuionDeArranque.Should().NotContain("SH/DX", "al RBN no se le piden anuncios guardados");
        cluster.NodoSeleccionado.Should().BeSameAs(cluster.Nodos[2]);

        // El mismo otra vez no se duplica.
        cluster.AnadirConocidoCommand.Execute(null);
        cluster.Nodos.Should().HaveCount(3);
        cluster.Fallo.Should().BeTrue();

        await cluster.AplicarCommand.ExecuteAsync(null);

        cluster.Fallo.Should().BeFalse(cluster.Parte);
        _banco.FuenteDelCluster.Nodos.Select(n => n.Servidor)
            .Should().Equal(cluster.Nodos.Select(n => n.Servidor));
        _banco.FuenteDelCluster.Nodos[1].EsSkimmer.Should().BeTrue();
        AjustesDelPrograma.Leer(_banco.Carpeta).Cluster.Nodos.Should().HaveCount(3);
        cluster.ResumenDeNodos.Should().Be("3 nodos · 0 conectados");
    }

    [Fact]
    public async Task Quitar_un_nodo_lo_saca_de_la_fuente_y_borra_su_contrasena()
    {
        var cluster = _banco.Cluster();
        cluster.Indicativo = "EA8DLF";
        cluster.AnadirNodoCommand.Execute(null);
        var nuevo = cluster.NodoSeleccionado!;
        nuevo.Servidor = "nodo.ejemplo";
        cluster.ContrasenaNueva = "solo-de-este";
        cluster.GuardarLaContrasenaCommand.Execute(null);
        await cluster.AplicarCommand.ExecuteAsync(null);

        _banco.Almacen.Guardadas[nuevo.ClaveDeContrasena].Should().Be("solo-de-este");
        nuevo.ClaveDeContrasena.Should().NotBe(ClavesDeCredencial.ClusterContrasena, "cada nodo guarda la suya");
        _banco.FuenteDelCluster.Nodos.Should().HaveCount(2);

        cluster.QuitarNodoCommand.Execute(nuevo);
        cluster.Nodos.Should().ContainSingle();
        await cluster.AplicarCommand.ExecuteAsync(null);

        _banco.FuenteDelCluster.Nodos.Should().ContainSingle();
        _banco.Almacen.Existe(nuevo.ClaveDeContrasena).Should().BeFalse("no se quedan contraseñas huérfanas");
    }

    [Fact]
    public async Task Probar_un_nodo_no_entra_y_dice_lo_que_ha_pasado()
    {
        var cluster = _banco.Cluster();
        var nodo = cluster.Nodos[0];

        await cluster.ProbarNodoCommand.ExecuteAsync(nodo);

        _banco.Probados.Should().Equal($"{nodo.Servidor}:{nodo.Puerto}");
        nodo.PruebaBien.Should().BeTrue();
        nodo.Prueba.Should().Contain("42 ms").And.Contain("login:");
    }

    [Fact]
    public async Task Conectar_y_desconectar_un_nodo_no_toca_los_demas()
    {
        var cluster = _banco.Cluster();
        cluster.Indicativo = "EA8DLF";
        cluster.NodoConocido = cluster.NodosConocidos.First(n => n.Servidor == "dxfun.com");
        cluster.AnadirConocidoCommand.Execute(null);

        // Sin aplicar, el nodo nuevo aun no existe en la fuente: se dice, no se hace nada.
        await cluster.ConectarNodoCommand.ExecuteAsync(cluster.Nodos[1]);
        cluster.Parte.Should().Contain("antes de conectar");

        await cluster.AplicarCommand.ExecuteAsync(null);
        await cluster.ConectarNodoCommand.ExecuteAsync(cluster.Nodos[0]);
        await cluster.ConectarNodoCommand.ExecuteAsync(cluster.Nodos[1]);
        cluster.Nodos.Should().OnlyContain(n => n.Estado == Aplicacion.Puertos.EstadoDeConexion.Conectado);

        await cluster.DesconectarNodoCommand.ExecuteAsync(cluster.Nodos[0]);

        cluster.Nodos[0].Estado.Should().Be(Aplicacion.Puertos.EstadoDeConexion.Desconectado);
        cluster.Nodos[1].Estado.Should().Be(Aplicacion.Puertos.EstadoDeConexion.Conectado);
        cluster.ResumenDeNodos.Should().Be("2 nodos · 1 conectados");
        await _banco.FuenteDelCluster.DesconectarAsync();
    }

    // ── Ajustes · CAT ──────────────────────────────────────────────────────

    [Fact]
    public async Task Descartar_del_cat_devuelve_via_velocidad_y_puerto()
    {
        var cat = _banco.Cat();
        var guardada = cat.Via;
        var velocidad = cat.Velocidad;
        var puerto = cat.Puerto;

        cat.Via = cat.Vias.First(v => v.Via == ViaDeControl.Rigctld);
        cat.Velocidad = cat.Velocidades[0];
        cat.PuertoDeRigctld = 9999;
        await cat.ProbarCommand.ExecuteAsync(null);
        cat.HayParte.Should().BeTrue();

        cat.DescartarCommand.Execute(null);

        cat.Via.Should().Be(guardada);
        cat.Velocidad.Should().Be(velocidad);
        cat.Puerto.Should().Be(puerto);
        cat.PuertoDeRigctld.Should().Be(4532);
        cat.HayParte.Should().BeFalse();
    }

    [Fact]
    public async Task Probar_el_cat_con_via_ninguna_no_monta_nada()
    {
        var montajes = 0;
        var cat = _banco.Cat((_, _, _) =>
        {
            montajes++;
            return Task.FromResult(new MontajeDeEquipo(new ControlDePapel(ViaDeControl.Ninguna), string.Empty));
        });
        cat.Via = cat.Vias.First(v => v.Via == ViaDeControl.Ninguna);

        await cat.ProbarCommand.ExecuteAsync(null);

        montajes.Should().Be(0);
        cat.Resultado.Should().Be(ResultadoDePrueba.Correcto);
        cat.EsCatNativo.Should().BeFalse();
        cat.HayEquipo.Should().BeFalse();
    }

    // ── Ajustes · audio ────────────────────────────────────────────────────

    [Fact]
    public async Task Probar_el_nivel_abre_la_entrada_y_parar_la_cierra()
    {
        var audio = _banco.Audio();

        audio.HayAudio.Should().BeTrue();
        audio.EntradaElegida.Should().NotBeNull("el codec del equipo sale elegido solo");
        audio.PararLaPruebaCommand.CanExecute(null).Should().BeFalse();

        await audio.ProbarElNivelCommand.ExecuteAsync(null);
        audio.Probando.Should().BeTrue();
        audio.ProbarElNivelCommand.CanExecute(null).Should().BeFalse();
        audio.PararLaPruebaCommand.CanExecute(null).Should().BeTrue();

        await audio.PararLaPruebaCommand.ExecuteAsync(null);
        audio.Probando.Should().BeFalse();
        audio.Nivel.Should().Be(0);
    }

    [Fact]
    public void Guardar_el_audio_lo_deja_en_el_fichero_y_descartar_lo_trae()
    {
        var audio = _banco.Audio();
        audio.FrecuenciaDeMuestreo = 24000;

        audio.GuardarCommand.Execute(null);
        audio.Parte.Should().Be("Guardado.");
        AjustesDelPrograma.Leer(_banco.Carpeta).Digital.FrecuenciaDeMuestreo.Should().Be(24000);

        audio.FrecuenciaDeMuestreo = 96000;
        audio.DescartarCommand.Execute(null);
        audio.FrecuenciaDeMuestreo.Should().Be(24000);

        audio.RefrescarCommand.Execute(null);
        audio.Entradas.Should().NotBeEmpty();
    }

    // ── Satélites ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Actualizar_elementos_trae_los_de_la_red_y_los_deja_en_disco()
    {
        var satelites = _banco.Satelites();
        var so50 = satelites.Satelites.Single(f => f.Abreviatura == "SO-50");
        so50.Cargado.Should().BeFalse();

        satelites.ActualizarElementosCommand.CanExecute(null).Should().BeTrue();
        await satelites.ActualizarElementosCommand.ExecuteAsync(null);

        _banco.Red.Preguntas.Should().NotBeEmpty("es el único botón que sale a la red");
        so50.Cargado.Should().BeTrue();
        so50.ProximoPasoTexto.Should().NotBe("Sin elementos orbitales cargados.");
        satelites.AvisoElementos.Should().StartWith("Actualizado a las 13:45 UTC");
        satelites.ActualizandoElementos.Should().BeFalse();
        Directory.GetFiles(Path.Combine(_banco.Carpeta, "satelites")).Should().NotBeEmpty();

        // Y la próxima vez se lee del disco, sin red.
        var otra = _banco.Satelites();
        await otra.CargarCacheAsync();
        otra.Satelites.Single(f => f.Abreviatura == "SO-50").Cargado.Should().BeTrue();
    }

    [Fact]
    public async Task Seguir_el_doppler_escribe_los_dos_vfo_y_soltar_para()
    {
        var satelites = _banco.Satelites();
        await satelites.ActualizarElementosCommand.ExecuteAsync(null);

        satelites.SeguirDopplerCommand.CanExecute(null).Should().BeFalse("sin satélite elegido");
        satelites.SateliteSeleccionado = satelites.Satelites.Single(f => f.Abreviatura == "SO-50");
        satelites.TranspondedorSeleccionado.Should().NotBeNull();
        satelites.SobreElHorizonte.Should().BeTrue("en mitad de un paso comprobado");
        satelites.AvisoDePosicion.Should().BeEmpty();
        satelites.HayEquipoConDosVfos.Should().BeTrue();

        satelites.SeguirDopplerCommand.CanExecute(null).Should().BeTrue();
        satelites.SeguirDopplerCommand.Execute(null);
        satelites.SiguiendoDoppler.Should().BeTrue();

        await satelites.LatirAsync();
        _banco.EquipoConDosVfos.Escrituras.Select(e => e.Vfo).Should().BeEquivalentTo([NombreDeVfo.B, NombreDeVfo.A]);
        _banco.EquipoConDosVfos.PttsPedidos.Should().Be(0, "el Doppler nunca toca el PTT");
        satelites.DesplazamientoBajadaTexto.Should().EndWith("Hz");

        satelites.SoltarDopplerCommand.Execute(null);
        satelites.SiguiendoDoppler.Should().BeFalse();
        var escritas = _banco.EquipoConDosVfos.Escrituras.Count;
        _banco.Hora.Ahora = _banco.Hora.Ahora.AddSeconds(10);
        await satelites.LatirAsync();
        _banco.EquipoConDosVfos.Escrituras.Should().HaveCount(escritas, "soltado ya no se escribe");
    }

    [Fact]
    public void Sin_elementos_no_se_empieza_a_seguir_y_se_dice_por_que()
    {
        var satelites = _banco.Satelites();
        satelites.SateliteSeleccionado = satelites.Satelites.Single(f => f.Abreviatura == "SO-50");

        satelites.SeguirDopplerCommand.Execute(null);

        satelites.SiguiendoDoppler.Should().BeFalse();
        satelites.AvisoDoppler.Should().Contain("Actualizar elementos orbitales");
        satelites.AvisoDePosicion.Should().StartWith("Sin elementos orbitales", "no es lo mismo que estar bajo el horizonte");
    }

    [Fact]
    public async Task Subida_y_bajada_por_el_mismo_vfo_no_se_sigue()
    {
        var satelites = _banco.Satelites();
        await satelites.ActualizarElementosCommand.ExecuteAsync(null);
        satelites.SateliteSeleccionado = satelites.Satelites.Single(f => f.Abreviatura == "SO-50");

        satelites.VfoDeBajada = NombreDeVfo.A;
        satelites.VfoDeSubida = NombreDeVfo.A;
        satelites.SeguirDopplerCommand.Execute(null);

        satelites.SiguiendoDoppler.Should().BeFalse();
        satelites.AvisoDoppler.Should().Contain("VFO distinto");
        AjustesDelPrograma.Leer(_banco.Carpeta).Satelites.VfoDeBajada.Should().Be(NombreDeVfo.A, "el reparto se guarda");
    }

    [Fact]
    public void Con_un_equipo_de_un_solo_vfo_seguir_sale_apagado()
    {
        var satelites = _banco.Satelites(new ControlDePapel(ViaDeControl.Rigctld));
        satelites.SateliteSeleccionado = satelites.Satelites.Single(f => f.Abreviatura == "SO-50");

        satelites.HayEquipoConDosVfos.Should().BeFalse();
        satelites.SeguirDopplerCommand.CanExecute(null).Should().BeFalse();
        satelites.SoltarDopplerCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Actualizar_pasos_recalcula_sin_salir_a_la_red()
    {
        var satelites = _banco.Satelites();

        satelites.RefrescarPasosCommand.Execute(null);

        _banco.Red.Preguntas.Should().BeEmpty();
        satelites.Satelites.Should().Contain(f => f.EsGeoestacionario && f.Cargado);
    }

    // ── Imprimir ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Buscar_marcar_y_la_vista_previa_con_la_frase_de_ahora()
    {
        var impresion = _banco.Impresion();
        impresion.MarcarTodasCommand.CanExecute(null).Should().BeFalse("sin etiquetas no hay qué marcar");
        impresion.VistaPreviaCommand.CanExecute(null).Should().BeFalse();

        impresion.SoloPendientesDeEnviar = false;
        await impresion.BuscarCommand.ExecuteAsync(null);

        impresion.Etiquetas.Should().NotBeEmpty();
        impresion.VistaPreviaCommand.CanExecute(null).Should().BeTrue();

        // Desmarcar a mano: el resumen y el botón se enteran.
        impresion.Etiquetas[0].Elegida = false;
        impresion.ResumenTexto.Should().Contain($"{impresion.Etiquetas.Count - 1} marcada(s)");

        impresion.DesmarcarTodasCommand.Execute(null);
        impresion.VistaPreviaCommand.CanExecute(null).Should().BeFalse("con todas desmarcadas no hay nada que imprimir");
        impresion.MarcarTodasCommand.Execute(null);
        impresion.VistaPreviaCommand.CanExecute(null).Should().BeTrue();

        impresion.Mensaje = "Gracias por el QSO";
        impresion.EmpezarEnLaEtiqueta = 3;
        await impresion.VistaPreviaCommand.ExecuteAsync(null);

        _banco.Generador.Pedidos.Should().ContainSingle();
        _banco.Generador.Pedidos[0].Should().OnlyContain(e => e.Mensaje == "Gracias por el QSO");
        _banco.Generador.UltimasOpciones!.PrimeraCasilla.Should().Be(2);
        _banco.DocumentosAbiertos.Should().ContainSingle();
        File.Exists(_banco.DocumentosAbiertos[0]).Should().BeTrue();
        impresion.Aviso.Should().StartWith("Vista previa abierta");
    }

    [Fact]
    public void La_casilla_de_inicio_no_se_sale_de_la_hoja()
    {
        var impresion = _banco.Impresion();

        impresion.EmpezarEnLaEtiqueta = 999;
        impresion.EmpezarEnLaEtiqueta.Should().Be(impresion.PlantillaElegida.PorHoja);

        impresion.EmpezarEnLaEtiqueta = 0;
        impresion.EmpezarEnLaEtiqueta.Should().Be(1);
    }

    [Fact]
    public async Task Un_filtro_que_no_deja_nada_lo_dice()
    {
        var impresion = _banco.Impresion();
        impresion.SoloPendientesDeEnviar = false;
        impresion.Indicativos = "ZZ9ZZZ";

        await impresion.BuscarCommand.ExecuteAsync(null);

        impresion.Etiquetas.Should().BeEmpty();
        impresion.Aviso.Should().Be("Ningún contacto cumple el filtro.");
    }

    // ── Ronda ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Una_ronda_entera_de_abrir_a_cerrar()
    {
        var ronda = _banco.Ronda();
        await ronda.CargarAsync();
        var antes = await _banco.Cuaderno.ContarAsync();

        ronda.Bandas.Should().Contain(ronda.Banda, "la banda de partida está en el desplegable");
        ronda.Modos.Should().Contain(ronda.Modo);
        ronda.AbrirCommand.CanExecute(null).Should().BeFalse("sin nombre no se abre");

        ronda.NombreRonda = "Ronda canaria";
        ronda.Frecuencia = "siete";
        await ronda.AbrirCommand.ExecuteAsync(null);
        ronda.HayRondaAbierta.Should().BeFalse();
        ronda.Tono.Should().Be(TonoDeMensaje.Error);
        ronda.Mensaje.Should().Contain("no se entiende");

        ronda.Frecuencia = "7,150";
        await ronda.AbrirCommand.ExecuteAsync(null);
        ronda.HayRondaAbierta.Should().BeTrue();
        ronda.TituloDeLaRonda.Should().Be("Ronda canaria · 40m · SSB · 7.15 MHz");
        ronda.AbrirCommand.CanExecute(null).Should().BeFalse("ya hay una abierta");

        ronda.AnadirParticipanteCommand.CanExecute(null).Should().BeFalse("sin indicativo");
        ronda.IndicativoNuevo = "EA8ABC";
        await ronda.AnadirParticipanteCommand.ExecuteAsync(null);
        ronda.Participantes.Should().ContainSingle();
        ronda.IndicativoNuevo.Should().BeEmpty();
        ronda.Mensaje.Should().Contain("EA8ABC");

        var fila = ronda.Participantes[0];
        ronda.ParticipanteElegido.Should().Be(fila);
        fila.RstEnviadoTexto = "59";
        fila.RstRecibidoTexto = "57";
        fila.Comentario = "desde La Palma";
        await ronda.GuardarParticipanteCommand.ExecuteAsync(null);
        ronda.Mensaje.Should().StartWith("Guardado el RST");

        ronda.MarcarTrabajadoCommand.CanExecute(null).Should().BeTrue();
        await ronda.MarcarTrabajadoCommand.ExecuteAsync(null);
        fila.Trabajado.Should().BeTrue();
        (await _banco.Cuaderno.ContarAsync()).Should().Be(antes + 1);
        ronda.MarcarTrabajadoCommand.CanExecute(null).Should().BeFalse("un segundo clic no mete otro contacto");

        var nuevo = (await _banco.Cuaderno.BuscarAsync(new CriterioQso { Call = "EA8ABC" }, 0, 10)).Elementos.Single();
        nuevo.RstSent.Texto.Should().Be("59");
        nuevo.RstRcvd.Texto.Should().Be("57");
        nuevo.Comentario.Should().Be("desde La Palma");
        nuevo.Band.Nombre.Should().Be("40m");

        ronda.IndicativoNuevo = "no vale!";
        await ronda.AnadirParticipanteCommand.ExecuteAsync(null);
        ronda.Tono.Should().Be(TonoDeMensaje.Error);
        ronda.Participantes.Should().ContainSingle();

        ronda.ParticipanteElegido = null;
        ronda.EliminarParticipanteCommand.CanExecute(null).Should().BeFalse("sin nadie elegido no se quita a nadie");
        ronda.GuardarParticipanteCommand.CanExecute(null).Should().BeFalse();
        ronda.MarcarTrabajadoCommand.CanExecute(null).Should().BeFalse();
        ronda.ParticipanteElegido = fila;
        await ronda.EliminarParticipanteCommand.ExecuteAsync(null);
        ronda.Participantes.Should().BeEmpty();
        ronda.Mensaje.Should().Contain("sigue en el cuaderno");

        await ronda.CerrarCommand.ExecuteAsync(null);
        ronda.HayRondaAbierta.Should().BeFalse();
        ronda.Historial.Should().ContainSingle();
        ronda.Historial[0].FinUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Una_ronda_que_quedo_abierta_se_reabre_al_cargar()
    {
        var gestionar = new Aplicacion.CasosDeUso.GestionarRonda(
            new Desarrollo.RepositorioRondasEnMemoria(),
            new Aplicacion.CasosDeUso.RegistrarQso(_banco.Cuaderno, _banco.Estaciones),
            _banco.Dxcc);
        await gestionar.AbrirAsync(new Aplicacion.CasosDeUso.PeticionDeRonda
        {
            Nombre = "De ayer",
            Band = Banda.Parse("80m"),
            Mode = Modo.Parse("SSB"),
        });

        var ronda = new VistaModeloRonda(gestionar, _banco.Estaciones);
        await ronda.CargarAsync();

        ronda.HayRondaAbierta.Should().BeTrue();
        ronda.NombreRonda.Should().Be("De ayer");
        ronda.Banda.Should().Be("80m");
        ronda.Estaciones.Should().NotBeEmpty();
        ronda.EstacionElegida.Should().NotBeNull();
    }

    private sealed class EscritorQueFalla : IEscritorAdif
    {
        public async Task EscribirAsync(
            IAsyncEnumerable<Qso> qsos,
            Stream destino,
            OpcionesAdif? opciones = null,
            CancellationToken ct = default)
        {
            await destino.WriteAsync("<ADIF_VER:5>3.1.4"u8.ToArray(), ct);
            throw new IOException("disco lleno");
        }
    }
}
