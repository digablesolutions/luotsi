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
2. Before tagging a public release, work through the release-day checklist in
   [docs/distribution-playbook.md](docs/distribution-playbook.md).
   Minimum release-prep steps:

   - Confirm repo description, homepage, and topics are current.
   - Upload the repository social preview manually in GitHub settings if it is
     still missing. Current candidate asset:
     `website/public/images/buggy-commands.png`.
   - Confirm the docs hub, AI agent workflows page, installation page, and
     replay page match the release.
   - Prepare a handwritten release intro by copying
     `.github/release-notes/stable.template.md` to
     `.github/release-notes/stable.md`, copying
     `.github/release-notes/prerelease.template.md` to
     `.github/release-notes/prerelease.md`, or creating
     `.github/release-notes/<tag>.md` directly.

3. Create and push a signed or annotated tag:

   ```powershell
   git tag -a v1.2.3 -m "Luotsi v1.2.3"
   git push origin v1.2.3
   ```

4. GitHub Actions runs `.github/workflows/release.yml`.
5. The workflow verifies that the tag commit is reachable from the repository's
   default branch, validates the source with locked package restore, publishes
   all supported runtime archives, writes SHA-256 checksums, creates artifact
   attestations, and creates a GitHub Release.

## Handwritten Release Intro

The release workflow prepends optional checked-in text above GitHub-generated
release notes.

The workflow only consumes non-template files. Use one of these active intro
files:

- `.github/release-notes/stable.md` for normal public releases.
- `.github/release-notes/prerelease.md` for prereleases such as `-rc` tags.
- `.github/release-notes/<tag>.md` for tag-specific messaging that should only
  apply to one release.

The tag-specific file wins over the stable or prerelease default.

Copy from one of these templates under `.github/release-notes/` and keep the
intro short:

- `.github/release-notes/stable.template.md`
- `.github/release-notes/prerelease.template.md`

The point is to add the top-level framing that generated release notes cannot
infer:

1. What changed for agent builders.
2. What changed for real-device engineers or CI users.
3. Which docs page to open first.

## Produced Assets

Each release publishes:

- `luotsi-cli-<version>-win-x64.zip`
- `luotsi-cli-<version>-linux-x64.tar.gz`
- `luotsi-cli-<version>-osx-x64.tar.gz`
- `luotsi-cli-<version>-osx-arm64.tar.gz`
- `luotsi-install.ps1`
- `luotsi-install.sh`
- `SHA256SUMS`

Each runtime archive contains the self-contained `luotsi` executable
(`luotsi.exe` on Windows) plus any companion files emitted by `dotnet publish`.
The installer scripts are published alongside the archives so the documented
quick-install flows can consume release-backed assets instead of source-tree
build outputs.

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
