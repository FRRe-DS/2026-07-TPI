using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using M8.Notificaciones.Api.Features.Documentos.Dtos; // pal qrResponse
using QRCoder;
using StackExchange.Redis;

namespace M8.Notificaciones.Api.Features.Documentos.Services;

public class QrService : IQrService
{
    // Claves en Redis:
    //   qr:token:{hash} -> JSON (RegistroQr). TTL largo para poder distinguir
    //                      "usado / reemplazado / expirado" de "no existe" (404 vs 409 vs 422).
    //   qr:viaje:{id}   -> hash del QR vigente del viaje. TTL = vida del QR.
    private static readonly TimeSpan TtlRegistro = TimeSpan.FromHours(24);
    private const int MaxIntentos = 5;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // "Expirado" no se guarda: se deduce comparando ExpiraEn con la hora actual.
    private enum EstadoQr { Vigente, Usado, Reemplazado }

    private record RegistroQr(
        Guid QrId,
        Guid ViajeId,
        EstadoQr Estado,
        DateTime ExpiraEn,
        DateTime? UsadoEn = null);

    private readonly IConnectionMultiplexer _redis;

    public QrService(IConnectionMultiplexer redis) => _redis = redis; // consttruct

    public async Task<QrResponse> GenerarAsync(Guid viajeId, Guid clienteId, int validezSegundos)
    {
        var db = _redis.GetDatabase();
        var duracion = TimeSpan.FromSeconds(Math.Clamp(validezSegundos, 30, 3600));
        var expiraEn = DateTime.UtcNow.Add(duracion);

        var viajeKey = $"qr:viaje:{viajeId}";
        var qrId = Guid.NewGuid();
        var token = NuevoToken();
        var hash = Hash(token);
        var nuevo = new RegistroQr(qrId, viajeId, EstadoQr.Vigente, expiraEn);

        // El anterior pasa a REEMPLAZADO y se crea el nuevo en una sola transaccion.
        // Si otro request toca las mismas claves en el medio, se reintenta.
        for (var intento = 0; intento < MaxIntentos; intento++)
        {
            var hashAnterior = await db.StringGetAsync(viajeKey);
            var tran = db.CreateTransaction();

            if (hashAnterior.HasValue)
            {
                tran.AddCondition(Condition.StringEqual(viajeKey, hashAnterior));

                var anteriorKey = $"qr:token:{hashAnterior}";
                var jsonAnterior = await db.StringGetAsync(anteriorKey);
                var anterior = jsonAnterior.HasValue ? Deserializar(jsonAnterior.ToString()) : null;

                if (anterior is { Estado: EstadoQr.Vigente })
                {
                    tran.AddCondition(Condition.StringEqual(anteriorKey, jsonAnterior));
                    _ = tran.StringSetAsync(anteriorKey,
                        Serializar(anterior with { Estado = EstadoQr.Reemplazado }), TtlRegistro);
                }
            }
            else
            {
                tran.AddCondition(Condition.KeyNotExists(viajeKey));
            }

            _ = tran.StringSetAsync($"qr:token:{hash}", Serializar(nuevo), TtlRegistro);
            _ = tran.StringSetAsync(viajeKey, hash, duracion);

            if (await tran.ExecuteAsync())
            {
                return new QrResponse(
                    QrId: qrId,
                    ViajeId: viajeId,
                    Estado: "VIGENTE",
                    Token: token,
                    ImagenPng: "data:image/png;base64," + Convert.ToBase64String(GenerarPng(token)),
                    ExpiraEn: expiraEn);
            }
        }

        throw new InvalidOperationException("No se pudo generar el QR por concurrencia, reintentar.");
    }

    public async Task<ResultadoValidacionQr> ConsumirAsync(string token, Guid viajeId)
    {
        var db = _redis.GetDatabase();
        var tokenKey = $"qr:token:{Hash(token)}";

        var json = await db.StringGetAsync(tokenKey);
        if (!json.HasValue) return new(EstadoValidacionQr.NoExiste);

        var registro = Deserializar(json.ToString());
        if (registro is null) return new(EstadoValidacionQr.NoExiste);

        // Primero el viaje: asi un token ajeno no revela si esta usado o reemplazado.
        if (registro.ViajeId != viajeId) return new(EstadoValidacionQr.ViajeNoCoincide);

        if (registro.Estado == EstadoQr.Usado) return new(EstadoValidacionQr.YaUsado);
        if (registro.Estado == EstadoQr.Reemplazado) return new(EstadoValidacionQr.Reemplazado);

        var ahora = DateTime.UtcNow;
        if (ahora > registro.ExpiraEn) return new(EstadoValidacionQr.Expirado);

        // Consumir de forma atomica: solo si el registro sigue exactamente como se leyo.
        // Ante dos validaciones concurrentes, una sola gana y la otra recibe 409.
        // Como el registro sigue VIGENTE, qr:viaje apunta a este mismo QR (o ya vencio),
        // asi que borrarlo es seguro.
        var usado = registro with { Estado = EstadoQr.Usado, UsadoEn = ahora };

        var tran = db.CreateTransaction();
        tran.AddCondition(Condition.StringEqual(tokenKey, json));
        _ = tran.StringSetAsync(tokenKey, Serializar(usado), TtlRegistro);
        _ = tran.KeyDeleteAsync($"qr:viaje:{registro.ViajeId}");

        if (!await tran.ExecuteAsync()) return new(EstadoValidacionQr.YaUsado);

        return new(EstadoValidacionQr.Exito, new ValidacionQr(registro.QrId, registro.ViajeId, ahora));
    }

    private static string Serializar(RegistroQr registro) => JsonSerializer.Serialize(registro, JsonOpts);

    // Registros viejos (formato anterior: solo el viajeId) o corruptos se tratan como inexistentes.
    private static RegistroQr? Deserializar(string json)
    {
        try { return JsonSerializer.Deserialize<RegistroQr>(json, JsonOpts); }
        catch (JsonException) { return null; }
    }

    private static string NuevoToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static byte[] GenerarPng(string contenido)
    {
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(datos).GetGraphic(10);
    }
}