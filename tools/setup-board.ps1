#Requires -Version 7
<#
.SYNOPSIS
    One-time setup of the studio's labels and the "RTS Studio" project board.

.DESCRIPTION
    OWNER ONLY. Agents must not run this script (it changes repository labels and the
    owner's GitHub Projects). See docs/studio/workflow.md.

    It creates the type:*, role:* and tier:* labels (never `approved`), creates the
    "RTS Studio" user project, links it to the repository and adds a Size field. It sets the
    Status options itself only on a project it has just created; for any other project it
    prints the manual steps, because replacing options clears cards' Status values.
    It is idempotent: existing labels, projects and fields are reported and left alone, and
    it stops before creating anything if it can't read the complete list of what exists.
    Run with -WhatIf first to see what it would do.

    Needs the GitHub CLI logged in with the `project` scope.

.EXAMPLE
    pwsh tools/setup-board.ps1 -WhatIf
    pwsh tools/setup-board.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Owner = 'smokim38',
    [string]$Repo = 'smokim38/rts-game',
    [string]$ProjectTitle = 'RTS Studio'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$Labels = @(
    @{ Name = 'type:feature'; Color = '1D76DB'; Description = 'New player-facing behavior' }
    @{ Name = 'type:bug'; Color = 'D73A4A'; Description = 'Something does not work as it should' }
    @{ Name = 'type:task'; Color = 'C5DEF5'; Description = 'Tooling, setup or other work' }
    @{ Name = 'type:spike'; Color = 'FBCA04'; Description = 'Time-boxed research; output is a finding' }
    @{ Name = 'type:docs'; Color = '0075CA'; Description = 'Documentation only' }
    @{ Name = 'type:art'; Color = 'F9A03F'; Description = 'Models, materials, shaders, art pipeline' }
    @{ Name = 'role:producer'; Color = '5319E7'; Description = 'Producer / PM' }
    @{ Name = 'role:game-designer'; Color = '5319E7'; Description = 'Game designer (docs only)' }
    @{ Name = 'role:tech-architect'; Color = '5319E7'; Description = 'Tech architect (ADRs)' }
    @{ Name = 'role:code-reviewer'; Color = '5319E7'; Description = 'Code reviewer' }
    @{ Name = 'role:gameplay-programmer'; Color = '5319E7'; Description = 'Gameplay programmer' }
    @{ Name = 'role:qa-test-engineer'; Color = '5319E7'; Description = 'QA / test engineer' }
    @{ Name = 'role:tech-artist'; Color = '5319E7'; Description = 'Tech artist' }
    @{ Name = 'tier:light'; Color = 'BFDADC'; Description = 'Review tier: author self-check (AGENTS.md 6)' }
    @{ Name = 'tier:owner'; Color = 'BFDADC'; Description = 'Review tier: owner reviews (AGENTS.md 6)' }
    @{ Name = 'tier:standard'; Color = 'BFDADC'; Description = 'Review tier: other-vendor review (AGENTS.md 6)' }
    @{ Name = 'tier:critical'; Color = 'B60205'; Description = 'Review tier: in-depth review, owner reads it (AGENTS.md 6)' }
)

$StatusOptions = @(
    @{ name = 'Backlog'; color = 'GRAY'; description = 'Drafted, not yet approved or not yet Ready' }
    @{ name = 'Ready'; color = 'BLUE'; description = 'Meets Definition of Ready, waiting for an agent' }
    @{ name = 'In progress'; color = 'YELLOW'; description = 'An agent is working on its branch' }
    @{ name = 'In review'; color = 'PURPLE'; description = 'PR open, under review or QA' }
    @{ name = 'Blocked'; color = 'RED'; description = 'Waiting on a decision or another Issue' }
    @{ name = 'Done'; color = 'GREEN'; description = 'PR merged and Issue closed' }
)
$GitHubDefaultStatus = @('Todo', 'In Progress', 'Done')
$StatusRenames = @{ 'Todo' = 'Backlog'; 'In Progress' = 'In progress' }
$RequiredSizeOptions = @('S', 'M', 'L')
$LabelLimit = 10000

# `approved` is applied only by the owner, by hand. This script must never create or touch it.
if ($Labels.Name -contains 'approved') { throw 'Refusing to run: the label list contains "approved".' }

$Summary = [ordered]@{ Created = @(); Existed = @(); Changed = @(); Manual = @() }

function Invoke-Gh {
    param([Parameter(Mandatory)][string[]]$Arguments, [string]$InputText)
    $all = if ($InputText) { $InputText | & gh @Arguments 2>&1 } else { & gh @Arguments 2>&1 }
    $stdout = ($all | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
    if ($LASTEXITCODE -ne 0) {
        $stderr = ($all | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] }) -join "`n"
        throw "gh $($Arguments -join ' ') failed (exit $LASTEXITCODE): $stderr"
    }
    $stdout
}

