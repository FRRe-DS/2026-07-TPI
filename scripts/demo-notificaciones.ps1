<#
.SYNOPSIS
    Demo de punta a punta de Notificaciones de M8 (TP2).

.DESCRIPTION
    Requisitos: M8 levantado (docker compose up --build) y el Keycloak de la
    cátedra corriendo en el puerto 8080 (repo FRRe-DS/2026-TPI, carpeta keycloak).

    Parte 1 - Seguridad: pide un token a Keycloak (client_credentials del client
              grupo-07) y muestra que sin token la API responde 401.
    Parte 2 - HTTP: un servicio informa VIAJE_ASIGNADO por POST /api/v1/eventos
              (202) y se repite el mismo evento (no se duplica).
    Parte 3 - RabbitMQ: se publica RESERVA_CONFIRMADA en el exchange
              movilidad.eventos (API HTTP de administración de RabbitMQ).
    Parte 4 - Envío asíncrono: las notificaciones pasan de PENDIENTE a ENVIADA
              y el correo aparece en Mailpit (http://localhost:8025).
    Parte 5 - Reintentos: un email que el proveedor rechaza (cliente@falla.test)
              hace 3 intentos con espera creciente, queda FALLIDA y el
              OPERADOR la reintenta.

.PARAMETER ClientSecret
    Secreto del client grupo-07 en el realm de la cátedra. Si no se pasa, se
    usa la variable KEYCLOAK_CLIENT_SECRET (por ejemplo, desde el .env).

.EXAMPLE
    .\scripts\demo-notificaciones.ps1 -ClientSecret <secreto> -SinPausa
#>
param(
    [string]$BaseUrl = "http://localhost:8088",
    [string]$KeycloakUrl = "http://localhost:8080/realms/ds-2026-realm",
    [string]$ClientId = "grupo-07",
    [string]$ClientSecret = $env:KEYCLOAK_CLIENT_SECRET,
    [string]$RabbitAdminUrl = "http://localhost:15672",
    [string]$RabbitUser = $env:RABBITMQ_USER,
    [string]$RabbitPassword = $env:RABBITMQ_PASSWORD,
    [string]$Exchange = "movilidad.eventos",
    [string]$MailpitUrl = "http://localhost:8025",
    [switch]$SinPausa
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')

# Completa lo que falte con el .env de la raíz del repo (sin mostrar los valores).
$archivoEnv = Join-Path $PSScriptRoot "..\.env"
if (Test-Path $archivoEnv) {
    $envVars = @{}
    Get-Content $archivoEnv | ForEach-Object { if ($_ -match '^\s*([A-Za-z_]+)=(.*)$') { $envVars[$Matches[1]] = $Matches[2].Trim() } }
    if (-not $ClientSecret) { $ClientSecret = $envVars["KEYCLOAK_CLIENT_SECRET"] }
    if (-not $RabbitUser) { $RabbitUser = $envVars["RABBITMQ_USER"] }
    if (-not $RabbitPassword) { $RabbitPassword = $envVars["RABBITMQ_PASSWORD"] }
}
if (-not $ClientSecret) { throw "Falta el secreto del client $ClientId (-ClientSecret o KEYCLOAK_CLIENT_SECRET en .env)." }

function Titulo([string]$t) { Write-Host ""; Write-Host ("=" * 78) -ForegroundColor Cyan; Write-Host $t -ForegroundColor Cyan; Write-Host ("=" * 78) -ForegroundColor Cyan }
function Paso([string]$t) { Write-Host ""; Write-Host ">> $t" -ForegroundColor Yellow }
function Pausa { if (-not $SinPausa) { Read-Host "Enter para seguir" | Out-Null } }
function Id { [guid]::NewGuid().ToString() }
function Mostrar($obj) { $obj | ConvertTo-Json -Depth 6 | Write-Host }

# Llama a la API y devuelve código y cuerpo, también en los errores.
function Llamar([string]$metodo, [string]$ruta, $cuerpo = $null, [hashtable]$encabezados = @{}) {
    $params = @{ Method = $metodo; Uri = "$BaseUrl$ruta"; Headers = $encabezados; UseBasicParsing = $true }
    if ($null -ne $cuerpo) { $params.Body = ($cuerpo | ConvertTo-Json -Depth 6); $params.ContentType = "application/json" }
    try {
        $r = Invoke-WebRequest @params
        return [pscustomobject]@{ Codigo = [int]$r.StatusCode; Cuerpo = $(if ($r.Content) { $r.Content | ConvertFrom-Json } else { $null }) }
    }
    catch {
        $codigo = [int]$_.Exception.Response.StatusCode
        $texto = $_.ErrorDetails.Message
        if (-not $texto) {
            # Windows PowerShell 5.1 con respuestas "chunked": se lee el stream (igual que demo-cqrs-es.ps1).
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                try { $stream.Position = 0 } catch { }
                $texto = (New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)).ReadToEnd()
            } catch { $texto = $null }
        }
        return [pscustomobject]@{ Codigo = $codigo; Cuerpo = $(if ($texto) { $texto | ConvertFrom-Json } else { $null }) }
    }
}

