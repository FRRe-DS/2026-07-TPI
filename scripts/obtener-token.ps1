<#
.SYNOPSIS
    Pide un access_token al Keycloak de la cátedra (client_credentials) y lo
    copia al portapapeles, para pegarlo en el botón "Authorize" de Swagger.

.EXAMPLE
    .\scripts\obtener-token.ps1                       # usa KEYCLOAK_CLIENT_SECRET del .env
    .\scripts\obtener-token.ps1 -ClientId grupo-05 -ClientSecret <secreto>   # como si fuera M6
#>
param(
    [string]$KeycloakUrl = "http://localhost:8080/realms/ds-2026-realm",
    [string]$ClientId = "grupo-07",
    [string]$ClientSecret = $env:KEYCLOAK_CLIENT_SECRET
)

$archivoEnv = Join-Path $PSScriptRoot "..\.env"
if (-not $ClientSecret -and (Test-Path $archivoEnv)) {
    Get-Content $archivoEnv | ForEach-Object { if ($_ -match '^\s*KEYCLOAK_CLIENT_SECRET=(.*)$') { $ClientSecret = $Matches[1].Trim() } }
}
if (-not $ClientSecret) { throw "Falta el secreto del client (-ClientSecret o KEYCLOAK_CLIENT_SECRET en .env)." }

$respuesta = Invoke-RestMethod -Method Post -Uri "$KeycloakUrl/protocol/openid-connect/token" `
    -ContentType "application/x-www-form-urlencoded" `
    -Body @{ grant_type = "client_credentials"; client_id = $ClientId; client_secret = $ClientSecret }

try { Set-Clipboard -Value $respuesta.access_token; Write-Host "Token copiado al portapapeles (vence en $($respuesta.expires_in) s)." }
catch { Write-Host "Token (vence en $($respuesta.expires_in) s):" }
$respuesta.access_token
