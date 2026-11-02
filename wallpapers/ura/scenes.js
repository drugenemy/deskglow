// ─────────────────────────────────────────────────────────────────────────────
//  Settings for the 裏 (ura) wallpaper. Edit this file to make it yours.
//
//  Put your images in the img/ folder and list them below. Portrait images work
//  best. Every field except `img` is optional; the wallpaper picks colors from
//  the image when you leave them out.
//
//  scene fields
//    img      path to the image
//    side     'right' | 'left' | 'flush'   where the art sits (flush = glued to the right edge)
//    title    one big character shown faintly behind the text (e.g. '月')
//    label    short scene name
//    quote    two lines of text
//    accent   accent color, e.g. '#6e9a73'
//    bg       background color [r, g, b] (auto from the image edges if left out)
//    light    true for light backgrounds (auto if left out)
//    fade     how much to fade each edge of the art [left, right, top, bottom], 0..0.5
//    fx       'pipe' | 'embers' | 'fireflies' | 'petals' | 'none'
//    pipe     [x, y]                   where the smoke comes out ('pipe'), in image pixels
//    line     [[x1, y1], [x2, y2]]     a glowing line that throws sparks ('embers')
//    area     [x1, y1, x2, y2]         where fireflies live ('fireflies')
//    ash      true to add drifting ash
// ─────────────────────────────────────────────────────────────────────────────
window.URA = {
  name: 'DESKGLOW',                         // giant outlined word at the bottom
  sign: '裏側',                              // vertical sign, top-left (2–4 characters)
  signLines: ['THE OTHER SIDE', 'A DESKGLOW WALLPAPER'],
  words: ['THE OTHER SIDE', '裏', 'EVERYTHING HAS A HIDDEN SIDE', '墨', 'DESKGLOW', '夜と紙', 'INK · PAPER · LIGHT', '静'],

  cutEvery: 14,          // seconds between small band sweeps
  sceneEvery: 120,       // seconds between scene changes

  scenes: [
    { img: 'img/ink-night.webp', side: 'right', title: '夜', label: 'NIGHT', accent: '#8fae93',
      quote: ['Everything in this world', 'has a hidden side.'],
      fade: [.3, .12, .04, .1], fx: 'fireflies', area: [80, 700, 900, 1100] },
    { img: 'img/ink-paper.webp', side: 'flush', title: '紙', label: 'PAPER', accent: '#b8392a',
      quote: ['The quietest moves', 'are the strongest.'],
      fade: [.34, 0, .04, 0], fx: 'petals', ash: true },
  ],

  // extra images that only appear inside the sweeping bands
  bandImages: ['img/ink-night.webp', 'img/ink-paper.webp'],
};
