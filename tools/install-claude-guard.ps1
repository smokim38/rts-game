#Requires -Version 7
#Requires -RunAsAdministrator
<#
.SYNOPSIS
Installs the studio guard as a Claude Code managed hook. Owner only: no agent runs this.

.DESCRIPTION
Copies .claude/hooks/guard.js from a committed git ref (default: origin/main) to
C:\Program Files\ClaudeCode\hooks\guard.js, and adds the PreToolUse hook from
.claude/hooks/managed-settings.template.json to C:\Program Files\ClaudeCode\managed-settings.json.
The files come from the ref, never from the working tree, so local edits can't reach the
active guard. Only administrators can write that folder, so agents can't change or disable
the guard. Run it again after every merged guard change. It is idempotent and prints what it did.

.EXAMPLE
pwsh -File tools/install-claude-guard.ps1
pwsh -File tools/install-claude-guard.ps1 -Ref origin/chore/8-claude-staff   # try a PR before merging
#>
param(
    [string]$Ref = 'origin/main'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$minimumClaude = [version]'2.1.295'   # first version with onFailure "block" for hooks
# git writes UTF-8; without this, PowerShell decodes it with the console code page and garbles non-ASCII text.
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

function Show-FromRef([string]$Path) {
    $text = git -C $repo show "${Ref}:$Path"
    if ($LASTEXITCODE -ne 0) { throw "Could not read $Path from $Ref." }
    return ($text -join "`n") + "`n"
}

git -C $repo fetch --quiet origin
if ($LASTEXITCODE -ne 0) { throw 'git fetch failed.' }
$commit = git -C $repo rev-parse --short $Ref
if ($LASTEXITCODE -ne 0) { throw "Unknown ref $Ref." }

$claudeVersion = [version]((claude --version) -replace '^\s*([\d.]+).*$', '$1')
if ($claudeVersion -lt $minimumClaude) {
    throw "Claude Code $claudeVersion is too old: onFailure ""block"" needs $minimumClaude or later. Update Claude Code first."
}

# Use the system node in Program Files, which only administrators can replace.
$node = (Get-Command node -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if (-not $node.StartsWith($env:ProgramFiles, [StringComparison]::OrdinalIgnoreCase)) {
    throw "node must be installed under $env:ProgramFiles (found $node)."
}

$managedDir = Join-Path $env:ProgramFiles 'ClaudeCode'
$hookDir = Join-Path $managedDir 'hooks'
$guardPath = Join-Path $hookDir 'guard.js'
$settingsPath = Join-Path $managedDir 'managed-settings.json'
New-Item -ItemType Directory -Force -Path $hookDir | Out-Null

Set-Content -LiteralPath $guardPath -Value (Show-FromRef '.claude/hooks/guard.js') -NoNewline -Encoding utf8NoBOM
# The installed file must be byte-for-byte the committed one: compare git object IDs.
$expected = git -C $repo rev-parse "${Ref}:.claude/hooks/guard.js"
$actual = git -C $repo hash-object --no-filters -- $guardPath
if ($LASTEXITCODE -ne 0 -or $actual -ne $expected) {
    Remove-Item -LiteralPath $guardPath
    throw "The installed guard.js doesn't match $Ref ($actual, expected $expected). Removed it; if the hook is registered, Claude Code blocks every call until a good install."
}
Write-Host "Installed guard.js from $Ref ($commit) to $guardPath (verified: $actual)"

$template = (Show-FromRef '.claude/hooks/managed-settings.template.json') | ConvertFrom-Json -AsHashtable
$entry = $template.hooks.PreToolUse[0]
$entry.hooks[0].command = $node
$entry.hooks[0].args = @($guardPath)

$settings = if (Test-Path -LiteralPath $settingsPath) {
    Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json -AsHashtable
} else { @{} }
if (-not $settings.ContainsKey('hooks')) { $settings.hooks = @{} }
# Replace an earlier install of this guard; keep any other managed hooks.
$others = @($settings.hooks.PreToolUse | Where-Object { $_ -and -not ($_.hooks.args -contains $guardPath) })
$settings.hooks.PreToolUse = @($others) + @($entry)

$settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding utf8NoBOM
Write-Host "Registered the PreToolUse hook in $settingsPath (node: $node, onFailure: block)"
Write-Host 'Restart Claude Code, then run the live checks in docs/studio/claude-staff.md.'
