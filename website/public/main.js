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
    $$("[data-ph-en]").forEach(el => { el.placeholder = el.dataset[lang === "cs" ? "phCs" : "phEn"]; });
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
  // Geometry, colours and labels follow the app's wheel (src/FileAction.App/Wheel/WheelWindow.cs).
  const SVG = "http://www.w3.org/2000/svg";
  const OUTER = 196, INNER = 80, RIM = 9, GAP = 6, CORNER = 11, POP = 7, PAD = 16;
  const VB = 2 * (OUTER + RIM + PAD), C = VB / 2;
  const LABEL_R = INNER + (OUTER - INNER) * 0.54;
  const KEYS = ["Shift", "Ctrl", "Alt"];
  const WHEEL_NAMES = { Shift: { en: "Convert", cs: "Převést" }, Ctrl: { en: "Tools", cs: "Nástroje" }, Alt: { en: "Actions", cs: "Akce" } };

  // Line icons standing in for the app's Segoe Fluent glyphs.
  const ICONS = {
    shrink: "M14 10l6-6M14 10h4.5M14 10V5.5M10 14l-6 6M10 14H5.5M10 14v4.5",
    rotate: "M12 4.5a7.5 7.5 0 1 1-6.8 4.4M5 4v5h5",
    palette: "M12 3.5a8.5 8.5 0 0 0 0 17c1.3 0 1.9-1 1.4-2.1-.6-1.3.3-2.6 1.7-2.6h2.2a3.2 3.2 0 0 0 3.2-3.2c0-5.2-3.8-9.1-8.5-9.1ZM8 10.5h.01M11 7.5h.01M15 8.5h.01",
    text: "M5 6h14M12 6v12M9 18h6",
    clear: "M6.5 6.5l11 11M17.5 6.5l-11 11",
    copy: "M9 8.5h10v12H9zM6 16.5H5v-12h10v1",
    move: "M3.5 6.5h5.5l2 2h9.5v11h-17zM11 14h6M14.5 11.5 17 14l-2.5 2.5",
    link: "M10.5 13.5a3.8 3.8 0 0 1 0-5.4l2.2-2.2a3.8 3.8 0 0 1 5.4 5.4l-1.1 1.1M13.5 10.5a3.8 3.8 0 0 1 0 5.4l-2.2 2.2a3.8 3.8 0 0 1-5.4-5.4l1.1-1.1",
    terminal: "M4 5h16v14H4zM7.5 10l2.5 2-2.5 2M12 14.5h4",
    pin: "M14.5 3.5l6 6-2.8 1.2-3.7 3.7.6 3.8-1.8 1.3-3.5-3.5L4.5 20.5M9.3 16l-5.3-5.3 1.3-1.8 3.8.6 3.7-3.7z",
    share: "M14 5l6 5.5-6 5.5v-3c-4.5 0-7.5 1.5-10 5 .8-5.5 4-8.8 10-9.5z",
  };

  const L = (en, cs = en) => ({ en, cs });
  const slice = (label, sub, icon) => ({
    label: typeof label === "string" ? L(label) : label,
    sub: typeof sub === "string" ? L(sub) : sub,
    icon,
  });

  const scenes = [
    {
      file: "holiday.heic", ext: "HEIC", key: "Shift", target: 2, out: "holiday.webp",
      slices: [slice("JPG"), slice("PNG"), slice("WEBP"), slice("AVIF"), slice("PDF"), slice("TIFF")],
      caption: { en: "<kbd>Shift</kbd> Convert: a HEIC photo to WebP", cs: "<kbd>Shift</kbd> Převést: fotka HEIC na WebP" },
    },
    {
      file: "clip.mov", ext: "MOV", key: "Shift", target: 3, out: "clip.gif",
      slices: [slice("MP4"), slice("MKV"), slice("WEBM"), slice("GIF", L("animation", "animace")), slice("MP3", L("audio only", "jen zvuk"))],
      caption: { en: "<kbd>Shift</kbd> Convert: a screen recording to GIF", cs: "<kbd>Shift</kbd> Převést: záznam obrazovky na GIF" },
    },
    {
      file: "scan.png", ext: "PNG", key: "Ctrl", target: 5, out: "scan.txt",
      slices: [
        slice(L("Compress", "Komprimovat"), null, "shrink"),
        slice(L("Shrink", "Zmenšit"), "50 %", "shrink"),
        slice("Max 1920 px", L("for web", "na web"), "shrink"),
        slice(L("Rotate", "Otočit"), L("right", "doprava"), "rotate"),
        slice(L("Grayscale", "Černobíle"), null, "palette"),
        slice(L("Extract text", "Text z obrázku"), "OCR", "text"),
        slice(L("No metadata", "Bez metadat"), "GPS, EXIF", "clear"),
      ],
      caption: { en: "<kbd>Ctrl</kbd> Tools: read the text out of a scan (OCR)", cs: "<kbd>Ctrl</kbd> Nástroje: přečíst text ze skenu (OCR)" },
    },
    {
      file: "contract.docx", ext: "DOCX", key: "Shift", target: 0, out: "contract.pdf",
      slices: [slice("PDF", L("document", "dokument")), slice("ODT"), slice("MD", "Markdown"), slice("HTML"), slice("TXT"), slice("EPUB")],
      caption: { en: "<kbd>Shift</kbd> Convert: a Word document to PDF", cs: "<kbd>Shift</kbd> Převést: dokument Wordu do PDF" },
    },
    {
      file: "invoice.pdf", ext: "PDF", key: "Alt", target: 1, out: "Invoices / invoice.pdf",
      slices: [
        slice(L("Copy", "Kopírovat"), L("to folder…", "do složky…"), "copy"),
        slice(L("Move", "Přesunout"), L("to folder…", "do složky…"), "move"),
        slice(L("Copy path", "Kopírovat cestu"), null, "link"),
        slice(L("Terminal", "Terminál"), L("open here", "otevřít zde"), "terminal"),
        slice(L("Float", "Plovoucí panel"), L("keep for later", "odložit na později"), "pin"),
        slice(L("Share", "Sdílet"), "Windows", "share"),
      ],
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
  // 0° is straight up and angles grow clockwise, as in the app.
  const polar = (radius, deg) => {
    const a = (deg * Math.PI) / 180;
    return [C + radius * Math.sin(a), C - radius * Math.cos(a)];
  };
  const pt = (radius, deg) => polar(radius, deg).map(v => v.toFixed(2)).join(",");
  const rad = deg => (deg * Math.PI) / 180;
  const deg = r => (r * 180) / Math.PI;

  // A ring segment with a constant-width gap to its neighbours and rounded corners.
  function sector(a0, a1) {
    const edge = (r, a, side) => a + side * deg(Math.asin(GAP / 2 / r));
    const outerStart = edge(OUTER, a0, 1), outerEnd = edge(OUTER, a1, -1);
    const innerStart = edge(INNER, a0, 1), innerEnd = edge(INNER, a1, -1);
    const cOut = Math.min(CORNER, rad(outerEnd - outerStart) * OUTER * 0.45);
    const cIn = Math.min(CORNER * 0.7, rad(innerEnd - innerStart) * INNER * 0.45);
    const dOut = deg(cOut / OUTER), dIn = deg(cIn / INNER);
    const largeOut = outerEnd - outerStart - 2 * dOut > 180 ? 1 : 0;
    const largeIn = innerEnd - innerStart - 2 * dIn > 180 ? 1 : 0;
    return [
      `M${pt(OUTER, outerStart + dOut)}`,
      `A${OUTER},${OUTER} 0 ${largeOut} 1 ${pt(OUTER, outerEnd - dOut)}`,
      `Q${pt(OUTER, outerEnd)} ${pt(OUTER - cOut, edge(OUTER - cOut, a1, -1))}`,
      `L${pt(INNER + cIn, edge(INNER + cIn, a1, -1))}`,
      `Q${pt(INNER, innerEnd)} ${pt(INNER, innerEnd - dIn)}`,
      `A${INNER},${INNER} 0 ${largeIn} 0 ${pt(INNER, innerStart + dIn)}`,
      `Q${pt(INNER, innerStart)} ${pt(INNER + cIn, edge(INNER + cIn, a0, 1))}`,
      `L${pt(OUTER - cOut, edge(OUTER - cOut, a0, 1))}`,
      `Q${pt(OUTER, outerStart)} ${pt(OUTER, outerStart + dOut)}Z`,
    ].join("");
  }

  // Formats on the Convert wheel are big and bare; everything else gets an icon.
  const isFormat = s => !s.icon;

  function sliceLabel(group, s, mid, width) {
    const sub = s.sub && s.sub[lang];
    const parts = [];
    if (s.icon) parts.push({ h: 20 + 5 });
    parts.push({ h: isFormat(s) ? 23 : 16 });
    if (sub) parts.push({ h: 14 });
    const total = parts.reduce((sum, p) => sum + p.h, 0);
    const [mx, my] = polar(LABEL_R, mid);
    const g = el("g", { class: "label-box" }, group);
    let y = my - total / 2;
    if (s.icon) {
      el("path", { class: "icon", d: ICONS[s.icon], transform: `translate(${mx - 10} ${y}) scale(${20 / 24})` }, g);
      y += 25;
    }
    const h = isFormat(s) ? 23 : 16;
    el("text", { class: isFormat(s) ? "label fmt" : "label", x: mx, y: y + h / 2 }, g).textContent = s.label[lang];
    y += h;
    if (sub) el("text", { class: "sub", x: mx, y: y + 7.5 }, g).textContent = sub;
    // Shrink a label that is wider than its slice instead of spilling into the neighbours.
    const box = g.getBBox();
    if (box.width > width) {
      const k = width / box.width;
      g.setAttribute("transform", `translate(${mx} ${my}) scale(${k}) translate(${-mx} ${-my})`);
    }
  }

  function hubText(scene) {
    const hot = scene.slices[scene.hot];
    scene.hubTitle.textContent = hot ? hot.label[lang] : WHEEL_NAMES[scene.key][lang];
    scene.hubDetail.textContent = (hot && hot.sub && hot.sub[lang]) || scene.file;
  }

  function drawWheel(scene) {
    wheel.textContent = "";
    wheel.setAttribute("viewBox", `0 0 ${VB} ${VB}`);
    const defs = el("defs", {}, wheel);
    const grad = el("linearGradient", { id: "hot", x1: "0", y1: "0", x2: "1", y2: "1" }, defs);
    el("stop", { offset: "0", "stop-color": "#ff8a1f" }, grad);
    el("stop", { offset: "1", "stop-color": "#ff4f6a" }, grad);
    const glow = el("filter", { id: "glow", x: "-40%", y: "-40%", width: "180%", height: "180%" }, defs);
    el("feDropShadow", { dx: "0", dy: "0", stdDeviation: "9", "flood-color": "#ff8a1f", "flood-opacity": "0.75" }, glow);
    const shadow = el("filter", { id: "shadow", x: "-20%", y: "-20%", width: "140%", height: "140%" }, defs);
    el("feDropShadow", { dx: "0", dy: "3", stdDeviation: "11", "flood-color": "#000", "flood-opacity": "0.55" }, shadow);
    const hubShadow = el("filter", { id: "hub-shadow", x: "-30%", y: "-30%", width: "160%", height: "160%" }, defs);
    el("feDropShadow", { dx: "0", dy: "3", stdDeviation: "5", "flood-color": "#000", "flood-opacity": "0.55" }, hubShadow);

    el("circle", { class: "backdrop", cx: C, cy: C, r: OUTER + RIM }, wheel);
    const n = scene.slices.length;
    const step = 360 / n;
    const width = n <= 2 ? 120 : Math.min(110, Math.max(48, 2 * LABEL_R * Math.sin(rad(step / 2)) * 0.84));
    scene.hot = -1;
    scene.groups = scene.slices.map((s, i) => {
      const mid = i * step;
      const pop = el("g", { class: "pop" }, wheel);
      pop.dataset.dx = (POP * Math.sin(rad(mid))).toFixed(2);
      pop.dataset.dy = (-POP * Math.cos(rad(mid))).toFixed(2);
      const grow = el("g", { class: "grow", style: `transition-delay: ${14 * i}ms` }, pop);
      el("path", { class: "slice", d: sector(mid - step / 2, mid + step / 2) }, grow);
      sliceLabel(grow, s, mid, width);
      return pop;
    });

    el("circle", { class: "hub", cx: C, cy: C, r: INNER - 6 }, wheel);
    // File icon, then the title, the detail line and the wheel keys, as in the app's hub.
    const doc = el("g", { class: "hub-doc", transform: `translate(${C - 13} ${C - 50})` }, wheel);
    el("path", { class: "doc-page", d: "M3 0h14l9 9v21a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V2a2 2 0 0 1 2-2Z" }, doc);
    el("path", { class: "doc-fold", d: "M17 0v7a2 2 0 0 0 2 2h7Z" }, doc);
    el("rect", { class: "doc-badge", x: 4, y: 18, width: 18, height: 9, rx: 2 }, doc);
    scene.hubTitle = el("text", { class: "hub-title", x: C, y: C - 3 }, wheel);
    scene.hubDetail = el("text", { class: "hub-detail", x: C, y: C + 15 }, wheel);
    hubText(scene);

    const tabW = KEYS.map(k => k.length * 6.4 + 14);
    const trackW = tabW.reduce((a, b) => a + b, 0) + 4;
    let x = C - trackW / 2;
    el("rect", { class: "track", x, y: C + 30, width: trackW, height: 22, rx: 10 }, wheel);
    x += 2;
    KEYS.forEach((key, i) => {
      const on = key === scene.key;
      if (on) el("rect", { class: "tab-on", x, y: C + 32, width: tabW[i], height: 18, rx: 8 }, wheel);
      el("text", { class: on ? "tab on" : "tab", x: x + tabW[i] / 2, y: C + 41.5 }, wheel).textContent = key;
      x += tabW[i];
    });
  }

  function setHot(scene, index) {
    scene.groups.forEach((g, i) => {
      const on = i === index;
      g.classList.toggle("hot", on);
      g.style.transform = on && scene.groups.length > 1 ? `translate(${g.dataset.dx}px, ${g.dataset.dy}px)` : "";
    });
    scene.hot = index;
    hubText(scene);
  }

  // Positions are in stage pixels; the wheel covers 82% of the stage, centred.
  function stagePoint(svgX, svgY) {
    const size = stage.clientWidth;
    const scale = (size * 0.82) / VB;
    return [size * 0.09 + svgX * scale, size * 0.09 + svgY * scale];
  }
  // Just below the target slice's label, so the label stays readable under the cursor.
  function dropPoint(scene) {
    const [x, y] = stagePoint(...polar(LABEL_R, scene.target * (360 / scene.slices.length)));
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

    const target = dropPoint(scene);
    await wait(350); if (!alive()) return;
    place(chip, target);
    place(cursor, target);
    await wait(480); if (!alive()) return;
    setHot(scene, scene.target);
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
    setHot(scene, scene.target);
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
    const hot = current.hot ?? -1;
    const open = wheel.classList.contains("open");
    drawWheel(current);
    if (hot >= 0) setHot(current, hot);
    if (open) wheel.classList.add("open");
  });

  // ---------- Feedback ----------
  function feedback() {
    const form = $("#feedback-form");
    if (!form) return;
    const status = $("#feedback-status");
    const say = (en, cs, ok) => {
      status.textContent = lang === "cs" ? cs : en;
      status.className = "feedback-status " + (ok ? "ok" : "err");
    };
    form.addEventListener("submit", async e => {
      e.preventDefault();
      const data = new FormData(form);
      const message = String(data.get("message") || "").trim();
      const email = String(data.get("email") || "").trim();
      if (message.length < 3) return say("Please write a few words first.", "Napište prosím aspoň pár slov.", false);
      if (email && !form.elements.email.checkValidity()) return say("That e-mail doesn't look right.", "Ten e-mail nevypadá správně.", false);
      const button = form.querySelector("button[type=submit]");
      button.disabled = true;
      say("Sending…", "Odesílám…", true);
      try {
        const r = await fetch("/api/feedback", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ kind: data.get("kind"), message, email, website: data.get("website"), source: "web", lang }),
        });
        if (r.ok) {
          form.reset();
          say("Thank you! Your message has been sent.", "Díky! Zpráva je odeslaná.", true);
        } else if (r.status === 429) {
          say("Too many messages in a short time. Try again in a few minutes.", "Moc zpráv za krátkou dobu. Zkuste to za pár minut.", false);
        } else {
          say("Sending failed. Please try again later.", "Odeslání se nepovedlo. Zkuste to prosím později.", false);
        }
      } catch {
        say("Sending failed. Check your connection and try again.", "Odeslání se nepovedlo. Zkontrolujte připojení a zkuste to znovu.", false);
      } finally {
        button.disabled = false;
      }
    });
  }

  // ---------- Support ----------
  function support() {
    const button = $("#sol-copy");
    if (!button) return;
    const note = $("#sol-copied");
    button.addEventListener("click", async () => {
      const address = button.dataset.copy;
      let ok = false;
      try {
        await navigator.clipboard.writeText(address);
        ok = true;
      } catch {
        // Older browsers or no clipboard permission: select the address so it can be copied by hand.
        const range = document.createRange();
        range.selectNodeContents($("#sol-address"));
        const selection = getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
      }
      note.textContent = ok
        ? (lang === "cs" ? "Zkopírováno" : "Copied")
        : (lang === "cs" ? "Adresa je označená, zkopírujte ji Ctrl+C" : "Address selected, press Ctrl+C");
      setTimeout(() => { note.textContent = ""; }, 2500);
    });
  }

  setLang(lang);
  feedback();
  support();
  primaryForLinux();
  loadRelease();
  reveal();
  startDemo();
})();