function Invoke-GhJson {
    param([Parameter(Mandatory)][string[]]$Arguments, [string]$InputText)
    Invoke-Gh -Arguments $Arguments -InputText $InputText | ConvertFrom-Json
}

function Write-Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

function Get-StatusManualSteps([string]$Number, [string[]]$Current) {
    $wanted = $StatusOptions.name
    $lines = @(
        "Set the Status options by hand at https://github.com/users/$Owner/projects/$Number/settings (Custom fields > Status)."
        'Renaming an option keeps every card that uses it. Deleting an option clears the Status of every card that uses it, archived cards included.'
    )
    $step = 1
    $afterRename = foreach ($name in $Current) {
        $to = $StatusRenames[$name]
        if ($StatusRenames.Keys -ccontains $name -and $Current -cnotcontains $to) {
            $lines += "  $step. Rename ""$name"" to ""$to""."; $step++
            $to
        } else { $name }
    }
    $missing = @($wanted | Where-Object { @($afterRename) -cnotcontains $_ })
    if ($missing) { $lines += "  $step. Add the options: $($missing -join ', ')."; $step++ }
    foreach ($extra in @($afterRename | Where-Object { $wanted -cnotcontains $_ })) {
        $lines += "  $step. ""$extra"" is not one of the six. Before deleting it, find its cards (filter  status:""$extra""  on the board, and check the project's ... menu > Archived items) and move each one to one of the six. Then delete it."
        $step++
    }
    $lines += "  $step. Drag the options into this order: $($wanted -join ', ')."
    $lines -join "`n"
}

function Test-Complete([int]$Returned, $Total, [string]$What) {
    # Deciding "missing, so create it" from a partial list would create duplicates.
    if ($null -eq $Total) { throw "gh did not report how many $What exist, so the script can't tell what already exists. Stopping before changing anything else." }
    if ($Returned -lt $Total) { throw "Only $Returned of $Total $What were returned, so the script can't tell what already exists. Stopping before changing anything else." }
}

# --- Preflight ---------------------------------------------------------------
Write-Step 'Checking the GitHub CLI'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The GitHub CLI (gh) is not installed or not on PATH.' }
$headers = Invoke-Gh -Arguments @('api', '-i', 'user')
$scopeLine = ($headers -split "`n") | Where-Object { $_ -match '^X-Oauth-Scopes:' } | Select-Object -First 1
if (-not $scopeLine) {
    Write-Warning 'Could not read the token scopes; continuing. If project steps fail, run: gh auth refresh -s project'
} elseif (($scopeLine -replace '^X-Oauth-Scopes:', '' -split ',' | ForEach-Object Trim) -notcontains 'project') {
    Write-Host 'Your gh token lacks the "project" scope. Run this yourself, then re-run the script:' -ForegroundColor Yellow
    Write-Host '  gh auth refresh -s project'
    exit 1
}

# --- Labels ------------------------------------------------------------------
Write-Step "Labels on $Repo"
$existingLabels = @((Invoke-GhJson -Arguments @('label', 'list', '--repo', $Repo, '--limit', "$LabelLimit", '--json', 'name')).name)
if ($existingLabels.Count -ge $LabelLimit) { throw "The repository has $LabelLimit or more labels, so the script can't tell which exist. Nothing was changed." }
foreach ($label in $Labels) {
    if ($existingLabels -contains $label.Name) {
        Write-Host "  exists, left as is: $($label.Name)"
        $Summary.Existed += "label $($label.Name)"
    } elseif ($PSCmdlet.ShouldProcess($label.Name, 'Create label')) {
        Invoke-Gh -Arguments @('label', 'create', $label.Name, '--repo', $Repo, '--color', $label.Color, '--description', $label.Description) | Out-Null
        Write-Host "  created: $($label.Name)"
        $Summary.Created += "label $($label.Name)"
    }
}

