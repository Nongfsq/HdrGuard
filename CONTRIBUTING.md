# Contributing

HdrGuard is a public open-source repository. Every commit must be safe to publish.

## Commit Rules

Only commit source, tests, public assets, public scripts, and public documentation.
Before staging a file, decide whether a stranger on GitHub should be able to read it.

Allowed public files include:

- Application source under `src/`.
- Tests under `tests/`.
- Public artwork under `assets/` and `src/HdrGuard/Assets/`.
- Public scripts under `scripts/`.
- Public docs such as `README.md`, `PRIVACY.md`, `SECURITY.md`, `NOTICE`,
  `LICENSE`, and this file.
- `config.example.json` only when it contains generic defaults.

Never commit:

- Real `config.json`, `state.json`, `HdrGuard.log`, backup configs, invalid-config
  backups, crash dumps, trace files, or extracted runtime `data/`.
- Personal paths such as real user-profile paths, machine-specific install paths, or
  tool-cache paths.
- Email addresses, account names, usernames, device names, IPs, RustDesk IDs, access
  tokens, private keys, certificates, passwords, cookies, or session data.
- Internal PM notes, progress logs, AI working memory, local verification notes, or
  private planning documents.
- Generated release output such as `artifacts/`, published ZIP files, temporary
  extraction folders, `bin/`, or `obj/`.

## Configuration Rules

Public configuration must stay generic.

- Keep only `src/HdrGuard/config.example.json` in the repository.
- Use environment variables such as `%AppData%` and `%ProgramData%` in examples.
- Do not include real expanded paths, local usernames, personal RustDesk log content,
  or local display identifiers.
- Runtime files belong beside the installed application in `data/`, and `data/` must
  not be committed.

## Documentation Rules

Public docs must describe the product, not the maintainer's machine.

- Use generic paths such as `C:\Tools\HdrGuard` for Windows examples.
- Do not mention private repository names, internal project roots, local backup
  folders, or personal workflow directories.
- Keep internal progress, PM, and AI state documents out of this public repository.
- If a durable public rule changes, update the narrowest public document that owns it.

## Release Rules

Before publishing a release:

- Build from a clean working tree.
- Package only the executable, public config example, public README, and public
  install/uninstall scripts.
- Do not include `data/`, logs, state, local backups, local installer output, or
  extracted test folders in the ZIP.
- Verify the release ZIP SHA256.
- Extract the ZIP and scan the extracted text files before uploading or trusting the
  release asset.

## Required Checks Before Commit

Run these checks before committing:

```powershell
git status --short --branch
git diff --check
git ls-files | rg -n "(^|/)(config\.json|state\.json|HdrGuard\.log|.*\.bak|.*\.pfx|.*\.pem|.*\.key|.*\.cer|.*\.crt)$"
$privacyTerms = @(
  "C:" + "\Users\",
  "Users/",
  "Dev" + "Tools",
  "HdrGuard" + ".backups",
  "legacy-" + "appdata",
  "PRIVATE " + "KEY",
  "BEGIN " + "RSA",
  "BEGIN " + "OPENSSH",
  "gh" + "p_",
  "github" + "_pat_",
  "sk" + "-",
  "AK" + "IA"
)
foreach ($term in $privacyTerms) {
  rg -n --hidden --glob '!/.git/**' --glob '!artifacts/**' --glob '!bin/**' --glob '!obj/**' --glob '!TestResults/**' -F $term .
}
dotnet test .\tests\HdrGuard.Tests\HdrGuard.Tests.csproj
```

The `git ls-files` command must return no tracked real runtime files or secrets. The
privacy scan may report intentional generic examples; review every hit before
committing.

## History And Tag Hygiene

If private data or machine-specific paths are committed:

1. Stop and do not create a release from that history.
2. Remove the content from the current tree.
3. Check whether the content is present in previous commits or tags.
4. If it reached public history, rewrite the affected public refs and force-push with
   lease.
5. Re-scan a fresh clone of the public repository before continuing.

Use this fresh-clone scan after any history rewrite:

```powershell
git clone https://github.com/Nongfsq/HdrGuard.git HdrGuard-privacy-check
cd HdrGuard-privacy-check
git fetch --tags --force
$privateTerms = @(
  "C:" + "\Users\",
  "Dev" + "Tools",
  "PRIVATE " + "KEY",
  "github" + "_pat_",
  "gh" + "p_"
)
$refs = @("main") + (git tag --list)
foreach ($ref in $refs) {
  foreach ($term in $privateTerms) { git grep -n -F -e $term $ref -- . }
}
```

Extend the scan terms when the removed content had a specific path, account name, or
token prefix.

## AI Agent Rules

AI coding agents working in this repository must follow the same public-safety rules.

- Do not commit generated local runtime files.
- Do not introduce personal paths in examples.
- Do not copy private planning, progress, or chat-derived machine details into public
  docs.
- Do not push tags or release assets until privacy scans pass.
- When changing public docs, update only the narrowest document that owns the rule.
