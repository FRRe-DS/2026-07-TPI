using System.Security.Cryptography;
using System.Text;
using M8.Notificaciones.Api.Features.Documentos.Dtos; // pal qrResponse
using QRCoder;
using StackExchange.Redis;

namespace M8.Notificaciones.Api.Features.Documentos.Services;

public class QrService: IQrService
{
    private readonly IConnectionMultiplexer _redis;

    public QrService(IConnectionMultiplexer redis)=> _redis = redis; // consttruct

    public async Task<QrResponse> GenerarAsync(Guid viajeId, Guid clienteId, int validezSegundos)
    {
        //preprar la conexion, con math.clamp fuerzas que sea entre 1-30
        var db = _redis.GetDatabase();
        var ttl = TimeSpan.FromMinutes(Math.Clamp(validezSegundos,30,3600)); 

        // invalidar el anterior

        var viajeKey = $"qr:viaje:{viajeId}";
        var hashAnterior = await db.StringGetDeleteAsync(viajeKey);
        if (hashAnterior.HasValue)
        {
            await db.KeyDeleteAsync($"qr:token:{hashAnterior}");
        }

        // crear el token 

        var token = NuevoToken();
        var hash = Hash(token);

        await db.StringSetAsync($"qr:token:{hash}", viajeId.ToString(), ttl);
        await db.StringSetAsync(viajeKey, hash, ttl);

        return new QrResponse(
            QrId: Guid.NewGuid(),
            ViajeId: viajeId,
            Estado: "VIGENTE",
            Token: token,
            ImagenPng: "data:image/png;base64," + Convert.ToBase64String(GenerarPng(token)),
            ExpiraEn: DateTime.UtcNow.Add(ttl));
    }

    public async Task<string?> ConsumirAsync(string token)
    {
        var db = _redis.GetDatabase();
        var viajeId = await db.StringGetDeleteAsync($"qr:token:{Hash(token)}");
        if (!viajeId.HasValue) return null;

        await db.KeyDeleteAsync($"qr:viaje:{viajeId}");
        return viajeId.ToString();

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

