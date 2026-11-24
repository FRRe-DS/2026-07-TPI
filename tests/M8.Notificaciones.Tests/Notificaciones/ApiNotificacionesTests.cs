using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Tests.Notificaciones.Apoyo;

namespace M8.Notificaciones.Tests.Notificaciones;

/// <summary>
/// Pruebas de integración de la API de M8 (Notificaciones + Eventos) contra el
/// pipeline HTTP real: autenticación JWT, roles, X-Request-Id, problem+json,
/// idempotencia y los códigos del contrato.
/// </summary>
public class ApiNotificacionesTests : IClassFixture<ApiNotificacionesTests.Api>
{
    public sealed class Api : ApiDePrueba { }

    private readonly Api _api;

    public ApiNotificacionesTests(Api api) => _api = api;

    private static object EventoJson(string tipo = "VIAJE_ASIGNADO", Guid? eventoId = null, string email = "cliente@ejemplo.com",
        Guid? clienteId = null) => new
    {
        eventoId = eventoId ?? Guid.NewGuid(),
        tipo,
        origen = "M5",
        ocurridoEn = "2026-11-18T15:00:00Z",
        viajeId = Guid.NewGuid(),
        destinatarios = new object[]
        {
            new { usuarioId = clienteId ?? Guid.NewGuid(), rol = "CLIENTE", email, canalesPreferidos = new[] { "EMAIL" } },
            new { usuarioId = Guid.NewGuid(), rol = "CONDUCTOR", telefono = "+5493624000000" }
        },
        detalle = new { origen = "Av. Sarmiento 100", destino = "Plaza 25 de Mayo", tipoVehiculo = "AUTO" }
    };

    private static async Task AssertProblema(HttpResponseMessage r, HttpStatusCode codigo, string? tipo = "about:blank", string? titulo = null)
    {
        Assert.Equal(codigo, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var cuerpo = await r.LeerAsync();
        Assert.Equal((int)codigo, cuerpo.GetProperty("status").GetInt32());
        if (tipo is not null) Assert.Equal(tipo, cuerpo.GetProperty("type").GetString());
        if (titulo is not null) Assert.Equal(titulo, cuerpo.GetProperty("title").GetString());
        Assert.True(r.Headers.Contains("X-Request-Id"));
    }

    // ------------------------------- Salud y correlación -------------------------------

    [Fact]
    public async Task Health_responde_ok_sin_token()
    {
        var r = await _api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("ok", (await r.LeerAsync()).GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Devuelve_el_X_Request_Id_recibido_o_genera_uno_si_es_invalido()
    {
        var cliente = _api.CreateClient();

        var conId = new HttpRequestMessage(HttpMethod.Get, "/health");
        conId.Headers.Add("X-Request-Id", "prueba-123.abc_X");
        var r1 = await cliente.SendAsync(conId);
        Assert.Equal("prueba-123.abc_X", r1.Headers.GetValues("X-Request-Id").Single());

        var invalido = new HttpRequestMessage(HttpMethod.Get, "/health");
        invalido.Headers.Add("X-Request-Id", "tiene espacios y ñ");
        var r2 = await cliente.SendAsync(invalido);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode); // nunca 400 por este encabezado
        var generado = r2.Headers.GetValues("X-Request-Id").Single();
        Assert.Matches("^[A-Za-z0-9._-]{1,128}$", generado);
    }

    // ------------------------------- Seguridad (RNF-12) -------------------------------

    [Fact]
    public async Task Sin_token_responde_401_en_problem_json()
    {
        var r = await _api.CreateClient().PostAsJsonAsync("/api/v1/eventos", EventoJson());
        await AssertProblema(r, HttpStatusCode.Unauthorized, titulo: "No autenticado");
    }

    [Fact]
    public async Task Un_token_de_otro_emisor_o_vencido_no_sirve()
    {
        var cliente = _api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiDePrueba.Token(new[] { "viajes-be" }, emisor: "https://otro-emisor"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsJsonAsync("/api/v1/eventos", EventoJson())).StatusCode);

        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            ApiDePrueba.Token(new[] { "viajes-be" }, vence: DateTime.UtcNow.AddMinutes(-10)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsJsonAsync("/api/v1/eventos", EventoJson())).StatusCode);
    }

