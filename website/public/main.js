// FileAction website: language switch, live download links from the latest GitHub release,
// and the animated wheel demo in the hero.
(() => {
  "use strict";

  const REPO = "Metamorfolog/file_action";
  const root = document.documentElement;
  const $ = (sel, el = document) => el.querySelector(sel);
  const $$ = (sel, el = document) => Array.from(el.querySelectorAll(sel));

  // ---------- Language ----------
  const store = {
    get(key) { try { return localStorage.getItem(key); } catch { return null; } },
    set(key, value) { try { localStorage.setItem(key, value); } catch { /* private mode */ } },
  };
  const params = new URLSearchParams(location.search);
  let lang = params.get("lang") || store.get("lang") || "en";
  const setLang = value => {
    lang = value === "cs" ? "cs" : "en";
    root.dataset.lang = lang;
    root.lang = lang;
    renderCaption();
  };
  $("#lang-toggle").addEventListener("click", () => {
    setLang(lang === "cs" ? "en" : "cs");
    store.set("lang", lang);
  });

  // ---------- Downloads ----------
  const isLinux = /Linux|X11/.test(navigator.userAgent) && !/Android/.test(navigator.userAgent);
  const mb = bytes => `${Math.round(bytes / 1048576)} MB`;

  function applyRelease(release) {
    const find = re => (release.assets || []).find(a => re.test(a.name));
    const assets = {
      setup: find(/Setup-.*\.exe$/i),
      zip: find(/win-x64\.zip$/i),
      linux: find(/linux-x64\.tar\.xz$/i),
      pkgbuild: find(/^PKGBUILD$/),
    };
    for (const [key, asset] of Object.entries(assets)) {
      if (!asset) continue;
      $$(`.js-dl-${key}`).forEach(a => { a.href = asset.browser_download_url; });
      $$(`.js-size-${key}`).forEach(el => { el.textContent = mb(asset.size); });
    }
    if (assets.setup && !isLinux) $$(".js-dl-primary").forEach(a => { a.href = assets.setup.browser_download_url; });
    if (assets.linux && isLinux) $$(".js-dl-primary").forEach(a => { a.href = assets.linux.browser_download_url; });
    if (release.tag_name) $$(".js-version").forEach(el => { el.textContent = release.tag_name; });
  }

  function primaryForLinux() {
    if (!isLinux) return;
    const primary = $(".js-dl-primary");
    primary.href = $(".js-dl-linux").href;
    $(".js-dl-primary-label").innerHTML =
      '<span data-l="en">Download for Linux</span><span data-l="cs">Stáhnout pro Linux</span>';
    $(".js-dl-meta").innerHTML =
      '<span data-l="en">Tarball <b class="js-size-linux">32 MB</b> · x64 · no .NET needed · MIT license</span>' +
      '<span data-l="cs">Archiv <b class="js-size-linux">32 MB</b> · x64 · bez .NET · licence MIT</span>';
    $(".js-dl-primary .ico")?.remove();
  }

  async function loadRelease() {
    const cacheKey = "release-cache";
    try {
      const cached = JSON.parse(sessionStorage.getItem(cacheKey) || "null");
      if (cached) return applyRelease(cached);
    } catch { /* ignore */ }
    try {
      const response = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, { headers: { Accept: "application/vnd.github+json" } });
      if (!response.ok) return;
      const release = await response.json();
      const slim = { tag_name: release.tag_name, assets: (release.assets || []).map(a => ({ name: a.name, size: a.size, browser_download_url: a.browser_download_url })) };
      try { sessionStorage.setItem(cacheKey, JSON.stringify(slim)); } catch { /* ignore */ }
      applyRelease(slim);
    } catch { /* offline or rate-limited: the links in the page point at v1.4.0 */ }
  }

  // ---------- Reveal on scroll ----------
  function reveal() {
    const targets = $$(".section h2, .section-lead, .wheel-card, .feature, .fmt, .dl, .terminal, .stats div");
    if (!("IntersectionObserver" in window) || matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const io = new IntersectionObserver(entries => {
      for (const entry of entries) {
        if (entry.isIntersecting) { entry.target.classList.add("in"); io.unobserve(entry.target); }
      }
    }, { rootMargin: "0px 0px -8% 0px" });
    targets.forEach((el, i) => {
      el.classList.add("reveal");
      el.style.transitionDelay = `${(i % 3) * 70}ms`;
      io.observe(el);
    });
  }

  // ---------- Wheel demo ----------
  const SVG = "http://www.w3.org/2000/svg";
  const C = 200, R = 182, r = 78, HUB = 70;
  const KEYS = ["Shift", "Ctrl", "Alt"];

  const scenes = [
    {
      file: "holiday.heic", ext: "HEIC", key: "Shift", target: 2, out: "holiday.webp",
      slices: [["JPG"], ["PNG"], ["WEBP"], ["AVIF"], ["PDF"], ["TIFF"]],
      caption: { en: "<kbd>Shift</kbd> Convert: a HEIC photo to WebP", cs: "<kbd>Shift</kbd> Převést: fotka HEIC na WebP" },
    },
    {
      file: "clip.mov", ext: "MOV", key: "Shift", target: 3, out: "clip.gif",
      slices: [["MP4"], ["MKV"], ["WEBM"], ["GIF", "animation", "animace"], ["MP3", "audio only", "jen zvuk"]],
      caption: { en: "<kbd>Shift</kbd> Convert: a screen recording to GIF", cs: "<kbd>Shift</kbd> Převést: záznam obrazovky na GIF" },
    },
    {
      file: "scan.png", ext: "PNG", key: "Ctrl", target: 4, out: "scan.txt",
      slices: [["Compress", "smaller file", "menší soubor", "Zmenšit"], ["1920 px", "max size", "max. velikost"], ["Rotate", "90°", "90°", "Otočit"], ["Mono", "grayscale", "odstíny šedi"], ["Text", "OCR", "OCR"], ["No GPS", "strip metadata", "bez metadat", "Bez GPS"]],
      caption: { en: "<kbd>Ctrl</kbd> Tools: read the text out of a scan (OCR)", cs: "<kbd>Ctrl</kbd> Nástroje: přečíst text ze skenu (OCR)" },
    },
    {
      file: "contract.docx", ext: "DOCX", key: "Shift", target: 0, out: "contract.pdf",
      slices: [["PDF"], ["ODT"], ["MD", "Markdown", "Markdown"], ["HTML"], ["TXT"], ["EPUB"]],
      caption: { en: "<kbd>Shift</kbd> Convert: a Word document to PDF", cs: "<kbd>Shift</kbd> Převést: dokument Wordu do PDF" },
    },
    {
      file: "invoice.pdf", ext: "PDF", key: "Alt", target: 1, out: "Invoices / invoice.pdf",
      slices: [["Copy to", "Documents", "Dokumenty", "Kopírovat"], ["Move to", "Invoices", "Faktury", "Přesunout"], ["Path", "copy", "kopírovat", "Cesta"], ["Terminal", "open here", "otevřít zde", "Terminál"], ["Shelf", "park it", "odložit", "Polička"], ["Share", "nearby", "nablízko", "Sdílet"]],
      caption: { en: "<kbd>Alt</kbd> Actions: move a file to a favourite folder", cs: "<kbd>Alt</kbd> Akce: přesunout soubor do oblíbené složky" },
    },
  ];

  const stage = $("#stage");
  const wheel = $("#wheel");
  const chip = $("#chip");
  const cursor = $("#cursor");
  const keycap = $("#keycap");
  const toast = $("#toast");
  const caption = $("#demo-caption");
  let current = scenes[0];

  function renderCaption() {
    caption.innerHTML = current.caption[lang] || current.caption.en;
  }

  const el = (name, attrs, parent) => {
    const node = document.createElementNS(SVG, name);
    for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
    if (parent) parent.appendChild(node);
    return node;
  };
  const polar = (radius, deg) => {
    const a = (deg * Math.PI) / 180;
    return [C + radius * Math.cos(a), C + radius * Math.sin(a)];
  };
  const sliceAngle = (i, n) => -90 + (360 / n) * i;

  function drawWheel(scene) {
    wheel.textContent = "";
    const defs = el("defs", {}, wheel);
    const grad = el("linearGradient", { id: "hot", x1: "0", y1: "0", x2: "1", y2: "1" }, defs);
    el("stop", { offset: "0", "stop-color": "#ff8a1f" }, grad);
    el("stop", { offset: "1", "stop-color": "#ff4f6a" }, grad);
    const glow = el("filter", { id: "glow", x: "-30%", y: "-30%", width: "160%", height: "160%" }, defs);
    el("feDropShadow", { dx: "0", dy: "0", stdDeviation: "9", "flood-color": "#ff8a1f", "flood-opacity": "0.7" }, glow);

    el("circle", { cx: C, cy: C, r: R + 10, fill: "rgba(22,22,27,0.94)", stroke: "rgba(255,255,255,0.08)" }, wheel);
    const n = scene.slices.length;
    const step = 360 / n;
    scene.paths = [];
    scene.slices.forEach(([label, subEn, subCs, labelCs], i) => {
      const mid = sliceAngle(i, n);
      const a0 = mid - step / 2, a1 = mid + step / 2;
      const [x0, y0] = polar(R, a0), [x1, y1] = polar(R, a1);
      const [x2, y2] = polar(r, a1), [x3, y3] = polar(r, a0);
      const large = step > 180 ? 1 : 0;
      const path = el("path", {
        class: "slice",
        d: `M${x0},${y0} A${R},${R} 0 ${large} 1 ${x1},${y1} L${x2},${y2} A${r},${r} 0 ${large} 0 ${x3},${y3} Z`,
      }, wheel);
      scene.paths.push(path);
      const [lx, ly] = polar((R + r) / 2 + 4, mid);
      const sub = lang === "cs" ? subCs : subEn;
      const text = el("text", { class: "label", x: lx, y: sub ? ly - 8 : ly }, wheel);
      text.textContent = (lang === "cs" && labelCs) || label;
      if (sub) el("text", { class: "sub", x: lx, y: ly + 13 }, wheel).textContent = sub;
    });
    el("circle", { class: "hub-ring", cx: C, cy: C, r: HUB + 7 }, wheel);
    el("circle", { class: "hub", cx: C, cy: C, r: HUB }, wheel);
    el("text", { class: "hub-fmt", x: C, y: C - 12 }, wheel).textContent = scene.ext;
    el("text", { class: "hub-name", x: C, y: C + 6 }, wheel).textContent = scene.file;
    KEYS.forEach((key, i) => {
      const x = C - 38 + i * 38;
      const on = key === scene.key;
      el("rect", { class: on ? "key-on" : "key-bg", x: x - 17, y: C + 20, width: 34, height: 17, rx: 8.5 }, wheel);
      el("text", { class: on ? "key on" : "key", x, y: C + 29 }, wheel).textContent = key;
    });
  }

  // Positions are in stage pixels; the wheel covers 82% of the stage, centred.
  function stagePoint(svgX, svgY) {
    const size = stage.clientWidth;
    const scale = (size * 0.82) / 400;
    return [size * 0.09 + svgX * scale, size * 0.09 + svgY * scale];
  }
  // Just below the target slice's label, so the label stays readable under the cursor.
  function dropPoint(scene) {
    const [x, y] = stagePoint(...polar((R + r) / 2 + 4, sliceAngle(scene.target, scene.slices.length)));
    return [x - 6, y + stage.clientWidth * 0.045];
  }
  const place = (node, [x, y]) => { node.style.transform = `translate(${x}px, ${y}px)`; };
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

  let running = false;
  let visible = true;
  let generation = 0;

  async function play() {
    if (running) return;
    running = true;
    const myGeneration = ++generation;
    let index = 0;
    const alive = () => myGeneration === generation && visible && !document.hidden;
    while (alive()) {
      const scene = scenes[index % scenes.length];
      current = scene;
      renderCaption();
      await runScene(scene, alive);
      index++;
    }
    running = false;
  }

  async function runScene(scene, alive) {
    const size = stage.clientWidth;
    $("#desk-name").textContent = scene.file;
    $("#desk-ext").textContent = scene.ext;
    $("#chip-ext").textContent = scene.ext;
    $("#keycap-label").textContent = scene.key;
    $("#toast-name").textContent = scene.out;
    drawWheel(scene);
    wheel.classList.remove("open", "dropped");
    toast.classList.remove("show", "run", "done");
    keycap.classList.remove("show", "down");
    chip.classList.remove("held", "gone");
    $("#desk-file").classList.remove("ghost");

    const start = [size * 0.12 + 27, size * 0.14 + 30];
    chip.style.transition = "none";
    cursor.style.transition = "none";
    place(chip, start);
    place(cursor, start);
    void chip.offsetWidth;
    chip.style.transition = "";
    cursor.style.transition = "";

    await wait(700); if (!alive()) return;
    chip.classList.add("held");
    $("#desk-file").classList.add("ghost");
    await wait(250); if (!alive()) return;

    const center = stagePoint(C, C);
    place(chip, center);
    place(cursor, center);
    await wait(850); if (!alive()) return;

    keycap.classList.add("show");
    await wait(220); if (!alive()) return;
    keycap.classList.add("down");
    wheel.classList.add("open");
    await wait(260); if (!alive()) return;
    keycap.classList.remove("down");

    const n = scene.slices.length;
    const target = dropPoint(scene);
    await wait(350); if (!alive()) return;
    place(chip, target);
    place(cursor, target);
    await wait(480); if (!alive()) return;
    scene.paths[scene.target].classList.add("hot");
    await wait(650); if (!alive()) return;

    chip.classList.add("gone");
    wheel.classList.add("dropped");
    keycap.classList.remove("show");
    await wait(300); if (!alive()) return;

    toast.classList.add("show");
    await wait(80);
    toast.classList.add("run");
    await wait(1050); if (!alive()) return;
    toast.classList.add("done");
    await wait(1700); if (!alive()) return;
    toast.classList.remove("show");
    await wait(400);
  }

  function staticFrame() {
    const scene = scenes[0];
    current = scene;
    renderCaption();
    drawWheel(scene);
    wheel.classList.add("open");
    scene.paths[scene.target].classList.add("hot");
    const target = dropPoint(scene);
    chip.classList.add("held");
    place(chip, target);
    place(cursor, target);
    $("#desk-file").classList.add("ghost");
  }

  function startDemo() {
    if (matchMedia("(prefers-reduced-motion: reduce)").matches) return staticFrame();
    if ("IntersectionObserver" in window) {
      new IntersectionObserver(([entry]) => {
        visible = entry.isIntersecting;
        if (visible) play();
      }, { threshold: 0.25 }).observe(stage);
    }
    document.addEventListener("visibilitychange", () => { if (!document.hidden) play(); });
    play();
  }

  // Redraw the wheel labels when the language changes mid-scene.
  $("#lang-toggle").addEventListener("click", () => {
    const hot = current.paths?.findIndex(p => p.classList.contains("hot"));
    const open = wheel.classList.contains("open");
    drawWheel(current);
    if (hot >= 0) current.paths[hot].classList.add("hot");
    if (open) wheel.classList.add("open");
  });

  setLang(lang);
  primaryForLinux();
  loadRelease();
  reveal();
  startDemo();
})();
