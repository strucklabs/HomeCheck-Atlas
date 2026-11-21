# One-time cleanup of the unpublished initial repository. Does not push.
[CmdletBinding()]
param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
function Invoke-RepoGit {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$GitArgs)
    $result = & git -c "safe.directory=$repo" -C $repo @GitArgs
    if ($LASTEXITCODE -ne 0) { throw "Git failed: $($GitArgs[0])" }
    return $result
}

if ((Invoke-RepoGit rev-parse --show-toplevel).Replace('/','\').TrimEnd('\') -ne $repo.TrimEnd('\')) {
    throw 'Unexpected repository root.'
}
if ((Invoke-RepoGit branch --show-current) -ne 'main') { throw 'Expected branch main.' }
if (@(Invoke-RepoGit remote).Count -ne 0) { throw 'A remote exists. Review publication status before rewriting history.' }
if ((Invoke-RepoGit rev-list --all --count) -ne '1') { throw 'Expected exactly one unpublished commit.' }
if ((Invoke-RepoGit rev-parse --short=7 HEAD) -ne '2bac91b') { throw 'The initial commit changed. Review before cleanup.' }
if (@(Invoke-RepoGit for-each-ref --format='%(refname)').Count -ne 1) { throw 'Unexpected additional refs.' }
if (@(Invoke-RepoGit diff --cached --name-only).Count -ne 0) { throw 'There are staged changes. Review them first.' }

$privateFiles = @(
    'work/test/20261114-214247-690c44/Messungen.csv',
    'work/test/20261114-214247-690c44/Netzstatus.txt',
    'work/test/20261114-214247-690c44/Verlauf.txt',
    'work/test/20261114-214247-690c44/WLAN-Verlauf.txt'
)
$publicChanges = @('.gitignore','README.md','Prepare-GitPublication.ps1')
$changes = @(Invoke-RepoGit diff --name-only)
foreach ($path in $changes) {
    if ($path -notin $publicChanges) { throw "Unexpected modified file: $path" }
}
foreach ($path in $publicChanges) {
    if (-not (Test-Path -LiteralPath (Join-Path $repo $path) -PathType Leaf)) { throw "Missing file: $path" }
}
foreach ($path in $privateFiles) {
    $null = Invoke-RepoGit ls-files --error-unmatch -- $path
    $null = Invoke-RepoGit check-ignore --no-index -- $path
}
if ($CheckOnly) {
    Write-Host 'Preflight OK. No changes made. Cleanup removes four logs from Git, preserves local copies and replaces the initial commit identity.'
    return
}

# Preserve local diagnostics, but remove them from the replacement commit.
$null = Invoke-RepoGit rm --cached -- @privateFiles
$null = Invoke-RepoGit add -- @publicChanges

# Neutral project identity: no personal account or private email in the commit.
$identity = @{
    GIT_AUTHOR_NAME='HomeCheck Contributors'
    GIT_AUTHOR_EMAIL='contributors@example.invalid'
    GIT_COMMITTER_NAME='HomeCheck Contributors'
    GIT_COMMITTER_EMAIL='contributors@example.invalid'
}
$previous = @{}
try {
    foreach ($key in $identity.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key,'Process')
        [Environment]::SetEnvironmentVariable($key,$identity[$key],'Process')
    }
    $null = Invoke-RepoGit -c commit.gpgsign=false commit --amend --reset-author -m 'HomeCheck Atlas: sanitized initial project with documentation'
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process') }
}

foreach ($path in $privateFiles) {
    if (@(Invoke-RepoGit ls-files -- $path).Count -ne 0) { throw 'A private log remains tracked.' }
    if (@(Invoke-RepoGit log --all --format=%H -- $path).Count -ne 0) { throw 'A private log remains in reachable history.' }
}
if (@(Invoke-RepoGit diff HEAD --name-only).Count -ne 0) { throw 'Tracked working tree is not clean.' }

# The previous initial commit contained private data. Remove local recovery refs
# and unreachable objects too. No backup of the contaminated Git history is made.
$null = Invoke-RepoGit config --local user.name 'HomeCheck Contributors'
$null = Invoke-RepoGit config --local user.email 'contributors@example.invalid'
$null = Invoke-RepoGit reflog expire --expire=now --all
$null = Invoke-RepoGit gc --prune=now
if (@(Invoke-RepoGit fsck --full --no-reflogs --unreachable).Count -ne 0) {
    throw 'Unexpected unreachable Git objects remain. Review before publishing.'
}
Write-Host 'Cleanup completed. Local diagnostic files were preserved and ignored. Nothing was pushed.'
Invoke-RepoGit log -1 --format='%h %s'
Invoke-RepoGit status --short --branch