# --- Project -----------------------------------------------------------------
Write-Step "Project '$ProjectTitle' for $Owner"
$projectList = Invoke-GhJson -Arguments @('project', 'list', '--owner', $Owner, '--closed', '--format', 'json', '--limit', '10000')
Test-Complete @($projectList.projects).Count $projectList.totalCount 'projects'
$projects = @($projectList.projects | Where-Object { $_.title -ceq $ProjectTitle })
if ($projects.Count -gt 1) { throw "Found $($projects.Count) projects titled '$ProjectTitle'. Keep one, then re-run." }

$project = $projects | Select-Object -First 1
$projectCreated = $false
if ($project) {
    if ($project.closed) { throw "Project '$ProjectTitle' (#$($project.number)) is closed. Reopen it, then re-run." }
    Write-Host "  exists: #$($project.number) $($project.url)"
    $Summary.Existed += "project #$($project.number)"
} elseif ($PSCmdlet.ShouldProcess($ProjectTitle, 'Create user project')) {
    $project = Invoke-GhJson -Arguments @('project', 'create', '--owner', $Owner, '--title', $ProjectTitle, '--format', 'json')
    $projectCreated = $true
    Write-Host "  created: #$($project.number) $($project.url)"
    $Summary.Created += "project #$($project.number)"
}

