# Releasing

Luotsi releases are tag-driven and intentionally boring.

## Version Tags

Use semantic version tags:

- Stable: `v1.2.3`
- Pre-release: `v1.2.3-rc.1`

The release workflow rejects tags outside that shape.

## Release Flow

1. Make sure `main` is green. The required branch protection check should be
   `CI result`.
2. Create and push a signed or annotated tag:

   ```powershell
   git tag -a v1.2.3 -m "Luotsi v1.2.3"
   git push origin v1.2.3
   ```

3. GitHub Actions runs `.github/workflows/release.yml`.
4. The workflow verifies that the tag commit is reachable from the repository's
   default branch, validates the source with locked package restore, publishes
   all supported runtime archives, writes SHA-256 checksums, creates artifact
   attestations, and creates a GitHub Release.

## Produced Assets

Each release publishes:

- `luotsi-cli-<version>-win-x64.zip`
- `luotsi-cli-<version>-linux-x64.tar.gz`
- `luotsi-cli-<version>-osx-x64.tar.gz`
- `luotsi-cli-<version>-osx-arm64.tar.gz`
- `SHA256SUMS`

Each runtime archive contains the self-contained `luotsi` executable
(`luotsi.exe` on Windows) plus any companion files emitted by `dotnet publish`.

Release validation diagnostics and MSBuild binlogs are kept as workflow
artifacts for debugging failed releases.

## Manual Re-run

If a GitHub Release was not created because of an infrastructure failure, use
the `Release` workflow's manual dispatch with the existing tag. Manual reruns
are only for retrying the same tagged release assets. Do not rebuild from an
untagged branch for a production release.

## Dependency Locking

Package locks are part of the release contract. Update them intentionally with:

```powershell
dotnet restore Luotsi.sln --use-lock-file
```

CI and release restore in locked mode, so stale lock files fail the build.

## Bad Release

Do not rewrite or move published tags. Mark the bad GitHub Release as
pre-release or delete the release entry if no one should consume it, then issue
a new patch version.
