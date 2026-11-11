<#
.SYNOPSIS
    Demo en vivo de Event Sourcing + CQRS sobre el historial de viajes de M8.

.DESCRIPTION
    Parte 1 - Event Sourcing: se registran los hitos de un viaje como eventos,
              se muestra el flujo y el estado reconstruido; se intenta una
              transición inválida (409) y se prueba la idempotencia (202 / 422).
    Parte 2 - CQRS: con otro viaje, se consulta la vista de historial apenas
              enviados los eventos (vacía) y cada segundo hasta que aparece;
              se muestra el retraso medido y los pendientes de la proyección.
    Parte 3 - Reconstrucción: se borra y rearma la vista desde los eventos y se
              muestra que el historial queda igual.

    Funciona en Windows PowerShell 5.1 y en PowerShell 7. Usa Invoke-RestMethod
    y ConvertTo-Json (no curl.exe, que en PowerShell rompe las comillas del JSON).

.PARAMETER BaseUrl
    URL de la API de M8. Por defecto http://localhost:8088 (docker compose).

.PARAMETER SinPausa
    No espera Enter entre las partes (para grabar la salida en un archivo).

.EXAMPLE
    .\scripts\demo-cqrs-es.ps1
    .\scripts\demo-cqrs-es.ps1 -BaseUrl http://localhost:5088