    [Theory]
    [InlineData("CLIENTE")]
    [InlineData("OPERADOR")]
    [InlineData("clientes-be")] // M2 no es uno de los módulos que informan hitos
    public async Task Eventos_solo_acepta_rol_SERVICIO(string rol)
    {
        var r = await _api.ClienteCon(rol).PostAsJsonAsync("/api/v1/eventos", EventoJson());
        await AssertProblema(r, HttpStatusCode.Forbidden, titulo: "Sin permiso");
    }

    [Fact]
    public async Task Listar_notificaciones_no_lo_puede_hacer_un_CLIENTE()
    {
        var r = await _api.ClienteCon("CLIENTE").GetAsync("/api/v1/notificaciones");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // --------------------------------- POST /eventos ---------------------------------

    [Fact]
    public async Task Un_evento_valido_responde_202_y_genera_las_notificaciones()
    {
        var eventoId = Guid.NewGuid();
        var r = await _api.ClienteServicio().PostAsJsonAsync("/api/v1/eventos", EventoJson(eventoId: eventoId));

        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var cuerpo = await r.LeerAsync();
        Assert.Equal(eventoId, cuerpo.GetProperty("eventoId").GetGuid());
        Assert.True(cuerpo.TryGetProperty("recibidoEn", out _));

        var lista = await (await _api.ClienteOperador().GetAsync($"/api/v1/notificaciones?size=100")).LeerAsync();
        var delEvento = lista.GetProperty("items").EnumerateArray()
            .Where(n => n.TryGetProperty("eventoId", out var e) && e.GetGuid() == eventoId).ToList();
        Assert.Equal(2, delEvento.Count);
        Assert.All(delEvento, n =>
        {
            Assert.Equal("PENDIENTE", n.GetProperty("estado").GetString());
            Assert.Equal("VIAJE_ASIGNADO", n.GetProperty("tipoEvento").GetString());
            Assert.Equal("VIAJE", n.GetProperty("referencia").GetProperty("tipo").GetString());
            Assert.False(n.TryGetProperty("contacto", out _)); // nunca se expone el email/teléfono
        });
    }

    [Fact]
    public async Task Repetir_el_evento_devuelve_la_misma_respuesta_y_otro_cuerpo_da_422()
    {
        var cliente = _api.ClienteServicio();
        var eventoId = Guid.NewGuid();
        var cuerpo = EventoJson(eventoId: eventoId);

        var r1 = await cliente.PostAsJsonAsync("/api/v1/eventos", cuerpo);
        var r2 = await cliente.PostAsJsonAsync("/api/v1/eventos", cuerpo);
        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, r2.StatusCode);
        var operador = await (await _api.ClienteOperador().GetAsync("/api/v1/notificaciones?size=100")).LeerAsync();
        Assert.Equal(2, operador.GetProperty("items").EnumerateArray()
            .Count(n => n.TryGetProperty("eventoId", out var e) && e.GetGuid() == eventoId)); // no se duplicaron

        // EventoJson genera otro viajeId: mismo eventoId con otro cuerpo → 422.
        var r3 = await cliente.PostAsJsonAsync("/api/v1/eventos", EventoJson(eventoId: eventoId));
        await AssertProblema(r3, HttpStatusCode.UnprocessableEntity, "/errores/evento-id-reutilizado");
    }

    [Fact]
    public async Task El_mismo_cuerpo_exacto_dos_veces_devuelve_el_recibidoEn_original()
    {
        var cliente = _api.ClienteServicio();
        var json = System.Text.Json.JsonSerializer.Serialize(EventoJson());

        var r1 = await cliente.PostAsync("/api/v1/eventos", new StringContent(json, Encoding.UTF8, "application/json"));
        await Task.Delay(20);
        var r2 = await cliente.PostAsync("/api/v1/eventos", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, r2.StatusCode);
        Assert.Equal((await r1.LeerAsync()).GetProperty("recibidoEn").GetString(), (await r2.LeerAsync()).GetProperty("recibidoEn").GetString());
    }

