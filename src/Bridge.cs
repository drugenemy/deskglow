namespace Deskglow;

/// <summary>
/// Script injected into every wallpaper page.
///
/// The desktop icon layer sits on top of the wallpaper, so the page never gets real
/// mouse input. The engine watches the mouse itself and posts messages; this script
/// turns them into normal pointer/mouse events on the element under the cursor.
/// Ordinary web demos that listen for mousemove/click work unchanged.
///
/// Extra API for wallpapers:  window.deskglow.on('pause' | 'resume', fn)
///                            window.deskglow.paused  (bool)
///                            window.deskglow.drawLockBackground(ctx, w, h)   optional, for the lock screen
///                            window.deskglow.lockTheme()                     optional, widget colors/fonts/side
/// </summary>
internal static class Bridge
{
    public const string Script = """
(() => {
  if (window.deskglow) return;
  const dg = window.deskglow = {
    paused: false,
    mouse: { x: 0, y: 0, down: false },
    on(type, fn) { window.addEventListener('deskglow:' + type, e => fn(e.detail)); },
  };
  function fire(target, Type, name, init) {
    try { target.dispatchEvent(new Type(name, init)); } catch (_) {}
  }
  // ── lock screen image ────────────────────────────────────────────────────
  // The engine sends {type:'lock', w, h, data}. The page may define
  //   deskglow.drawLockBackground(ctx, w, h)  a clean still frame (no clock, no UI)
  //   deskglow.lockTheme()                    { side, fg, accent, light, sans, serif }
  // Without drawLockBackground the engine sends a screenshot instead (m.bg).
  const post = msg => window.chrome?.webview?.postMessage(msg);
  const loadImg = src => new Promise((ok, no) => { const i = new Image(); i.onload = () => ok(i); i.onerror = no; i.src = src; });
  async function renderLock(m) {
    const c = document.createElement('canvas'); c.width = m.w; c.height = m.h;
    const g = c.getContext('2d');
    const own = typeof dg.drawLockBackground === 'function';
    if (m.bg) g.drawImage(await loadImg(m.bg), 0, 0, m.w, m.h);
    else if (own) await dg.drawLockBackground(g, m.w, m.h);
    else { post({ type: 'lock-need-bg' }); return; }
    const t = Object.assign({ side: 'left', fg: '#ece6d6', accent: '#8fae93', light: false,
      sans: '"Bahnschrift","Segoe UI",sans-serif', serif: '"Georgia",serif' }, own && dg.lockTheme ? dg.lockTheme() : {});
    if (!own) {        // unknown page: a soft shade behind the widgets so they stay readable
      const s = g.createLinearGradient(t.side === 'left' ? 0 : m.w, 0, t.side === 'left' ? m.w * .5 : m.w * .5, 0);
      s.addColorStop(0, 'rgba(0,0,0,.55)'); s.addColorStop(1, 'rgba(0,0,0,0)');
      g.fillStyle = s; g.fillRect(0, 0, m.w, m.h);
    }
    drawWidgets(g, m.w, m.h, m.data || {}, t);
    post({ type: 'lock-image', data: c.toDataURL('image/jpeg', .93) });
  }
  function drawWidgets(g, w, h, d, t) {
    const k = h / 940, left = t.side !== 'right';
    const X = left ? w * .07 : w * .93, align = left ? 'left' : 'right', dir = left ? 1 : -1;
    const fg = a => t.light ? `rgba(14,15,13,${a})` : (t.fg.startsWith('#') ? hexA(t.fg, a) : t.fg);
    function hexA(hx, a) { const n = parseInt(hx.slice(1), 16); return `rgba(${n >> 16 & 255},${n >> 8 & 255},${n & 255},${a})`; }
    function text(s, x, y, font, color, spacing = 0, al = align) {
      g.save(); g.font = font; g.fillStyle = color; g.textAlign = al; g.textBaseline = 'alphabetic';
      if (spacing) g.letterSpacing = spacing + 'px';
      g.fillText(s, x, y); g.restore();
    }
    const label = (s, y) => text(s.toUpperCase(), X, y, `500 ${13 * k}px ${t.sans}`, fg(.62), 4 * k);
    const now = d.now ? new Date(d.now) : new Date();
    let y = h * .34;
    const order = d.widgets || ['weather', 'note', 'year'];
    let first = true;
    const rule = () => { if (!first) { g.fillStyle = fg(.22); g.fillRect(left ? X : X - 170 * k, y, 170 * k, Math.max(1, k)); y += 46 * k; } first = false; };

    for (const wd of order) {
      if (wd === 'weather' && d.weather) {
        rule();
        const W = d.weather;
        label(`${W.city} · weather`, y); y += 104 * k;
        g.save(); g.font = `200 ${112 * k}px ${t.sans}`; g.fontStretch = 'condensed'; g.fillStyle = fg(.95);
        g.textAlign = align; g.fillText(`${W.temp}°`, X, y); g.restore();
        y += 38 * k;
        text(`${W.text}   ·   H ${W.hi}°  L ${W.lo}°`, X, y, `400 ${19 * k}px ${t.sans}`, fg(.85), 1 * k); y += 46 * k;
        const days = W.days || [];
        days.forEach((dd, i) => {
          const col = left ? X + i * 128 * k : X - (days.length - 1 - i) * 128 * k;
          const name = new Date(dd.date + 'T12:00').toLocaleDateString('en-GB', { weekday: 'short' }).toUpperCase();
          text(name, col, y, `500 ${12 * k}px ${t.sans}`, fg(.55), 3 * k);
          text(`${dd.hi}° / ${dd.lo}°`, col, y + 26 * k, `400 ${17 * k}px ${t.sans}`, fg(.9));
          text(dd.text, col, y + 48 * k, `400 ${12 * k}px ${t.sans}`, fg(.5));
        });
        y += 74 * k;
        text(`as of ${W.at}`, X, y, `400 ${11 * k}px ${t.sans}`, fg(.35), 2 * k); y += 44 * k;
      }
      if (wd === 'note' && d.note && d.note.trim()) {
        rule();
        g.font = `italic 400 ${30 * k}px ${t.serif}`;
        const words = d.note.trim().split(/\s+/), lines = []; let line = '';
        for (const wv of words) { const test = line ? line + ' ' + wv : wv; if (g.measureText(test).width > 520 * k && line) { lines.push(line); line = wv; } else line = test; }
        if (line) lines.push(line);
        const shown = lines.slice(0, 4);
        g.fillStyle = t.accent; g.fillRect(left ? X : X - 2 * k, y - 26 * k, 2 * k, shown.length * 40 * k);
        shown.forEach((ln, i) => text(ln, X + dir * 20 * k, y + i * 40 * k, `italic 400 ${30 * k}px ${t.serif}`, fg(.92)));
        y += shown.length * 40 * k + 30 * k;
      }
      if (wd === 'year') {
        rule();
        const start = new Date(now.getFullYear(), 0, 1), end = new Date(now.getFullYear() + 1, 0, 1);
        const doy = Math.floor((now - start) / 864e5) + 1, total = Math.round((end - start) / 864e5);
        const pct = Math.floor((now - start) / (end - start) * 100);
        const th = new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()));
        th.setUTCDate(th.getUTCDate() + 4 - (th.getUTCDay() || 7));
        const week = Math.ceil(((th - new Date(Date.UTC(th.getUTCFullYear(), 0, 1))) / 864e5 + 1) / 7);
        label(`day ${doy} / ${total}   ·   week ${week}`, y); y += 22 * k;
        const bw = 300 * k, bx = left ? X : X - bw;
        g.fillStyle = fg(.18); g.fillRect(bx, y, bw, 3 * k);
        g.fillStyle = t.accent; g.fillRect(left ? bx : bx + bw * (1 - pct / 100), y, bw * pct / 100, 3 * k);
        y += 34 * k;
        text(`${pct}% of ${now.getFullYear()} is gone`, X, y, `400 ${17 * k}px ${t.sans}`, fg(.8)); y += 40 * k;
      }
    }
  }

  window.chrome?.webview?.addEventListener('message', ev => {
    const m = ev.data || {};
    if (m.type === 'mouse') {
      const r = window.devicePixelRatio || 1;
      const x = m.x / r, y = m.y / r;
      dg.mouse.x = x; dg.mouse.y = y; dg.mouse.down = !!m.buttons;
      const target = document.elementFromPoint(x, y) || document.body || document.documentElement;
      const base = { clientX: x, clientY: y, screenX: x, screenY: y, bubbles: true, cancelable: true,
                     composed: true, view: window, buttons: m.buttons | 0, button: m.button | 0 };
      const ptr = { ...base, pointerId: 1, pointerType: 'mouse', isPrimary: true };
      if (m.kind === 'move') {
        fire(target, PointerEvent, 'pointermove', ptr);
        fire(target, MouseEvent, 'mousemove', base);
      } else if (m.kind === 'down') {
        fire(target, PointerEvent, 'pointerdown', ptr);
        fire(target, MouseEvent, 'mousedown', base);
      } else if (m.kind === 'up') {
        fire(target, PointerEvent, 'pointerup', ptr);
        fire(target, MouseEvent, 'mouseup', base);
        if (m.button === 0) fire(target, MouseEvent, 'click', base);
        else fire(target, MouseEvent, 'contextmenu', base);
      }
      return;
    }
    if (m.type === 'lock') { renderLock(m).catch(e => post({ type: 'lock-error', message: String(e) })); return; }
    if (m.type === 'pause') dg.paused = true;
    if (m.type === 'resume') dg.paused = false;
    window.dispatchEvent(new CustomEvent('deskglow:' + m.type, { detail: m }));
  });
})();
""";
}
