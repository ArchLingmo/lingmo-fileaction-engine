# FileAction

🇬🇧 [English](README.md) · 🇨🇿 **Čeština**

**Převod souborů bez klikání pro Windows (a Linux).** Chyťte soubor myší, během tažení ťukněte na klávesu a pusťte ho na formát v kruhovém menu. Vše běží lokálně, žádné soubory se nikam neodesílají.

Inspirováno skvělou aplikací [Tangerine](https://tangerineformac.com/) pro macOS. FileAction s ní není nijak spojený, jde o nezávislou obdobu pro Windows.

<p align="center">
  <img src="docs/screenshots/cs/wheel-video-convert.png" width="32%" alt="Kolo Převést pro video" />
  <img src="docs/screenshots/cs/wheel-image-tools.png" width="32%" alt="Kolo Nástroje pro obrázek" />
  <img src="docs/screenshots/cs/wheel-actions.png" width="32%" alt="Kolo Akce" />
</p>
<p align="center">
  <img src="docs/screenshots/cs/wheel-pdf-convert.png" width="32%" alt="Kolo Převést pro PDF" />
  <img src="docs/screenshots/cs/settings.png" width="32%" alt="Nastavení" />
  <img src="docs/screenshots/cs/jobs.png" width="32%" alt="Průběh" />
</p>

## Jak to funguje

1. Chyťte soubor: v Průzkumníku, na ploše, obrázek nebo odkaz v prohlížeči, přílohu e-mailu, soubor uvnitř ZIP složky…
2. Během tažení **ťukněte** na klávesu:

   | Klávesa | Kolo | Co nabízí |
   |---|---|---|
   | **Shift** | Převést | JPG, PNG, WebP, MP4, MP3, DOCX, PDF, XLSX… podle typu souboru |
   | **Ctrl** | Nástroje | komprimovat, zmenšit, otočit, oříznout, spojit, rozdělit, zabalit… |
   | **Alt** | Akce | kopírovat do…, přesunout do…, kopírovat cestu, terminál, plovoucí panel, sdílet |

3. Pusťte soubor na výseč. Výsledek se uloží vedle originálu (originál zůstane beze změny). Puštěním do středu kola akci zrušíte.

Kolo zůstane otevřené, dokud soubor nepustíte. Klávesu tedy můžete pustit a další klávesou přepnout na jiné kolo. Klávesy lze v nastavení přehodit.

**Zopakovat poslední akci:** během tažení ťukněte na stejnou klávesu znovu (nebo na otevřeném kole stiskněte **R**). Kolo se změní na jednu výseč, která zopakuje to, co jste dělali naposledy, se stejným nastavením (třeba stejné časy oříznutí).

**Vlastní kolo:** v **Nastavení → Upravit kolo** označíte oblíbené výseče hvězdičkou (budou vždy první a na stejném místě) a nepoužívané skryjete.

**Bez myši:** vyberte soubory v Průzkumníku, nebo zkopírujte soubor či snímek obrazovky (**Win + Shift + S**), a stiskněte **Ctrl + Alt + Mezerník**. Šipkami vyberete výseč, Enter potvrdí, Tab přepne kolo, Esc zavře, čísla 1–9 spustí výseč přímo.
Kolo otevřete i přes **pravé tlačítko → FileAction…** (ve Windows 11 pod „Zobrazit další možnosti“).

Průběh převodu se ukazuje v rohu obrazovky. Odtud jde výsledek otevřít, ukázat ve složce nebo převod zrušit.

## Co umí převést

| Typ | Vstup | Výstup |
|---|---|---|
| Obrázky | JPG, PNG, WebP, HEIC, TIFF, AVIF, BMP, GIF, ICO, SVG | JPG, PNG, WebP, AVIF, TIFF, ICO, PDF (GIF → MP4/WebM) |
| Audio | MP3, M4A, AAC, WAV, FLAC, OGG, Opus, AIFF, WMA | MP3, M4A, WAV, FLAC, OGG, Opus, AIFF |
| Video | MP4, MOV, MKV, WebM, AVI, WMV, FLV, MPG, 3GP, TS | MP4, MOV, MKV, WebM, GIF, MP3 |
| PDF | PDF | DOCX, JPG, PNG (všechny strany, 300 DPI), PPTX, TXT, MD, HTML, EPUB |
| Dokumenty | DOCX, DOC, ODT, RTF, MD, TXT, HTML, EPUB | DOCX, PDF, ODT, MD, HTML, TXT, EPUB, RTF |
| Prezentace | PPTX, PPT, ODP | PDF, PNG, JPG (všechny snímky), PPTX, ODP |
| Tabulky | XLSX, XLSM, XLS, ODS, CSV, TSV | XLSX, CSV, TSV, ODS, PDF |
| Titulky | SRT, VTT | SRT, VTT, TXT |
| Archivy | ZIP, TAR, TAR.GZ, GZ, BZ2, XZ, RAR, 7z | rozbalit, ZIP, TAR, TAR.GZ |

**Nástroje (Ctrl):**
- obrázky: komprimovat, zmenšit na 50 %, max. 1920 px, otočit, zrcadlit, oříznout na čtverec, černobíle, odstranit metadata (GPS, EXIF), spojit do PDF,
- video: komprimovat (H.264), 720p, ztlumit, oříznout podle času, otočit, zrychlit 2×, náhledový snímek,
- audio: komprimovat, normalizovat hlasitost, oříznout, spojit,
- PDF: spojit, rozdělit po stranách, otočit, optimalizovat,
- obrázky a naskenovaná PDF: **text z obrázku (OCR)** pomocí rozpoznávání vestavěného ve Windows; text se uloží jako .txt a zkopíruje do schránky,
- cokoli včetně složek: zabalit do ZIP / TAR.GZ.

Co ještě není soubor (obrázek přetažený z prohlížeče, příloha z Outlooku, snímek obrazovky ve schránce), se nejdřív uloží do **Stažené soubory\FileAction** a výsledky vzniknou vedle něj.

U více vybraných souborů se převede každý zvlášť. Nástroje typu „spojit“ vytvoří jeden výsledek.

### Pomocné programy

Obrázky, PDF, tabulky, titulky a archivy zvládá FileAction sám. Některé formáty potřebují další programy:

- **FFmpeg** (audio a video) a **Pandoc** (dokumenty) jsou přibalené v instalátoru,
- **LibreOffice** (DOC, PPTX, ODS, převod dokumentů a tabulek do PDF) je potřeba doinstalovat zvlášť: `winget install TheDocumentFoundation.LibreOffice`.

Co chybí, ukáže **Nastavení → Pomocné programy**, odkud jde i rovnou nainstalovat. Kolo nabízí jen převody, pro které je nástroj k dispozici.

## Instalace

Stáhněte `FileAction-Setup-x.y.z.exe` z [Releases](../../releases) nebo z artefaktů posledního běhu [GitHub Actions](../../actions). Instalátor nepotřebuje práva správce. Jde také o přenosnou verzi v `.zip`.

Aplikace je česky i anglicky a ve světlém i tmavém režimu; obojí se řídí nastavením Windows, jazyk jde přepnout v nastavení.

Při prvním spuštění se FileAction zapíše do automatického spouštění a do kontextové nabídky Průzkumníka (obojí jde vypnout v nastavení). Pak běží v oznamovací oblasti vedle hodin.

**Aktualizace** se instalují samy: FileAction se občas zeptá GitHubu na nejnovější vydání (o vašich souborech se nic neodesílá), nabídne aktualizaci v oznámení a v nabídce u hodin, ověří otisk SHA-256 instalátoru a nainstaluje ho na pozadí. Kontrolu jde v nastavení vypnout. Přenosná verze místo toho otevře stránku ke stažení.

Požadavky: Windows 10 (1809+) nebo Windows 11, x64.

## Linux

```sh
yay -S fileaction-bin        # Arch Linux (AUR)
```

Ostatní distribuce: stáhněte `FileAction-x.y.z-linux-x64.tar.gz` z [Releases](../../releases), rozbalte a spusťte `fileaction/app/fileaction` (.NET není potřeba).

Linux aplikacím nedovolí sledovat klávesnici a myš jiných programů (Wayland to záměrně zakazuje), takže gesto „táhnu a ťuknu na klávesu“ je jen pro Windows. Na Linuxu se stejné kolo používá takto:

- **Kolo pro přetahování:** spusťte `fileaction` (nebo *FileAction* z nabídky aplikací) a přetáhněte na kolo soubory. Zůstane na obrazovce, posunout ho jde za střed.
- **Pravé tlačítko ve správci souborů:** `fileaction integrate` přidá *FileAction…* do Nautilu (podnabídka *Skripty*), Dolphinu a Nema (balíček z AUR instaluje Dolphin a Nemo pro všechny).
- **Příkazová řádka** a klávesové zkratky prostředí:

  ```sh
  fileaction foto.heic                        # kolo pro tento soubor
  fileaction list foto.heic                   # co se s ním dá dělat
  fileaction run image.to.webp *.heic         # provést, bez okna
  fileaction run video.trim klip.mov --param start=0:10 --param end=0:25
  ```

Volitelné balíčky: `ffmpeg` (audio, video), `pandoc-cli` (dokumenty), `libreoffice-fresh` (Office), `tesseract` + `tesseract-data-ces` (text z obrázku). Nastavení je v `~/.config/fileaction/settings.json`.

## Sestavení ze zdrojů

```powershell
# .NET 10 SDK
./scripts/fetch-tools.ps1                      # stáhne ffmpeg.exe a pandoc.exe do .\tools
dotnet test tests/FileAction.Tests             # testy převodů
dotnet publish src/FileAction.App -c Release -r win-x64 --self-contained -o publish
Copy-Item tools publish\tools -Recurse
publish\FileAction.exe
```

`FileAction.exe --self-test <složka>` sestaví všechna okna mimo obrazovku a uloží jejich náhledy do PNG v obou jazycích (používá CI).

### Vydání nové verze

Pošlete do `main` commit, jehož zpráva začíná `Release v1.2.3` (nebo pushněte tag `v1.2.3`, případně ručně spusťte workflow **Build** s číslem verze). CI sestaví instalátor a zip a zveřejní je jako vydání na GitHubu. Když do repozitáře přidáte secret `RELEASE_TOKEN` (osobní přístupový token s oprávněním *Contents: read and write* k tomuto repozitáři), vydání se zveřejní pod vaším účtem místo github-actions.

Stejný běh sestaví i balíček pro Linux, na Arch Linuxu ho zkušebně nainstaluje a když secret `AUR_SSH_PRIVATE_KEY` obsahuje klíč účtu AUR, který spravuje `fileaction-bin`, nahraje nový PKGBUILD do AUR.

## Struktura

```
src/FileAction.Core     převody a nástroje (multiplatformní knihovna, testovaná i na Linuxu)
  Backends/             ImageMagick, FFmpeg, Pandoc, LibreOffice, PDF, tabulky, titulky, archivy
  Operations/Catalog.cs seznam všech výsečí na kolech „Převést“ a „Nástroje“
src/FileAction.Linux    aplikace pro Linux (Avalonia): kolo pro přetahování, příkazová řádka, napojení na správce souborů
src/FileAction.App      aplikace pro Windows (WPF)
  Input/GlobalInput.cs  globální sledování myši a kláves (gesto „táhnu + ťuknu“)
  Wheel/                kruhové menu a logika, kdy se otevře
  Actions/              kolo „Akce“, plovoucí panel, sdílení
  Jobs/                 běh převodů na pozadí a karty s průběhem
  Shell/                ikona v oznamovací oblasti, zkratka, kontextová nabídka, jediná instance
tests/FileAction.Tests  jednotkové testy a integrační testy, které spustí každý převod na vzorových souborech
installer/              Inno Setup
packaging/              balíček pro Linux a šablona PKGBUILD pro AUR
```

## Rozdíly oproti Tangerine

- Klávesy Shift / Ctrl / Alt místo Shift / Command / Control.
- Formáty Apple (Pages, Keynote, RTFD) a zápis do HEIC nejsou podporované. HEIC jde jen otevřít.
- PDF → DOCX/EPUB/HTML převádí text, ne přesné rozvržení stránky (to nabízí PDF → PPTX nebo obrázky).
- Akce „AirDrop“ odpovídá ve Windows nabídce **Sdílet** (Sdílení nablízko, Teams, Mail…).
