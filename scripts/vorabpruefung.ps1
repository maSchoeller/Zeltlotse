<#
.SYNOPSIS
  Prüft, ob die Umgebung für den autonomen Lauf bereit ist. Exit 0 = bereit.
#>
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$fehler = @()
function Pruefe($name, $ok, $hilfe) {
  if ($ok) { "OK    $name" } else { "FEHLT $name -> $hilfe"; $script:fehler += $name }
}

$sdks = (dotnet --list-sdks 2>$null) -join "`n"
Pruefe '.NET SDK 10.0.4xx (global.json verlangt 10.0.401)' ($sdks -match '^10\.0\.4\d\d ' -or $sdks -match '(?m)^10\.0\.(4\d\d|[5-9]\d\d) ') 'aktuelles .NET 10 SDK installieren (winget install Microsoft.DotNet.SDK.10)'
$docker = docker version --format '{{.Server.Version}}' 2>$null
Pruefe 'Docker Desktop läuft' ($LASTEXITCODE -eq 0 -and $docker) 'Docker Desktop starten (Linux-Container)'
Pruefe 'Node.js' ((Get-Command node -ErrorAction SilentlyContinue) -ne $null) 'Node.js LTS installieren'
Pruefe 'Claude Code CLI' ((Get-Command claude -ErrorAction SilentlyContinue) -ne $null) 'npm install -g @anthropic-ai/claude-code'
python -I -c "import yaml" 2>$null
Pruefe 'Python mit PyYAML' ($LASTEXITCODE -eq 0) 'pip install pyyaml'
Pruefe 'uv' ((Get-Command uv -ErrorAction SilentlyContinue) -ne $null) 'uv installieren'
$branch = git branch --show-current
Pruefe "Branch mvp ausgecheckt (ist: $branch)" ($branch -eq 'mvp') 'git switch mvp'
Pruefe 'Arbeitsstand sauber' (-not (git status --porcelain)) 'Änderungen committen'

if ($fehler.Count) { "`nNICHT BEREIT: $($fehler.Count) Punkt(e) offen."; exit 1 } else { "`nBEREIT."; exit 0 }
