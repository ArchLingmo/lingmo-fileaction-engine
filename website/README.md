# FileAction website

A single static page (`public/`) with no build step. Its only third-party request goes to the GitHub API for the latest release, so the download links and sizes stay current. If that request fails, the links point at the release the page was written for.

## Host it

Copy `public/` to any static web server; no build step or server-side code is needed.

## Edit it

- `public/index.html`: all the text, in English and Czech (`data-l="en"` / `data-l="cs"`). The visitor's browser language picks one, and the CZ/EN button switches.
- `public/main.js`: the language switch, the release links and the animated wheel in the hero. `scenes` lists the files and slices the animation plays.
- `public/style.css`: the dark theme, with the same orange-to-coral accent as the app.
- `public/assets/og.png`: the preview image for Twitter and other link previews (1200×630). The page loads it from raw.githubusercontent.com, so it works on any domain.

To preview locally, run `python3 -m http.server -d website/public 8000` and open http://localhost:8000.
