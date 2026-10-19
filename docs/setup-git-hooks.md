# Setting up the Git hooks

Owner steps, once per clone. Agents may not run step 2 (it changes git config).

The hooks in `.githooks/` do two jobs:

- **Secret scan:** `pre-commit` and `pre-merge-commit` run gitleaks on staged changes and block the commit if it finds a secret. They fail closed: no gitleaks, no commit. Nothing in the caller's environment can change the scan:
  - The gitleaks binary must match the SHA-256 of the official 8.30.1 build pinned in `pre-commit`, so a replaced binary or a fake `gitleaks` earlier on `PATH` blocks the commit.
  - After finding gitleaks, the hook keeps only Git for Windows' own folders (under Program Files) on `PATH`.
  - It removes every `GITLEAKS_*` variable and always passes `--config .githooks/gitleaks.toml`, so neither `GITLEAKS_CONFIG` nor a `.gitleaks.toml` elsewhere in the repository can change the rules.
- **Git LFS:** `pre-push`, `post-checkout`, `post-commit` and `post-merge` are the standard hooks from `git lfs install`. Setting `core.hooksPath` makes Git ignore `.git/hooks/`, so these copies keep LFS working.

## 1. Install gitleaks

Install gitleaks **8.30.1** from the official releases (https://github.com/gitleaks/gitleaks/releases) or with `winget install Gitleaks.Gitleaks`. The hook accepts only the build whose SHA-256 is pinned in `.githooks/pre-commit`.

```sh
gitleaks version
```

**Upgrading gitleaks** blocks every commit until the pin is updated. The owner does that in a Critical-tier PR:
1. Download the new `gitleaks_<version>_windows_x64.zip` and `gitleaks_<version>_checksums.txt` from the official release, and check the zip with the checksums file.
2. Take the SHA-256 of `gitleaks.exe` inside the zip, and check that it matches the installed binary.
3. Put the new version and hash in `.githooks/pre-commit`, and the new version and tarball checksum in `.github/workflows/gitleaks.yml`.

## 2. Point Git at the hooks

From the repository root (PowerShell):

```powershell
git config core.hooksPath .githooks
git config core.hooksPath   # expect: .githooks
```

## 3. Check that the scan blocks a secret

Owner only: agents can't run this, because it sets git config and environment variables in the test.

This runs in a throwaway repo in your temp folder, never in `rts-game`. It points that repo at `C:/dev/rts-game/.githooks` and tries to commit a random fake token four ways. Each attempt must be blocked:
1. plainly
2. with `GITLEAKS_CONFIG` pointing to a config that allows everything
3. with a `.gitleaks.toml` in the repo that allows everything
4. with a fake `gitleaks` first on `PATH`

Then it deletes the folder. It stops on any unexpected result. (A token like `ghp_aaaa…` won't trigger gitleaks because it doesn't look like a real one, so the script makes a random one.)

```powershell
$hooks = 'C:/dev/rts-game/.githooks'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("hook-test-" + [guid]::NewGuid())
New-Item -ItemType Directory $tmp | Out-Null
$savedPath = $env:PATH
Push-Location $tmp
try {
  git init -q
  if ($LASTEXITCODE -ne 0) { throw "git init failed" }
  git config core.hooksPath $hooks
  if ($LASTEXITCODE -ne 0) { throw "git config failed" }
  $chars = [char[]]'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
  $token = 'ghp_' + -join (1..36 | ForEach-Object { $chars | Get-Random })
  Set-Content token.txt "token = `"$token`""
  git add token.txt
  if ($LASTEXITCODE -ne 0) { throw "git add failed" }
  function Test-Blocked([string]$Case, [string]$Expect) {
    $out = git -c user.name=hook-test -c user.email=hook-test@example.invalid commit -q -m "hook test" 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) { throw "UNEXPECTED ($Case): the commit went through. The secret scan is NOT working." }
    if ($out -notmatch $Expect) { throw "UNEXPECTED ($Case): commit failed for another reason:`n$out" }
    Write-Host "OK: blocked ($Case)."
  }
  $allowAll = "[allowlist]`npaths = ['''.*''']"

  Test-Blocked 'plain fake token' 'Commit blocked'

  Set-Content allow-all.toml $allowAll
  $env:GITLEAKS_CONFIG = Join-Path $tmp 'allow-all.toml'
  Test-Blocked 'GITLEAKS_CONFIG allows everything' 'Commit blocked'
  Remove-Item Env:GITLEAKS_CONFIG

  Set-Content .gitleaks.toml $allowAll
  git add .gitleaks.toml
  Test-Blocked '.gitleaks.toml allows everything' 'Commit blocked'
  git rm -q --cached .gitleaks.toml

  New-Item -ItemType Directory fake | Out-Null
  Copy-Item "$env:SystemRoot\System32\whoami.exe" fake\gitleaks.exe
  $env:PATH = (Join-Path $tmp 'fake') + ';' + $savedPath
  Test-Blocked 'fake gitleaks first on PATH' 'pinned'
} finally {
  $env:PATH = $savedPath
  Remove-Item Env:GITLEAKS_CONFIG -ErrorAction SilentlyContinue
  Pop-Location
  Remove-Item -Recurse -Force $tmp
}
```

If it prints anything other than four `OK: blocked …` lines, stop and fix that before you rely on the hooks.

## 4. Check the LFS hooks

```powershell
Get-ChildItem .githooks -Name
# expect: post-checkout post-commit post-merge pre-commit pre-merge-commit pre-push
Select-String -Path .githooks/* -Pattern 'git lfs' -List | ForEach-Object Filename
# expect: post-checkout post-commit post-merge pre-push
```

Don't run `git lfs install` after setting `core.hooksPath`: it may rewrite the hooks in `.githooks/`, which is a protected path.

## 5. The CI scan (server-side backstop)

Local hooks can be skipped, so `.github/workflows/gitleaks.yml` scans the full history of every pull request and every push to `main`. It uses the official gitleaks 8.30.1 Linux build, checked against its published checksum.
- **Rules a PR can't change:** the workflow runs from `main` (`pull_request_target`) and writes its own config, so a pull request can't change the rules it is judged by. It uses only `main`'s `.gitleaksignore` and deletes the copy in the scanned code.
- **Read-only:** it only reads the pull request's content and never runs it. The token is read-only.

Owner settings on GitHub (once, after the workflow is on `main`):
1. **Required check.** Settings → Rules → Rulesets → New branch ruleset. Target: the default branch.
   - Turn on "Require status checks to pass" and add `gitleaks`.
   - Turn on "Require a pull request before merging", "Block force pushes" and "Restrict deletions".
2. **Bypass list.** Leave it empty, so the rules apply to you as well. Don't bypass the check for a false positive. Instead:
   1. Add the exception to `.gitleaksignore` in a PR of its own, and merge that PR.
   2. Re-run the failed check: the scan reads `.gitleaksignore` only from `main`.
