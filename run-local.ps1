#Requires -Version 7
<#
    Startet Zeltlotse lokal. Einziger unterstützter Weg — der AppHost bringt
    SQL Server, Schnittstelle und Oberfläche gemeinsam hoch.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

try {
    docker info --format '{{.ServerVersion}}' 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw }
}
catch {
    Write-Error 'Docker läuft nicht. Der AppHost startet SQL Server als Container — bitte Docker Desktop starten.'
    exit 1
}

Write-Host 'Starte Zeltlotse (Aspire AppHost). Das Dashboard öffnet sich im Browser.' -ForegroundColor Cyan
dotnet run --project src/Zeltlotse.AppHost
