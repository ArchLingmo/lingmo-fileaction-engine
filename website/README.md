# FileAction website

A single static page (`public/`) with no build step. Its only third-party request goes to the GitHub API for the latest release, so the download links and sizes stay current. If that request fails, the links point at the release the page was written for.

## Run it on a server

You need a Linux server with Docker and the compose plugin, with ports 80 and 443 open.

```sh
git clone https://github.com/Metamorfolog/file_action.git
cd file_action/website/deploy
./up.sh
```

`up.sh` looks up the server's public IP address and serves the page at `https://<ip-with-dashes>.sslip.io` (for example `https://203-0-113-7.sslip.io`). Caddy gets an HTTPS certificate automatically.

**Using your own domain later:** point the domain's DNS `A` record at the server, then run:

```sh
SITE_ADDRESS=fileaction.example.com ./up.sh
```

The address is saved in `deploy/.env`, so the site comes back on its own after a reboot.

**Updating the page:** run `git pull` in the repository. Caddy serves the files straight from `public/`, so no restart is needed.

**Stopping it:** run `docker compose down` in `deploy/`.

## Edit it

- `public/index.html`: all the text, in English and Czech (`data-l="en"` / `data-l="cs"`). The visitor's browser language picks one, and the CZ/EN button switches.
- `public/main.js`: the language switch, the release links and the animated wheel in the hero. `scenes` lists the files and slices the animation plays.
- `public/style.css`: the dark theme, with the same orange-to-coral accent as the app.
- `public/assets/og.png`: the preview image for Twitter and other link previews (1200×630). The page loads it from raw.githubusercontent.com, so it works on any domain.

To preview locally, run `python3 -m http.server -d website/public 8000` and open http://localhost:8000.
