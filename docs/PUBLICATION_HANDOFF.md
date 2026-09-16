# Public preview publication handoff

Nothing in this handoff has been published. The recommended free path is a
dedicated public GitHub repository named `raydeStar/thaddeus-preview` containing
only the static launch page and its public media. The private application source
repository remains private.

## Why this route

GitHub Pages is available for public repositories on GitHub Free and can publish
static files from a branch. GitHub still records a Pages deployment workflow,
but Actions usage is free for public repositories. GitHub Releases permits up to
1,000 assets per release, with each asset under 2 GiB and no stated total release
or bandwidth cap. The 52,599,543-byte preview archive is therefore comfortably
within the current release-asset limit.

Cloudflare Pages Direct Upload is a sound alternative for the page, but its
current 25 MiB per-file limit cannot carry the preview archive. It would still
need a second download host. A single public GitHub repository is simpler for
this launch.

Official references checked September 16, 2026:

- https://docs.github.com/en/pages/quickstart
- https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site
- https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases
- https://developers.cloudflare.com/pages/get-started/direct-upload/

## Prepared material

- `artifacts/publication-handoff-20260916-a/site-repo` is the exact public
  repository payload. Its download link already targets the proposed repository
  and `v0.1.0-preview` release.
- `artifacts/publication-handoff-20260916-a/site-repo.zip` is the same payload as
  a portable handoff bundle.
- `artifacts/publication-handoff-20260916-a/RELEASE_NOTES.md` is ready to paste
  into the GitHub Release.
- `artifacts/publication-handoff-20260916-a/SHA256SUMS.txt` contains the frozen
  archive checksum.
- `artifacts/portable-local-delegation-release-20260916-d/thaddeus-win-x64.zip`
  is the exact release asset.
- `artifacts/publication-handoff-20260916-a/manifest.json` records every public
  file and hash.

## Publication sequence

1. Create the public repository `raydeStar/thaddeus-preview` without adding a
   license or generated starter files.
2. Push the contents of `site-repo` to its default branch.
3. In repository settings, configure Pages to deploy from the default branch
   root. No custom build workflow is needed.
4. Create tag and release `v0.1.0-preview`, use `RELEASE_NOTES.md` as the body,
   and attach `thaddeus-win-x64.zip` plus `SHA256SUMS.txt`.
5. Confirm that the page download button returns the release archive and that
   its downloaded SHA256 matches the published checksum.
6. Put the resulting Pages URL into the Product Hunt submission.

## Public disclosure boundary

Publish only this prepared payload and the two release assets. Do not copy the
private repository, local settings, database, host key, logs, connector
credentials, owner data, or test evidence into the public repository.

The landing page and release notes describe the build as an unsigned Windows
preview. Keep that wording until the clean-user acceptance pass and code-signing
work are complete.