# ------------------------------------------------------------------------------
Titulo "PARTE 1 - SEGURIDAD: token de Keycloak (M1) y roles"
Paso "Sin token, POST /api/v1/eventos responde 401 (problem+json)"
$r = Llamar POST "/api/v1/eventos" @{ eventoId = (Id) }
Write-Host "HTTP $($r.Codigo)"; Mostrar $r.Cuerpo

Paso "Token client_credentials de $ClientId (rol 'notificaciones-be' -> SERVICIO; en desarrollo también OPERADOR)"
$token = (Invoke-RestMethod -Method Post -Uri "$KeycloakUrl/protocol/openid-connect/token" -ContentType "application/x-www-form-urlencoded" `
    -Body @{ grant_type = "client_credentials"; client_id = $ClientId; client_secret = $ClientSecret }).access_token
Write-Host "Token obtenido ($($token.Length) caracteres)."
$auth = @{ Authorization = "Bearer $token" }
Pausa

# ------------------------------------------------------------------------------
Titulo "PARTE 2 - EVENTO POR HTTP: VIAJE_ASIGNADO"
$viajeId = Id; $clienteId = Id; $conductorId = Id; $eventoId = Id
$evento = [ordered]@{
    eventoId = $eventoId; tipo = "VIAJE_ASIGNADO"; origen = "M5"; ocurridoEn = (Get-Date).ToUniversalTime().ToString("o"); viajeId = $viajeId
    destinatarios = @(
        @{ usuarioId = $clienteId; rol = "CLIENTE"; email = "cliente.demo@ejemplo.com"; canalesPreferidos = @("EMAIL") },
        @{ usuarioId = $conductorId; rol = "CONDUCTOR"; telefono = "+5493624000000"; canalesPreferidos = @("SMS", "PUSH") }
    )
    detalle = @{ origen = "Av. Sarmiento 100"; destino = "Plaza 25 de Mayo"; tipoVehiculo = "AUTO" }
}
$reqId = @{ Authorization = "Bearer $token"; "X-Request-Id" = "demo-tp2-001" }
$r = Llamar POST "/api/v1/eventos" $evento $reqId
Write-Host "HTTP $($r.Codigo)  (X-Request-Id enviado: demo-tp2-001, se correlaciona en los logs)"; Mostrar $r.Cuerpo

Paso "El MISMO evento otra vez: 202 con la respuesta original, sin duplicar avisos"
$r2 = Llamar POST "/api/v1/eventos" $evento $auth
Write-Host "HTTP $($r2.Codigo) - recibidoEn igual al original: $($r2.Cuerpo.recibidoEn -eq $r.Cuerpo.recibidoEn)"
Pausa

# ------------------------------------------------------------------------------
Titulo "PARTE 3 - EVENTO POR RABBITMQ: RESERVA_CONFIRMADA"
$reservaId = Id
$eventoCola = [ordered]@{
    eventoId = (Id); tipo = "RESERVA_CONFIRMADA"; origen = "M9"; ocurridoEn = (Get-Date).ToUniversalTime().ToString("o"); reservaId = $reservaId
    destinatarios = @(@{ usuarioId = $clienteId; rol = "CLIENTE"; email = "cliente.demo@ejemplo.com" })
    detalle = @{ origen = "Casa"; destino = "Aeropuerto"; fechaProgramada = (Get-Date).AddDays(2).ToUniversalTime().ToString("o"); zonaHoraria = "America/Argentina/Buenos_Aires"; tipoVehiculo = "AUTO" }
}
$credRabbit = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("${RabbitUser}:${RabbitPassword}"))
$publicacion = @{
    properties = @{ content_type = "application/json"; delivery_mode = 2; correlation_id = "demo-tp2-002" }
    routing_key = "reserva.confirmada"; payload = ($eventoCola | ConvertTo-Json -Depth 6 -Compress); payload_encoding = "string"
} | ConvertTo-Json -Depth 6
$p = Invoke-RestMethod -Method Post -Uri "$RabbitAdminUrl/api/exchanges/%2F/$Exchange/publish" -Headers @{ Authorization = "Basic $credRabbit" } -ContentType "application/json" -Body $publicacion
Write-Host "Publicado en '$Exchange' con clave reserva.confirmada. ¿Enrutado a alguna cola?: $($p.routed)"
Pausa

# ------------------------------------------------------------------------------
Titulo "PARTE 4 - ENVÍO ASÍNCRONO: PENDIENTE -> ENVIADA"
Start-Sleep -Seconds 3
Paso "GET /api/v1/notificaciones?destinatarioId=<cliente>  (rol OPERADOR)"
$lista = Llamar GET "/api/v1/notificaciones?destinatarioId=$clienteId" $null $auth
$lista.Cuerpo.items | Select-Object tipoEvento, canal, estado, intentos, asunto | Format-Table -AutoSize | Out-String | Write-Host
Paso "Conductor (canales SMS y PUSH, simulados)"
(Llamar GET "/api/v1/notificaciones?destinatarioId=$conductorId" $null $auth).Cuerpo.items | Select-Object tipoEvento, canal, estado, intentos | Format-Table -AutoSize | Out-String | Write-Host
Paso "Correos recibidos por Mailpit ($MailpitUrl)"
try {
    (Invoke-RestMethod "$MailpitUrl/api/v1/messages?limit=5").messages | Select-Object @{n = "Para"; e = { $_.To[0].Address } }, Subject, Created | Format-Table -AutoSize | Out-String | Write-Host
} catch { Write-Host "No se pudo consultar Mailpit: $($_.Exception.Message)" }
Pausa

# ------------------------------------------------------------------------------
Titulo "PARTE 5 - REINTENTOS: 3 intentos con espera creciente, FALLIDA y reintento manual"
$malo = Id
$eventoMalo = [ordered]@{
    eventoId = (Id); tipo = "VIAJE_FINALIZADO"; origen = "M6"; ocurridoEn = (Get-Date).ToUniversalTime().ToString("o"); viajeId = $viajeId
    destinatarios = @(@{ usuarioId = $malo; rol = "CLIENTE"; email = "cliente@falla.test" })
}
Llamar POST "/api/v1/eventos" $eventoMalo $auth | Out-Null
$notif = (Llamar GET "/api/v1/notificaciones?destinatarioId=$malo" $null $auth).Cuerpo.items[0]
Write-Host "Notificación $($notif.notificacionId). Esperas configuradas: 5 s y 30 s (en total ~35 s)."
for ($i = 0; $i -lt 45; $i++) {
    Start-Sleep -Seconds 1
    $n = (Llamar GET "/api/v1/notificaciones/$($notif.notificacionId)" $null $auth).Cuerpo
    Write-Host -NoNewline "`r  t=$($i + 1)s  estado=$($n.estado)  intentos=$($n.intentos)     "
    if ($n.estado -eq "FALLIDA") { break }
}
Write-Host ""
Paso "Historial de intentos (no se borra ni se modifica)"
(Llamar GET "/api/v1/notificaciones/$($notif.notificacionId)/intentos" $null $auth).Cuerpo.items | Format-Table -AutoSize | Out-String | Write-Host

