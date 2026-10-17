# FileAction

**Převod souborů bez klikání pro Windows.** Chyťte soubor myší, během tažení ťukněte na klávesu a pusťte ho na formát v kruhovém menu. Vše běží lokálně, žádné soubory se nikam neodesílají.

Inspirováno aplikací [Tangerine](https://tangerineformac.com/) pro macOS.

<p align="center"><img src="src/FileAction.App/Assets/FileAction.png" width="96" alt="" /></p>

## Jak to funguje

1. Chyťte soubor (v Průzkumníku, na ploše, v libovolné aplikaci, která umí přetahovat soubory).
2. Během tažení **ťukněte** na klávesu:

   | Klávesa | Kolo | Co nabízí |
   |---|---|---|
   | **Shift** | Převést | JPG, PNG, WebP, MP4, MP3, DOCX, PDF, XLSX… podle typu souboru |
   | **Ctrl** | Nástroje | komprimovat, zmenšit, otočit, oříznout, spojit, rozdělit, zabalit… |
   | **Alt** | Akce | kopírovat do…, přesunout do…, kopírovat cestu, terminál, plovoucí panel, sdílet |

3. Pusťte soubor na výseč. Výsledek se uloží vedle originálu (originál zůstane beze změny). Puštěním do středu kola akci zrušíte.

Kolo zůstane otevřené, dokud soubor nepustíte. Klávesu tedy můžete pustit a další klávesou přepnout na jiné kolo. Klávesy lze v nastavení přehodit.

**Bez myši:** vyberte soubory v Průzkumníku a stiskněte **Ctrl + Alt + Mezerník**. Šipkami vyberete výseč, Enter potvrdí, Tab přepne kolo, Esc zavře, čísla 1–9 spustí výseč přímo.
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
- cokoli včetně složek: zabalit do ZIP / TAR.GZ.

U více vybraných souborů se převede každý zvlášť. Nástroje typu „spojit“ vytvoří jeden výsledek.

### Pomocné programy

Obrázky, PDF, tabulky, titulky a archivy zvládá FileAction sám. Některé formáty potřebují další programy:

- **FFmpeg** (audio a video) a **Pandoc** (dokumenty) jsou přibalené v instalátoru,
- **LibreOffice** (DOC, PPTX, ODS, převod dokumentů a tabulek do PDF) je potřeba doinstalovat zvlášť: `winget install TheDocumentFoundation.LibreOffice`.

Co chybí, ukáže **Nastavení → Pomocné programy**, odkud jde i rovnou nainstalovat. Kolo nabízí jen převody, pro které je nástroj k dispozici.

## Instalace

Stáhněte `FileAction-Setup-x.y.z.exe` z [Releases](../../releases) nebo z artefaktů posledního běhu [GitHub Actions](../../actions). Instalátor nepotřebuje práva správce. Jde také o přenosnou verzi v `.zip`.

Při prvním spuštění se FileAction zapíše do automatického spouštění a do kontextové nabídky Průzkumníka (obojí jde vypnout v nastavení). Pak běží v oznamovací oblasti vedle hodin.

Požadavky: Windows 10 (1809+) nebo Windows 11, x64.

## Sestavení ze zdrojů

```powershell
# .NET 10 SDK
./scripts/fetch-tools.ps1                      # stáhne ffmpeg.exe a pandoc.exe do .\tools
dotnet test tests/FileAction.Tests             # testy převodů
dotnet publish src/FileAction.App -c Release -r win-x64 --self-contained -o publish
Copy-Item tools publish\tools -Recurse
publish\FileAction.exe
```

`FileAction.exe --self-test <složka>` sestaví všechna okna mimo obrazovku a uloží jejich náhledy do PNG (používá CI).

## Struktura

```
src/FileAction.Core     převody a nástroje (multiplatformní knihovna, testovaná i na Linuxu)
  Backends/             ImageMagick, FFmpeg, Pandoc, LibreOffice, PDF, tabulky, titulky, archivy
  Operations/Catalog.cs seznam všech výsečí na kolech „Převést“ a „Nástroje“
src/FileAction.App      aplikace pro Windows (WPF)
  Input/GlobalInput.cs  globální sledování myši a kláves (gesto „táhnu + ťuknu“)
  Wheel/                kruhové menu a logika, kdy se otevře
  Actions/              kolo „Akce“, plovoucí panel, sdílení
  Jobs/                 běh převodů na pozadí a karty s průběhem
  Shell/                ikona v oznamovací oblasti, zkratka, kontextová nabídka, jediná instance
tests/FileAction.Tests  jednotkové testy a integrační testy, které spustí každý převod na vzorových souborech
installer/              Inno Setup
```

## Rozdíly oproti Tangerine

- Klávesy Shift / Ctrl / Alt místo Shift / Command / Control.
- Formáty Apple (Pages, Keynote, RTFD) a zápis do HEIC nejsou podporované. HEIC jde jen otevřít.
- PDF → DOCX/EPUB/HTML převádí text, ne přesné rozvržení stránky (to nabízí PDF → PPTX nebo obrázky).
- Akce „AirDrop“ odpovídá ve Windows nabídce **Sdílet** (Sdílení nablízko, Teams, Mail…).
