# Setting up the Git hooks

Owner steps, once per clone. Agents may not run step 2 (it changes git config).

The hooks in `.githooks/` do two jobs:

- **Secret scan:** `pre-commit` and `pre-merge-commit` run gitleaks on staged changes and block the commit if it finds a secret. They fail closed: no gitleaks, no commit.
- **Git LFS:** `pre-push`, `post-checkout`, `post-commit` and `post-merge` are the standard hooks from `git lfs install`. Setting `core.hooksPath` makes Git ignore `.git/hooks/`, so these copies keep LFS working.

## 1. Install gitleaks

Install gitleaks v8 from the official releases: https://github.com/gitleaks/gitleaks/releases. The hooks were tested with **8.30.1**.

```sh
gitleaks version
```

## 2. Point Git at the hooks

From the repository root (PowerShell):

```powershell
git config core.hooksPath .githooks
git config core.hooksPath   # expect: .githooks
```

## 3. Check that the scan blocks a secret

This runs in a throwaway repo in your temp folder, never in `rts-game`. It points that repo at `C:/dev/rts-game/.githooks`, tries to commit a random fake token, expects the commit to be blocked, then deletes the folder. It stops on any unexpected result. (A token like `ghp_aaaa…` won't trigger gitleaks because it doesn't look like a real one, so the script makes a random one.)

```powershell
$hooks = 'C:/dev/rts-game/.githooks'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("hook-test-" + [guid]::NewGuid())
New-Item -ItemType Directory $tmp | Out-Null
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
  $out = git -c user.name=hook-test -c user.email=hook-test@example.invalid commit -q -m "hook test" 2>&1 | Out-String
  if ($LASTEXITCODE -eq 0) { throw "UNEXPECTED: the commit went through. The secret scan is NOT working." }
  if ($out -notmatch 'Commit blocked') { throw "UNEXPECTED: commit failed for another reason:`n$out" }
  Write-Host "OK: the hook blocked the fake token."
} finally {
  Pop-Location
  Remove-Item -Recurse -Force $tmp
}
```

If it prints anything other than `OK: ...`, stop and fix that before you rely on the hooks.

## 4. Check the LFS hooks

```powershell
Get-ChildItem .githooks -Name
# expect: post-checkout post-commit post-merge pre-commit pre-merge-commit pre-push
Select-String -Path .githooks/* -Pattern 'git lfs' -List | ForEach-Object Filename
# expect: post-checkout post-commit post-merge pre-push
```

Don't run `git lfs install` after setting `core.hooksPath`: it may rewrite the hooks in `.githooks/`, which is a protected path.