if (-not $project) {
    Write-Host '  -WhatIf: the project does not exist yet, so linking, the Size field and Status options are skipped.'
} else {
    $number = [string]$project.number

    # --- Link to the repository ---------------------------------------------
    Write-Step "Link to $Repo"
    $repoOwner, $repoName = $Repo -split '/', 2
    $linkQuery = 'query($owner:String!,$name:String!){repository(owner:$owner,name:$name){projectsV2(first:100){totalCount nodes{id}}}}'
    $linkedProjects = (Invoke-GhJson -Arguments @('api', 'graphql', '-f', "query=$linkQuery", '-f', "owner=$repoOwner", '-f', "name=$repoName")).data.repository.projectsV2
    Test-Complete @($linkedProjects.nodes).Count $linkedProjects.totalCount "projects linked to $Repo"
    if (@($linkedProjects.nodes.id) -contains $project.id) {
        Write-Host '  already linked'
        $Summary.Existed += "link to $Repo"
    } elseif ($PSCmdlet.ShouldProcess($Repo, "Link project #$number")) {
        Invoke-Gh -Arguments @('project', 'link', $number, '--owner', $Owner, '--repo', $Repo) | Out-Null
        Write-Host '  linked'
        $Summary.Created += "link to $Repo"
    }

    # --- Size field ----------------------------------------------------------
    Write-Step 'Size field'
    $fieldList = Invoke-GhJson -Arguments @('project', 'field-list', $number, '--owner', $Owner, '--format', 'json', '--limit', '100')
    Test-Complete @($fieldList.fields).Count $fieldList.totalCount 'project fields'
    $fields = $fieldList.fields
    $size = $fields | Where-Object name -ceq 'Size' | Select-Object -First 1
    $ExistingSizeOptions = @($size.options | ForEach-Object name)
    $missingSizes = @($RequiredSizeOptions | Where-Object { $ExistingSizeOptions -cnotcontains $_ })
    if ($size -and $size.type -ne 'ProjectV2SingleSelectField') {
        Write-Host "  exists but is a $($size.type), not a single-select field; left as is"
        $Summary.Manual += @"
Fix the Size field by hand at https://github.com/users/$Owner/projects/$number/settings (Custom fields):
  1. Rename the current "Size" field to "Size (old)".
  2. Create a single-select field "Size" with the options $($RequiredSizeOptions -join ', ').
  3. Copy each card's size across, then delete "Size (old)". Deleting it earlier loses those values.
"@
    } elseif ($size -and $missingSizes) {
        Write-Host "  exists but lacks the options: $($missingSizes -join ', '); left as is"
        $Summary.Manual += "Add the options $($missingSizes -join ', ') to the Size field at https://github.com/users/$Owner/projects/$number/settings (Custom fields > Size). Don't delete or recreate the field: that clears every card's Size."
    } elseif ($size) {
        Write-Host "  exists: $($ExistingSizeOptions -join ', ')"
        $Summary.Existed += 'field Size'
    } elseif ($PSCmdlet.ShouldProcess('Size', 'Create single-select field (S, M, L)')) {
        Invoke-Gh -Arguments @('project', 'field-create', $number, '--owner', $Owner, '--name', 'Size', '--data-type', 'SINGLE_SELECT', '--single-select-options', 'S,M,L') | Out-Null
        Write-Host '  created'
        $Summary.Created += 'field Size'
    }

    # --- Status options ------------------------------------------------------
    Write-Step 'Status options'
    $status = $fields | Where-Object name -ceq 'Status' | Select-Object -First 1
    $wanted = $StatusOptions.name
    $current = @($status.options | ForEach-Object name)
    if (-not $status) {
        Write-Host '  no Status field found'
        $Summary.Manual += Get-StatusManualSteps $number $current
    } elseif (($current -join '|') -ceq ($wanted -join '|')) {
        Write-Host "  already set: $($current -join ', ')"
        $Summary.Existed += 'Status options'
    } else {
        # Replacing options recreates them with new IDs and clears the Status of every card, archived ones included.
        # Only a project this run just created is certain to have no cards, so any other project is left to the owner.
        if (-not $projectCreated -or ($current -join '|') -cne ($GitHubDefaultStatus -join '|')) {
            Write-Host "  current: $($current -join ', '). Not changed automatically, so no card can lose its Status."
            $Summary.Manual += Get-StatusManualSteps $number $current
        } elseif ($PSCmdlet.ShouldProcess('Status', "Replace options with: $($wanted -join ', ')")) {
            $mutation = 'mutation($fieldId:ID!,$options:[ProjectV2SingleSelectFieldOptionInput!]){updateProjectV2Field(input:{fieldId:$fieldId,singleSelectOptions:$options}){projectV2Field{... on ProjectV2SingleSelectField{options{name}}}}}'
            $body = @{ query = $mutation; variables = @{ fieldId = $status.id; options = $StatusOptions } } | ConvertTo-Json -Depth 5 -Compress
            try {
                $result = Invoke-GhJson -Arguments @('api', 'graphql', '--input', '-') -InputText $body
                $now = $result.data.updateProjectV2Field.projectV2Field.options.name
                Write-Host "  set: $($now -join ', ')"
                $Summary.Changed += 'Status options'
                $Summary.Manual += "Check the project's built-in workflows still target ""Done"" (the option was recreated): https://github.com/users/$Owner/projects/$number/workflows"
            } catch {
                Write-Host "  gh could not set the options: $($_.Exception.Message)" -ForegroundColor Yellow
                $Summary.Manual += Get-StatusManualSteps $number $current
            }
        }
    }

    $Summary.Manual += @"
Optional built-in workflows (gh can't set these): open https://github.com/users/$Owner/projects/$number/workflows and turn on
  - "Item added to project": set Status to Backlog
  - "Item closed": set Status to Done
  - "Pull request merged": set Status to Done
  - "Auto-add to project": filter  repo:$Repo is:issue
"@
}

# --- Summary -----------------------------------------------------------------
Write-Step 'Summary'
Write-Host "Created ($($Summary.Created.Count)):"; $Summary.Created | ForEach-Object { Write-Host "  $_" }
Write-Host "Already existed ($($Summary.Existed.Count)):"; $Summary.Existed | ForEach-Object { Write-Host "  $_" }
Write-Host "Changed ($($Summary.Changed.Count)):"; $Summary.Changed | ForEach-Object { Write-Host "  $_" }
if ($Summary.Manual.Count) {
    Write-Host 'Manual steps left:' -ForegroundColor Yellow
    $Summary.Manual | ForEach-Object { Write-Host $_ }
}
