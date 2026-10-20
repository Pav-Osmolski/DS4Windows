# GitHub Pages website

The public site is [pav-osmolski.github.io/DS4Windows](https://pav-osmolski.github.io/DS4Windows/).
GitHub Pages hosts a static Zensical build. No separate documentation copy,
account service, analytics SDK or server application is required.

## Sources

- `docs/` remains the canonical documentation, usable directly on GitHub.
- `website/index.md` and `website/overrides/home.html` define the homepage.
- `website/assets/site.css` styles the site and responsive homepage.
- `zensical.toml` defines navigation, themes and the public project URL.
- Fresh app captures live in `docs/images/site/`; artwork provenance and the
  image-generation prompt are in [assets/README.md](assets/README.md).

`prepare.py` stages documentation into the ignored `.pages/docs/` directory.
It adapts repository-only source links for the website, keeps image links local,
and reads the release version from the application project. It does not edit
the canonical documentation. The generated site is in `.pages/site/`.
Historical validation and protocol records remain accessible by links, but are
excluded from built-in search and marked `noindex` so current setup guidance is
easier to find. No canonical source metadata is rewritten.

## Preview and validate

Use Python 3.12 in a dedicated virtual environment:

```text
python -m venv .venv-pages
.venv-pages\Scripts\python -m pip install -r website/requirements.txt
.venv-pages\Scripts\python website/prepare.py
.venv-pages\Scripts\zensical build --clean
.venv-pages\Scripts\python website/check.py
.venv-pages\Scripts\zensical serve
```

On Linux/macOS use `.venv-pages/bin/` instead of `.venv-pages\Scripts\`.
Keep local virtual environments untracked. `check.py` validates page titles,
image alt attributes, local page/asset targets, fragment anchors, sitemap and
required outputs. It makes no requests to third-party sites.

Review the homepage and representative guides on desktop and mobile, both
reading themes, search results, download destinations, and the project URL's
`/DS4Windows/` path prefix before publication. Do not include real device IDs,
private logs or personal file paths in newly published screenshots.

## Publish

The **Documentation site** workflow builds and validates pull requests without
deploying them. Pushes to `main`, published stable releases and manual dispatch
can publish through GitHub Actions. Pages must use **GitHub Actions** as its
source, with the `github-pages` environment allowing `main` to deploy.

Deployments use the standard Pages artifact and OIDC workflow; they do not
need a long-lived deployment token. A release event builds the current `main`
documentation. Preview builds never publish. `site_url` must retain the project
path unless a custom domain is deliberately configured.

Download buttons use GitHub's `releases/latest` destination, where visitors can
choose the installer or portable ZIP and read current release notes. The website
does not distribute its own copy of Windows binaries.
