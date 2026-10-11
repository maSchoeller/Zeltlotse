<#
.SYNOPSIS
  Baut genau eine Story aus stories.yaml in einer frischen Claude-Code-Sitzung (headless) mit bmad-build-auto.

.EXAMPLE
  pwsh scripts/story-ausfuehren.ps1 -Id 0-1
#>
param(
  [Parameter(Mandatory)] [string] $Id,
  [string] $SpecOrdner = '_bmad-output/specs/spec-zeltlotse',
  [string] $Modell = 'claude-opus-5-5',
  [switch] $Folgedurchlauf
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$logOrdner = Join-Path $SpecOrdner 'lauf/logs'
New-Item -ItemType Directory -Force $logOrdner | Out-Null
$stempel = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $logOrdner "$Id-$stempel.log"

# invoke_dev_with der Story aus stories.yaml lesen
$hinweis = python -I -c @"
import sys, yaml
d = yaml.safe_load(open(sys.argv[1], encoding='utf-8'))
e = [x for x in d if x['id'] == sys.argv[2]]
if not e: sys.exit('Story-Id nicht gefunden: ' + sys.argv[2])
print(e[0].get('invoke_dev_with', ''))
"@ "$SpecOrdner/stories.yaml" $Id
if ($LASTEXITCODE -ne 0) { throw "Story $Id nicht in stories.yaml" }

$zusatz = if ($Folgedurchlauf) { ' Dies ist ein Folgedurchlauf (follow-up pass) auf die bereits erledigte Story.' } else { '' }
$prompt = "/bmad-build-auto Spec-Ordner: $SpecOrdner, Story-Id: $Id.$zusatz Hinweis des Aufrufers: $hinweis"

"== Story $Id | Start $(Get-Date -Format s) | Modell $Modell" | Tee-Object -FilePath $log
claude -p $prompt --model $Modell --permission-mode auto *>> $log
$exit = $LASTEXITCODE
"== Story $Id | Ende $(Get-Date -Format s) | Exit $exit" | Tee-Object -FilePath $log -Append

# Ergebnis zusammenfassen: Status der Story-Spec und Arbeitsstand
$specDatei = Get-ChildItem -Path (Join-Path $SpecOrdner 'stories') -Filter "$Id-*.md" -ErrorAction SilentlyContinue | Select-Object -First 1
$status = if ($specDatei) { (Select-String -Path $specDatei.FullName -Pattern '^status:\s*(\S+)' | Select-Object -First 1).Matches.Groups[1].Value } else { 'keine-story-spec' }
$folge = if ($specDatei) { (Select-String -Path $specDatei.FullName -Pattern '^followup_review_recommended:\s*(\S+)' | Select-Object -First 1).Matches.Groups[1].Value } else { '' }
$sauber = -not (git status --porcelain)
"ERGEBNIS id=$Id status=$status followup=$folge arbeitsstand_sauber=$sauber log=$log" | Tee-Object -FilePath $log -Append