    [Fact]
    public async Task Un_evento_que_no_cumple_el_contrato_da_400()
    {
        var cliente = _api.ClienteServicio();

        var sinViaje = new { eventoId = Guid.NewGuid(), tipo = "VIAJE_INICIADO", origen = "M6", ocurridoEn = "2026-11-18T15:00:00Z", reservaId = Guid.NewGuid() };
        await AssertProblema(await cliente.PostAsJsonAsync("/api/v1/eventos", sinViaje), HttpStatusCode.BadRequest, titulo: "Solicitud inválida");

        var tipoInexistente = new { eventoId = Guid.NewGuid(), tipo = "VIAJE_TELETRANSPORTADO", origen = "M6", ocurridoEn = "2026-11-18T15:00:00Z", viajeId = Guid.NewGuid() };
        await AssertProblema(await cliente.PostAsJsonAsync("/api/v1/eventos", tipoInexistente), HttpStatusCode.BadRequest);

        var jsonRoto = new StringContent("{ esto no es json", Encoding.UTF8, "application/json");
        await AssertProblema(await cliente.PostAsync("/api/v1/eventos", jsonRoto), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Otro_content_type_da_415_y_un_cuerpo_de_mas_de_1_MiB_da_413()
    {
        var cliente = _api.ClienteServicio();

        var texto = await cliente.PostAsync("/api/v1/eventos", new StringContent("hola", Encoding.UTF8, "text/plain"));
        await AssertProblema(texto, HttpStatusCode.UnsupportedMediaType, titulo: "Tipo de contenido no soportado");

        var grande = new StringContent(new string(' ', 1024 * 1024 + 10), Encoding.UTF8, "application/json");
        await AssertProblema(await cliente.PostAsync("/api/v1/eventos", grande), HttpStatusCode.RequestEntityTooLarge,
            titulo: "Cuerpo demasiado grande");
    }

    [Fact]
    public async Task Un_metodo_no_definido_da_405_con_Allow()
    {
        var r = await _api.ClienteServicio().DeleteAsync("/api/v1/eventos");

        await AssertProblema(r, HttpStatusCode.MethodNotAllowed, titulo: "Método no permitido");
        Assert.Contains("POST", r.Content.Headers.Allow);
    }

    // ------------------------------ GET /notificaciones ------------------------------

    [Fact]
    public async Task Consultar_una_notificacion_y_sus_intentos_despues_del_envio()
    {
        var eventoId = Guid.NewGuid();
        await _api.ClienteServicio().PostAsJsonAsync("/api/v1/eventos", EventoJson(eventoId: eventoId));
        await _api.DespacharAsync();

        var operador = _api.ClienteOperador();
        var lista = await (await operador.GetAsync("/api/v1/notificaciones?tipoEvento=VIAJE_ASIGNADO&canal=EMAIL&size=100")).LeerAsync();
        var item = lista.GetProperty("items").EnumerateArray().First(n => n.GetProperty("eventoId").GetGuid() == eventoId);
        var id = item.GetProperty("notificacionId").GetGuid();

        var detalle = await operador.GetAsync($"/api/v1/notificaciones/{id}");
        Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
        var n = await detalle.LeerAsync();
        Assert.Equal("ENVIADA", n.GetProperty("estado").GetString());
        Assert.Equal(1, n.GetProperty("intentos").GetInt32());
        Assert.True(n.TryGetProperty("enviadoEn", out _));

        // También lo puede consultar un SERVICIO (por ejemplo M6).
        var intentos = await (await _api.ClienteServicio().GetAsync($"/api/v1/notificaciones/{id}/intentos")).LeerAsync();
        var unico = Assert.Single(intentos.GetProperty("items").EnumerateArray());
        Assert.Equal(1, unico.GetProperty("numero").GetInt32());
        Assert.Equal("EXITO", unico.GetProperty("resultado").GetString());
        Assert.Equal("EMAIL", unico.GetProperty("canal").GetString());
        Assert.Equal(1, intentos.GetProperty("totalElementos").GetInt32());
        Assert.Equal(1, intentos.GetProperty("totalPaginas").GetInt32());
    }

    [Fact]
    public async Task Paginacion_y_filtros_respetan_los_limites_del_contrato()
    {
        var operador = _api.ClienteOperador();
        await AssertProblema(await operador.GetAsync("/api/v1/notificaciones?size=0"), HttpStatusCode.BadRequest);
        await AssertProblema(await operador.GetAsync("/api/v1/notificaciones?size=101"), HttpStatusCode.BadRequest);
        await AssertProblema(await operador.GetAsync("/api/v1/notificaciones?page=-1"), HttpStatusCode.BadRequest);
        await AssertProblema(await operador.GetAsync("/api/v1/notificaciones?estado=PERDIDA"), HttpStatusCode.BadRequest);

        var pagina = await (await operador.GetAsync("/api/v1/notificaciones?page=0&size=1")).LeerAsync();
        Assert.Equal(0, pagina.GetProperty("page").GetInt32());
        Assert.Equal(1, pagina.GetProperty("size").GetInt32());
        Assert.True(pagina.GetProperty("items").GetArrayLength() <= 1);
    }

    [Fact]
    public async Task Un_id_que_no_es_UUID_da_400_y_uno_inexistente_404()
    {
        var operador = _api.ClienteOperador();
        await AssertProblema(await operador.GetAsync("/api/v1/notificaciones/no-es-uuid"), HttpStatusCode.BadRequest);
        await AssertProblema(await operador.GetAsync($"/api/v1/notificaciones/{Guid.NewGuid()}"), HttpStatusCode.NotFound, titulo: "No encontrado");
        await AssertProblema(await operador.GetAsync($"/api/v1/notificaciones/{Guid.NewGuid()}/intentos"), HttpStatusCode.NotFound);
    }

    // ----------------------------- POST /notificaciones -----------------------------

    private static object Manual(Guid? destinatario = null, string canal = "PUSH", string asunto = "Aviso del operador") => new
    {
        destinatarioId = destinatario ?? Guid.NewGuid(),
        rol = "CLIENTE",
        canal,
        asunto,
        mensaje = "Mañana hay corte de calles en el centro.",
        referencia = new { tipo = "VIAJE", id = Guid.NewGuid() }
    };

    [Fact]
    public async Task Notificacion_manual_exige_Idempotency_Key_y_rol_OPERADOR()
    {
        var sinClave = await _api.ClienteOperador().SendAsync(Json.Post("/api/v1/notificaciones", Manual()));
        await AssertProblema(sinClave, HttpStatusCode.BadRequest);

        var servicio = await _api.ClienteServicio().SendAsync(Json.Post("/api/v1/notificaciones", Manual(), Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, servicio.StatusCode);
    }

    [Fact]
    public async Task Notificacion_manual_es_idempotente()
    {
        var operador = _api.ClienteOperador();
        var clave = Guid.NewGuid().ToString();
        var cuerpo = Manual();

        var r1 = await operador.SendAsync(Json.Post("/api/v1/notificaciones", cuerpo, clave));
        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);
        var n1 = await r1.LeerAsync();
        var id = n1.GetProperty("notificacionId").GetGuid();
        Assert.Equal($"/api/v1/notificaciones/{id}", r1.Headers.Location!.ToString());
        Assert.Equal("PENDIENTE", n1.GetProperty("estado").GetString());
        Assert.Equal("PUSH", n1.GetProperty("canal").GetString());
        Assert.False(n1.TryGetProperty("eventoId", out _)); // las manuales no tienen evento

        var r2 = await operador.SendAsync(Json.Post("/api/v1/notificaciones", cuerpo, clave));
        Assert.Equal(HttpStatusCode.Accepted, r2.StatusCode);
        Assert.Equal(id, (await r2.LeerAsync()).GetProperty("notificacionId").GetGuid());

        var r3 = await operador.SendAsync(Json.Post("/api/v1/notificaciones", Manual(asunto: "Otro"), clave));
        await AssertProblema(r3, HttpStatusCode.UnprocessableEntity, "/errores/idempotency-key-reutilizada");
    }

    [Fact]
    public async Task Notificacion_manual_invalida_da_400_y_libera_la_clave()
    {
        var operador = _api.ClienteOperador();
        var clave = Guid.NewGuid().ToString();
        var invalida = new { destinatarioId = Guid.NewGuid(), rol = "CLIENTE", canal = "EMAIL", asunto = "", mensaje = "x" };

        await AssertProblema(await operador.SendAsync(Json.Post("/api/v1/notificaciones", invalida, clave)), HttpStatusCode.BadRequest);
        // Corregido el cuerpo, la misma clave se puede usar (los errores no se recuerdan).
        var corregida = await operador.SendAsync(Json.Post("/api/v1/notificaciones", Manual(), clave));
        Assert.Equal(HttpStatusCode.Accepted, corregida.StatusCode);
    }

    [Fact]
    public async Task Notificacion_manual_por_EMAIL_usa_el_contacto_registrado_por_un_evento()
    {
        var clienteId = Guid.NewGuid();
        await _api.ClienteServicio().PostAsJsonAsync("/api/v1/eventos", EventoJson(clienteId: clienteId, email: "registrado@ejemplo.com"));

        var r = await _api.ClienteOperador().SendAsync(Json.Post("/api/v1/notificaciones", Manual(clienteId, "EMAIL"), Guid.NewGuid().ToString()));
        var id = (await r.LeerAsync()).GetProperty("notificacionId").GetGuid();
        await _api.DespacharAsync();

        Assert.Contains(_api.Proveedor.Enviados, m => m.NotificacionId == id && m.Contacto == "registrado@ejemplo.com");
    }

    [Fact]
    public async Task Notificacion_manual_por_SMS_sin_telefono_registrado_queda_FALLIDA()
    {
        var r = await _api.ClienteOperador().SendAsync(Json.Post("/api/v1/notificaciones", Manual(canal: "SMS"), Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var n = await r.LeerAsync();
        Assert.Equal("FALLIDA", n.GetProperty("estado").GetString());
        Assert.Contains("Sin contacto", n.GetProperty("ultimoError").GetString());
    }

    // ------------------------------------ Reintentos ------------------------------------

    [Fact]
    public async Task Reintentar_una_FALLIDA_agrega_un_intento_y_en_otro_estado_da_409()
    {
        var eventoId = Guid.NewGuid();
        _api.Proveedor.Falla = m => m.Contacto == "rebota@ejemplo.com" ? "rebote" : null;
        await _api.ClienteServicio().PostAsJsonAsync("/api/v1/eventos", EventoJson(eventoId: eventoId, email: "rebota@ejemplo.com"));

        var operador = _api.ClienteOperador();
        var lista = await (await operador.GetAsync("/api/v1/notificaciones?canal=EMAIL&size=100")).LeerAsync();
        var id = lista.GetProperty("items").EnumerateArray().First(n => n.GetProperty("eventoId").GetGuid() == eventoId)
            .GetProperty("notificacionId").GetGuid();

        // PENDIENTE todavía: no se puede reintentar.
        var temprano = await operador.SendAsync(Json.Post($"/api/v1/notificaciones/{id}/reintentos", null, Guid.NewGuid().ToString()));
        await AssertProblema(temprano, HttpStatusCode.Conflict, "/errores/notificacion-no-reintentable");

        // Tres intentos fallidos (se adelanta el reloj para saltear las esperas).
        var ahora = DateTimeOffset.UtcNow;
        await _api.DespacharAsync(ahora);
        await _api.DespacharAsync(ahora.AddSeconds(6));
        await _api.DespacharAsync(ahora.AddSeconds(40));
        var fallida = await (await operador.GetAsync($"/api/v1/notificaciones/{id}")).LeerAsync();
        Assert.Equal("FALLIDA", fallida.GetProperty("estado").GetString());
        Assert.Equal(3, fallida.GetProperty("intentos").GetInt32());
        Assert.Equal("rebote", fallida.GetProperty("ultimoError").GetString());

        // El OPERADOR la reintenta: vuelve a PENDIENTE (idempotente con la misma clave).
        var clave = Guid.NewGuid().ToString();
        var r = await operador.SendAsync(Json.Post($"/api/v1/notificaciones/{id}/reintentos", null, clave));
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        Assert.Equal("PENDIENTE", (await r.LeerAsync()).GetProperty("estado").GetString());
        var repetido = await operador.SendAsync(Json.Post($"/api/v1/notificaciones/{id}/reintentos", null, clave));
        Assert.Equal(HttpStatusCode.Accepted, repetido.StatusCode);

        // Ahora el proveedor funciona: el cuarto intento sale bien y el historial conserva los anteriores.
        _api.Proveedor.Falla = _ => null;
        await _api.DespacharAsync(ahora.AddMinutes(5));
        var intentos = await (await operador.GetAsync($"/api/v1/notificaciones/{id}/intentos")).LeerAsync();
        var resultados = intentos.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("resultado").GetString()).ToList();
        Assert.Equal(new[] { "ERROR", "ERROR", "ERROR", "EXITO" }, resultados);
        var enviada = await (await operador.GetAsync($"/api/v1/notificaciones/{id}")).LeerAsync();
        Assert.Equal("ENVIADA", enviada.GetProperty("estado").GetString());
        Assert.False(enviada.TryGetProperty("ultimoError", out _));
    }

    [Fact]
    public async Task Reintentar_una_notificacion_inexistente_da_404()
    {
        var r = await _api.ClienteOperador().SendAsync(Json.Post($"/api/v1/notificaciones/{Guid.NewGuid()}/reintentos", null, "clave-1"));
        await AssertProblema(r, HttpStatusCode.NotFound);
    }

    [Fact]
    public void Los_enums_se_serializan_con_los_nombres_del_contrato()
    {
        Assert.Equal("\"VIAJE_ARRIBO_CONDUCTOR\"", System.Text.Json.JsonSerializer.Serialize(TipoEvento.VIAJE_ARRIBO_CONDUCTOR));
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<Canal>("1"));
    }
}
