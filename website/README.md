# Luotsi Website

This directory contains the GitHub Pages site for Luotsi.

The stack is Astro + Starlight with a custom landing page at `/` and curated docs under `/docs/`.

## Commands

Run these commands from `website/`.

| Command | Action |
|---|---|
| `npm install` | Install dependencies |
| `npm run dev` | Start the local dev server |
| `npm run build` | Build the static site into `dist/` |
| `npm run check` | Run Astro project validation |
| `npm run preview` | Preview the production build locally |

## Deployment

GitHub Pages deploys from `.github/workflows/pages.yml` and publishes the built output to `https://digablesolutions.github.io/luotsi/`.

Keep the site base path in `astro.config.mjs` aligned with the GitHub Pages route.
