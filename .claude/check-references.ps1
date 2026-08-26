#!/usr/bin/env pwsh
# Every harness file the harness names must exist. Scope is deliberately narrow:
# only `.claude/...` and `.github/...` paths, because files the bootstrap creates
# in a project (foundation.md, design-system.md, run-local.ps1) are absent here
# by construction. Catches the class of bug where a rule points at nothing.
$ErrorActionPreference = 'Stop'

$files = @('CLAUDE.md') + @(Get-ChildItem '.claude' -Recurse -File -Filter '*.md' | ForEach-Object { $_.FullName })
$missing = @()
foreach ($f in $files) {
  $text = Get-Content -LiteralPath $f -Raw
  foreach ($m in [regex]::Matches($text, '`(\.(?:claude|github)/[^`]+)`')) {
    $p = $m.Groups[1].Value
    if ($p -notmatch '\*' -and -not (Test-Path -LiteralPath $p)) {
      $missing += "$(Resolve-Path -Relative -LiteralPath $f) -> $p"
    }
  }
}
if ($missing.Count) { $missing | ForEach-Object { Write-Host "MISSING: $_" }; exit 1 }
Write-Host 'references ok'
