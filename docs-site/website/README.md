# Website

This website is built using [Docusaurus](https://docusaurus.io/), a modern static website generator, matching the layout of [ryandanthony/dottie](https://github.com/ryandanthony/dottie)'s docs site (QUESTIONS A4).

Content lives one level up in [`../docs`](../docs) so the site and its Markdown sources sit together under `docs-site/`; this directory (`website/`) holds only the Docusaurus app (config, theme, build tooling).

## Installation

```bash
npm ci
```

## Local development

```bash
npm start
```

Starts a local dev server and opens a browser window. Most changes are reflected live without restarting the server.

## Build

```bash
npm run build
```

Generates static content into `build/`, which can be served by any static host. CI runs this with `--` no extra flags; `onBrokenLinks`/`onBrokenMarkdownLinks` are set to `throw`, so a broken internal link fails the build (the closest equivalent to `mkdocs build --strict`).

## Deployment

Deployed automatically to GitHub Pages on every push to `main` that touches `docs-site/**` (see [`.github/workflows/docs.yml`](../../.github/workflows/docs.yml)), publishing to `https://ryandanthony.github.io/nats-auth-callout-service/`.
