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
    if (m.type === 'pause') dg.paused = true;
    if (m.type === 'resume') dg.paused = false;
    window.dispatchEvent(new CustomEvent('deskglow:' + m.type, { detail: m }));
  });
})();
""";
}