#>
param(
    [string]$BaseUrl = "http://localhost:8088",
    [switch]$SinPausa
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')

# ----------------------------------------------------------------- utilidades

function Escribir-Titulo([string]$texto) {
    Write-Host ""
    Write-Host ("=" * 78) -ForegroundColor Cyan
    Write-Host "  $texto" -ForegroundColor Cyan
    Write-Host ("=" * 78) -ForegroundColor Cyan
}

function Escribir-Paso([string]$texto) {
    Write-Host ""
    Write-Host ">> $texto" -ForegroundColor Yellow
}

function Pausar {
    if (-not $SinPausa) { [void](Read-Host "Enter para continuar") }
}

function Mostrar-Json($objeto) {
    Write-Host ($objeto | ConvertTo-Json -Depth 8)
}

function Ahora-Utc {
    # ISO 8601 en UTC, por ejemplo 2026-11-11T13:00:00.1234567Z
    return (Get-Date).ToUniversalTime().ToString("o")
}

function Nuevo-Id { return [guid]::NewGuid().ToString() }

# Envía un hito a POST /api/v1/eventos. Devuelve el cuerpo de la respuesta 202.
function Enviar-Evento([string]$tipo, [string]$viajeId, [string]$origen, [string]$conductorId = $null, [string]$eventoId = $null) {
    if (-not $eventoId) { $eventoId = Nuevo-Id }
    $cuerpo = [ordered]@{
        eventoId   = $eventoId
        tipo       = $tipo
        origen     = $origen
        ocurridoEn = Ahora-Utc
        viajeId    = $viajeId
    }
    if ($conductorId) { $cuerpo.detalle = @{ conductorId = $conductorId } }
    $json = $cuerpo | ConvertTo-Json -Depth 5
    return Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/eventos" -ContentType "application/json" -Body $json
}

# Lee el cuerpo de una respuesta de error (409, 422...) en PowerShell 5.1 y 7.
# Primero prueba $_.ErrorDetails.Message; en Windows PowerShell 5.1 a veces viene
# vacío (respuestas "chunked"), y entonces se lee el stream de la respuesta.
function Leer-CuerpoError($registroError) {
    if ($registroError.ErrorDetails -and $registroError.ErrorDetails.Message) {
        return $registroError.ErrorDetails.Message
    }
    try {
        $stream = $registroError.Exception.Response.GetResponseStream()
        try { $stream.Position = 0 } catch { }
        $lector = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
        return $lector.ReadToEnd()
    }
    catch {
        return ""
    }
}

# Envía un cuerpo JSON ya armado y devuelve código + cuerpo, también si es un error (4xx).
function Enviar-Crudo([string]$json) {
    try {
        $r = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/eventos" -ContentType "application/json" -Body $json
        return [pscustomobject]@{ Codigo = 202; Cuerpo = ($r | ConvertTo-Json -Depth 5) }
    }
    catch {
        $codigo = [int]$_.Exception.Response.StatusCode
        return [pscustomobject]@{ Codigo = $codigo; Cuerpo = (Leer-CuerpoError $_) }
    }
}

function Obtener([string]$ruta) {
    return Invoke-RestMethod -Method Get -Uri "$BaseUrl$ruta"
}

# ----------------------------------------------------------------- arranque

Escribir-Titulo "DEMO CQRS + EVENT SOURCING - M8 (Grupo 7) - historial de viajes"
Write-Host "API: $BaseUrl"
try {
    $lab = Obtener "/api/v1/lab/proyeccion/estado"
    Write-Host "API disponible. Retraso configurado de la proyección: $($lab.retrasoConfiguradoMs) ms" -ForegroundColor Green
}
catch {
    Write-Host "No se pudo conectar con la API en $BaseUrl. ¿Corriste 'docker compose up --build'?" -ForegroundColor Red
    throw
}
Pausar

# ============================================================ PARTE 1: EVENT SOURCING

Escribir-Titulo "PARTE 1 - EVENT SOURCING: guardar eventos y reconstruir el estado"

$viaje1 = Nuevo-Id
$conductor1 = Nuevo-Id
Write-Host "viajeId    = $viaje1"
Write-Host "conductorId = $conductor1"

Escribir-Paso "Se envían los tres hitos a POST /api/v1/eventos (M5 solicita y asigna, M6 finaliza)"
$r = Enviar-Evento "VIAJE_SOLICITADO" $viaje1 "M5"
Write-Host ("  VIAJE_SOLICITADO -> 202, version {0}" -f $r.version)
$r = Enviar-Evento "VIAJE_ASIGNADO" $viaje1 "M5" $conductor1
Write-Host ("  VIAJE_ASIGNADO   -> 202, version {0}" -f $r.version)
$r = Enviar-Evento "VIAJE_FINALIZADO" $viaje1 "M6"
Write-Host ("  VIAJE_FINALIZADO -> 202, version {0}" -f $r.version)

Escribir-Paso "GET /api/v1/viajes/{id}/eventos  -> el flujo de eventos tal como se guardó (append-only)"
$flujo = Obtener "/api/v1/viajes/$viaje1/eventos"
$flujo.eventos | Select-Object seq, version, tipo, origen | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Payload del evento 2 (ConductorAsignado): solo IDs, ningún dato personal"
Mostrar-Json $flujo.eventos[1].payload

Escribir-Paso "GET /api/v1/viajes/{id}/estado   -> estado RECONSTRUIDO reproduciendo los eventos"
Mostrar-Json (Obtener "/api/v1/viajes/$viaje1/estado")
Pausar

Escribir-Paso "Viaje nuevo: se solicita y se intenta FINALIZAR sin conductor asignado"
$viaje2 = Nuevo-Id
$null = Enviar-Evento "VIAJE_SOLICITADO" $viaje2 "M5"
try {
    $null = Enviar-Evento "VIAJE_FINALIZADO" $viaje2 "M6"
    Write-Host "  ERROR: se esperaba 409 y la API aceptó el evento." -ForegroundColor Red
}
catch {
    $codigo = [int]$_.Exception.Response.StatusCode
    Write-Host "  Respuesta: HTTP $codigo (application/problem+json)" -ForegroundColor Green
    Write-Host (Leer-CuerpoError $_)
}

Escribir-Paso "Idempotencia: el mismo eventoId reenviado con el MISMO cuerpo"
$eventoRepetido = Nuevo-Id
$viaje3 = Nuevo-Id
$cuerpo = [ordered]@{ eventoId = $eventoRepetido; tipo = "VIAJE_SOLICITADO"; origen = "M5"; ocurridoEn = Ahora-Utc; viajeId = $viaje3 } | ConvertTo-Json
$primera = Enviar-Crudo $cuerpo
$segunda = Enviar-Crudo $cuerpo
Write-Host "  1er envío -> HTTP $($primera.Codigo)"
Write-Host "  2do envío -> HTTP $($segunda.Codigo)   ¿misma respuesta? $($primera.Cuerpo -eq $segunda.Cuerpo)"
Write-Host "  Eventos guardados para ese viaje: $((Obtener "/api/v1/viajes/$viaje3/eventos").cantidad)"

Escribir-Paso "Idempotencia: el mismo eventoId con OTRO cuerpo (otro origen)"
$otro = [ordered]@{ eventoId = $eventoRepetido; tipo = "VIAJE_SOLICITADO"; origen = "M6"; ocurridoEn = Ahora-Utc; viajeId = $viaje3 } | ConvertTo-Json
$res = Enviar-Crudo $otro
Write-Host "  Respuesta: HTTP $($res.Codigo)" -ForegroundColor Green
Write-Host $res.Cuerpo
Pausar

# ============================================================ PARTE 2: CQRS

Escribir-Titulo "PARTE 2 - CQRS: la vista de lectura se actualiza DESPUÉS (consistencia eventual)"

$viaje4 = Nuevo-Id
Write-Host "viajeId = $viaje4"

Escribir-Paso "Se envían los tres hitos (lado de ESCRITURA)"
$null = Enviar-Evento "VIAJE_SOLICITADO" $viaje4 "M5"
$null = Enviar-Evento "VIAJE_ASIGNADO" $viaje4 "M5" (Nuevo-Id)
$null = Enviar-Evento "VIAJE_FINALIZADO" $viaje4 "M6"
$reloj = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "  3 eventos aceptados (202)."

Escribir-Paso "Inmediatamente: GET /api/v1/viajes/{id}/historial (lado de LECTURA, lee solo la vista)"
$h = Obtener "/api/v1/viajes/$viaje4/historial"
Write-Host ("  t = {0,4:N1} s -> items en la vista: {1}  (la proyección todavía no llegó)" -f $reloj.Elapsed.TotalSeconds, @($h.items).Count)
$diag = Obtener "/api/v1/lab/proyeccion/estado"
Write-Host ("  Proyección: eventosEnStore={0} proyectados={1} pendientes={2}" -f $diag.eventosEnStore, $diag.eventosProyectados, $diag.pendientes)

Escribir-Paso "Se consulta cada 1 segundo hasta que aparezcan los 3 items"
$cantidad = 0
for ($i = 1; $i -le 20 -and $cantidad -lt 3; $i++) {
    Start-Sleep -Seconds 1
    $h = Obtener "/api/v1/viajes/$viaje4/historial"
    $diag = Obtener "/api/v1/lab/proyeccion/estado"
    $cantidad = @($h.items).Count
    Write-Host ("  t = {0,4:N1} s -> items en la vista: {1}   pendientes de proyectar: {2}" -f $reloj.Elapsed.TotalSeconds, $cantidad, $diag.pendientes)
}

Escribir-Paso "Historial final (retrasoMs = proyectadoEn - registradoEn)"
$h.items | Select-Object version, evento, descripcion, retrasoMs | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Resumen según la vista:"
Mostrar-Json $h.resumen

Escribir-Paso "GET /api/v1/lab/proyeccion/estado"
Mostrar-Json (Obtener "/api/v1/lab/proyeccion/estado")
Pausar

# ============================================================ PARTE 3: RECONSTRUCCIÓN

Escribir-Titulo "PARTE 3 - RECONSTRUCCIÓN: la vista es descartable, la verdad son los eventos"

$antes = $h.items | ForEach-Object { "{0}|{1}|{2}" -f $_.version, $_.evento, $_.descripcion }

Escribir-Paso "POST /api/v1/lab/proyeccion/reconstruir (borra la vista y reproduce todos los eventos)"
Mostrar-Json (Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/lab/proyeccion/reconstruir")

Escribir-Paso "Se vuelve a consultar el historial del mismo viaje"
$h2 = Obtener "/api/v1/viajes/$viaje4/historial"
$h2.items | Select-Object version, evento, descripcion, retrasoMs | Format-Table -AutoSize | Out-String | Write-Host
$despues = $h2.items | ForEach-Object { "{0}|{1}|{2}" -f $_.version, $_.evento, $_.descripcion }
$iguales = (@(Compare-Object $antes $despues).Count -eq 0)
Write-Host "¿Mismo historial (versión, evento, descripción) que antes de reconstruir? $iguales" -ForegroundColor Green
Write-Host "(retrasoMs cambia: es un dato de CUÁNDO se proyectó, no del viaje.)"

Escribir-Titulo "FIN DE LA DEMO"
