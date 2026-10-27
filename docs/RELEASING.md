# Releasing LinkPeek on macOS and Windows

One repository contains two native implementations. Build and validate each platform independently; a Windows-only change does not require replacing the macOS app. Existing macOS tags, release notes and assets can stay in place when Windows assets are added.

## Package names and versions

| Platform | Package | Checksum | Version source |
| --- | --- | --- | --- |
| macOS | `LinkPeek-0.1.1.zip` | `LinkPeek-0.1.1.zip.sha256` | Xcode project marketing version |
| Windows x64 | `LinkPeek-Windows-0.1.1-win-x64.zip` | Same filename + `.sha256` | `Windows/LinkPeek.Windows/LinkPeek.Windows.csproj`, `Version` |
| Windows ARM64 | `LinkPeek-Windows-0.1.1-win-arm64.zip` | Same filename + `.sha256` | Same Windows project |

Use platform-specific names so Windows packages cannot replace the Mac ZIP. A release can have all six files. GitHub's automatic Source code archives are not runnable applications.

Windows is at version 0.1.1 and no Windows assets have been published yet. Actions artifacts and `BUILD-INFO.txt` distinguish builds by source commit. If 0.1.1 Windows assets already exist, the upload workflow rejects duplicate filenames; to replace them, remove only those Windows assets in Releases first (or bump the Windows version) and leave the Mac assets untouched.

The current macOS release is `v0.1.1`. Its tag predates the Windows port. If Windows packages are added to that release, **do not move or recreate the tag**: the Mac package still corresponds to that original tag, and the new Windows packages correspond to the selected workflow run's commit. `BUILD-INFO.txt` inside every Windows ZIP records that commit. Add a Windows section in the release notes with the build commit and validation status so users can distinguish them. For a single source tag covering both implementations, create a new release from a commit that contains both.

## Add Windows assets to the existing v0.1.1 release

This is the shortest route when a Mac release already exists:

1. Open [Actions → Windows](https://github.com/nqnhut92/LinkPeek/actions/workflows/windows.yml). Wait for the build on `main` to pass. Its x64/ARM64 jobs run the core tests on a Windows runner and create self-contained portable ZIPs. **desktop-checks** runs the hosted-hyperlink fixture, the UI layout and saved-PAT checks and a hover-card lifecycle test, and uploads **LinkPeek-UI-previews** (light/dark PNGs, kept 14 days). These checks do not replace testing real apps such as Teams.
2. For a trial build, open that run's **Artifacts** section and download `LinkPeek-Windows-win-x64` (or `win-arm64`). You must be signed in to GitHub. Extract the outer Actions archive, then the application ZIP inside it. Keep the entire extracted application folder and run `LinkPeek.exe`.
3. Run through the [Windows acceptance checklist](../Windows/README.md#windows-acceptance-checklist) on a Windows machine and note in the release notes what you actually tested.
4. Back on the **Windows** workflow page, click **Run workflow**. Choose branch **main**, enter `v0.1.1` in **release_tag**, and run it. This builds the selected branch revision again, then attaches only the two Windows ZIPs and two checksums to that existing release. Leaving the field blank builds downloadable Actions artifacts without changing Releases.
5. Wait for both build jobs and **attach-to-release** to pass. Open [release v0.1.1](https://github.com/nqnhut92/LinkPeek/releases/tag/v0.1.1) and confirm the four Windows assets appear beside the existing Mac files. Verify the checksum after downloading.
6. Click **Edit** on the release and append a Windows section: supported Windows versions, x64/ARM64 download names, portable extraction instructions, the Windows source commit from the run/BUILD-INFO, unsigned status and what was actually tested. Preserve the Mac instructions and validation notes.

The workflow uses GitHub's built-in per-run token; no personal access token or repository secret is needed. It requests `contents: write` only for the manual upload job. An organization policy can still deny write access; in that case use the manual upload method below or ask a repository administrator to permit the workflow. Inputs are passed as environment variables, not interpolated into shell commands.

The upload job requires an existing release and never creates a tag/release, edits notes, deletes assets or uses `--clobber`. It rejects duplicate Windows asset names before upload. If an upload partially fails, inspect the release before retrying: remove only the incomplete Windows assets you intend to replace, or bump the Windows version for a distinct build. Never delete the Mac files to retry Windows publication.

## Upload Windows assets manually

This also works if Actions cannot write to Releases:

1. Download the x64 and ARM64 Actions artifacts from the **same successful commit**, or build locally on Windows with `Windows/publish.ps1` for each architecture.
2. Extract each outer Actions artifact. Keep the versioned application ZIP and its matching `.zip.sha256` intact; do not upload the outer artifact wrapper or an isolated EXE.
3. Go to **Releases → v0.1.1 → Edit** and drag the four Windows files into the asset area. Keep all Mac assets and existing notes, append the Windows notes described above, then save the release.

Optional GitHub CLI equivalent, authenticated on your own machine:

```powershell
gh release upload v0.1.1 `
  LinkPeek-Windows-0.1.1-win-x64.zip `
  LinkPeek-Windows-0.1.1-win-x64.zip.sha256 `
  LinkPeek-Windows-0.1.1-win-arm64.zip `
  LinkPeek-Windows-0.1.1-win-arm64.zip.sha256 `
  --repo nqnhut92/LinkPeek
```

## Create later releases

For a release containing both updated apps, update the relevant version sources, commit and push to `main`, then create a **draft release** from a new tag (for example `v0.1.2`) pointing to that reviewed commit. Build the Mac package from that same commit, upload it, and run the Windows workflow from that tag with `release_tag` set to it. A tag can be selected in the CLI as below. Validate the downloaded assets and notes before publishing the draft.

```powershell
gh workflow run windows.yml --ref v0.1.2 -f release_tag=v0.1.2 --repo nqnhut92/LinkPeek
```

For independent Windows release cadence, use a new Windows-specific tag/release (for example `windows-v0.1.2`) instead of changing an existing Mac tag. Keep the Windows application version in sync with its package name; choose **pre-release** while desktop acceptance is pending. GitHub's **latest release** selection is repository-wide, so review it when creating a Windows-only release to avoid confusing Mac users. The upload workflow works with either tag style because it attaches to an explicitly selected existing release.

## macOS packaging remains separate

On a Mac with Xcode/Swift 6, run the existing documented checks (`swift test`, strict Swift formatting, and the Python release-script tests), build Debug/Release and complete native runtime checks. From the root checkout, `sh scripts/release.sh` archives, signs and packages the Mac app. It needs the existing stable `LinkPeek Release` signing identity in that Mac's Keychain; never commit a private key or certificate export. `LINKPEEK_SIGNING_IDENTITY=- sh scripts/release.sh` makes an explicitly named ad-hoc test package, not a public release-identity package.

Upload `build/release/LinkPeek-<version>.zip` and its checksum to the intended release after verifying them. The Windows workflow does not build, sign, upload or alter macOS packages. Neither platform currently provides Apple notarization/Windows Authenticode signing through this workflow.