Paso "El OPERADOR la reintenta (Idempotency-Key obligatoria)"
$clave = Id
$re = Llamar POST "/api/v1/notificaciones/$($notif.notificacionId)/reintentos" $null @{ Authorization = "Bearer $token"; "Idempotency-Key" = $clave }
Write-Host "HTTP $($re.Codigo) estado=$($re.Cuerpo.estado)"
$re2 = Llamar POST "/api/v1/notificaciones/$($notif.notificacionId)/reintentos" $null @{ Authorization = "Bearer $token"; "Idempotency-Key" = $clave }
Write-Host "Misma clave otra vez: HTTP $($re2.Codigo) (respuesta original, no agrega otro intento)"
Start-Sleep -Seconds 3
$final = (Llamar GET "/api/v1/notificaciones/$($notif.notificacionId)" $null $auth).Cuerpo
Write-Host "Después del reintento: estado=$($final.estado) intentos=$($final.intentos) (el proveedor sigue rechazando ese email)"
$conflicto = Llamar POST "/api/v1/notificaciones/$((Llamar GET "/api/v1/notificaciones?destinatarioId=$clienteId" $null $auth).Cuerpo.items[0].notificacionId)/reintentos" $null @{ Authorization = "Bearer $token"; "Idempotency-Key" = (Id) }
Write-Host "Reintentar una ENVIADA: HTTP $($conflicto.Codigo) $($conflicto.Cuerpo.type)"

Titulo "FIN"
