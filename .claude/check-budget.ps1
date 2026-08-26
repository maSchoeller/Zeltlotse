#!/usr/bin/env pwsh
# The written measurement for the budget numbers in CLAUDE.md. A number used as
# an acceptance criterion without one gets measured differently every run.
# Run from the repo root; exits 1 on any breach.
$ErrorActionPreference = 'Stop'

function Count($path) { @(Get-Content -LiteralPath $path).Count }

$fail = @()

$claude = Count 'CLAUDE.md'
if ($claude -gt 40) { $fail += "CLAUDE.md: $claude / 40" }

# Pipeline core: SKILL.md plus the phase files, non-recursive so presets/ stays out.
$coreFiles = Get-ChildItem '.claude/skills/pipeline' -File -Filter '*.md'
$core = ($coreFiles | ForEach-Object { Count $_.FullName } | Measure-Object -Sum).Sum
if ($core -gt 300) { $fail += "pipeline core: $core / 300" }

# Everything that loads only when its case arises: other skills and presets.
$skills = @(Get-ChildItem '.claude/skills' -Directory | Where-Object { $_.Name -ne 'pipeline' })
$apart = @($skills | ForEach-Object { Get-ChildItem $_.FullName -Recurse -File -Filter '*.md' })
if (Test-Path '.claude/skills/pipeline/presets') {
  $apart += @(Get-ChildItem '.claude/skills/pipeline/presets' -File -Filter '*.md')
}
foreach ($f in $apart) {
  $n = Count $f.FullName
  if ($n -gt 80) { $fail += "$($f.Name): $n / 80" }
}
if ($skills.Count -gt 6) { $fail += "skills besides pipeline: $($skills.Count) / 6" }

Write-Host "CLAUDE.md $claude/40 | pipeline core $core/300 | skills $($skills.Count)/6 | apart $($apart.Count) files <=80"
if ($fail.Count) { $fail | ForEach-Object { Write-Host "OVER BUDGET: $_" }; exit 1 }
Write-Host 'budget ok'
