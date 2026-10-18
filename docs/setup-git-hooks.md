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

From the repository root:

```sh
git config core.hooksPath .githooks
git config --get core.hooksPath   # prints: .githooks
```

## 3. Check that the scan blocks a secret

Use a throwaway branch and a random fake token, and don't push it. (A token like `ghp_aaaa…` won't trigger gitleaks: it looks too unlike a real one.)

```sh
git switch -c hook-test
printf 'token = "ghp_%s"\n' "$(LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 36)" > hook-test.txt
git add hook-test.txt
git commit -m "test: hook check"   # expect: blocked, exit 1, value redacted
git reset hook-test.txt
rm hook-test.txt
git switch -
git branch -D hook-test
```

If the commit goes through, stop: the hook isn't active. Check step 2.

## 4. Check the LFS hooks

```sh
ls .githooks   # expect: post-checkout post-commit post-merge pre-commit pre-merge-commit pre-push
grep -l "git lfs" .githooks/*   # expect: post-checkout post-commit post-merge pre-push
```

Don't run `git lfs install` after setting `core.hooksPath`: it may rewrite the hooks in `.githooks/`, which is a protected path.
