# 천명 UI 키트 v3 — 밝은 한지 + 오방색 (창 · 패널 수정안에 맞춤). python3 make_kit_v3.py
import random, os
from PIL import Image, ImageDraw
D = os.path.dirname(os.path.abspath(__file__))
H = lambda c: tuple(int(c[i:i + 2], 16) for i in (1, 3, 5)) + (255,)
INK = H('#0B0A10'); BLUE = H('#2A5CAA'); BLUE_D = H('#1D4280'); RED = H('#D7263D'); RED_D = H('#9E1B2D'); YEL = H('#F2C230'); WHITE = H('#FFFDF5')
HJ = H('#F1E6CC'); HJ_L = H('#FBF4E4'); HJ_D = H('#DCCBA5'); HJ_DD = H('#C2AE86'); GREY = H('#8C93A3'); GROOVE = H('#5A463A')
def hanji(im, box, seed=1, base=HJ):
    x0, y0, x1, y1 = box; r = random.Random(seed); ImageDraw.Draw(im).rectangle(box, fill=base)
    for _ in range(int((x1 - x0 + 1) * (y1 - y0 + 1) * 0.05)): im.putpixel((r.randint(x0, x1), r.randint(y0, y1)), HJ_D if r.random() < .5 else HJ_L)
    for _ in range(int((x1 - x0 + 1) * (y1 - y0 + 1) * 0.003)):
        x, y = r.randint(x0, max(x0, x1 - 4)), r.randint(y0, y1)
        for k in range(r.randint(2, 5)): im.putpixel((min(x1, x + k), y), HJ_D)
def obang_tile(d, cx, cy, s):
    m = s // 2
    d.rectangle((cx, cy, cx + s - 1, cy + s - 1), fill=WHITE, outline=INK)
    d.polygon([(cx + m, cy + 1), (cx + s - 2, cy + m - 1), (cx + m, cy + m - 1)], fill=BLUE)
    d.polygon([(cx + s - 2, cy + m), (cx + m, cy + s - 2), (cx + m, cy + m)], fill=RED)
    d.polygon([(cx + 1, cy + m - 1), (cx + m - 1, cy + 1), (cx + m - 1, cy + m - 1)], fill=INK)
    d.polygon([(cx + m, cy + 1), (cx + s - 2, cy + m), (cx + m, cy + s - 2), (cx + 1, cy + m)], outline=INK)
    q = max(1, s // 6); d.rectangle((cx + m - q, cy + m - q, cx + m + q - 1, cy + m + q - 1), fill=YEL, outline=INK)
def panel(S, corner=True, seed=1, thin=False):
    im = Image.new('RGBA', (S, S)); hanji(im, (0, 0, S - 1, S - 1), seed); d = ImageDraw.Draw(im)
    rings = [(INK, 1), (RED, 2), (WHITE, 1), (BLUE, 2), (YEL, 1), (INK, 1)] if thin else [(INK, 1), (RED, 3), (WHITE, 1), (BLUE, 3), (YEL, 2), (INK, 1)]
    i = 0
    for c, w in rings:
        for _ in range(w): d.rectangle((i, i, S - 1 - i, S - 1 - i), outline=c); i += 1
    by = 4 if thin else 6
    for x in range(by + 2, S - by - 2, 6): im.putpixel((x, by), WHITE); im.putpixel((x, S - 1 - by), WHITE)
    for y in range(by + 2, S - by - 2, 6): im.putpixel((by, y), WHITE); im.putpixel((S - 1 - by, y), WHITE)
    if corner:
        s = 16 if S >= 128 else (11 if not thin else 9)
        for cx, cy in [(0, 0), (S - s, 0), (0, S - s), (S - s, S - s)]: obang_tile(d, cx, cy, s)
    return im
def button(kind='basic', W=128, Hh=32, seed=3):
    im = Image.new('RGBA', (W, Hh)); d = ImageDraw.Draw(im)
    if kind == 'primary':
        d.rectangle((0, 0, W - 1, Hh - 1), fill=RED); d.rectangle((1, Hh - 3, W - 2, Hh - 2), fill=RED_D); d.line((2, 1, W - 3, 1), fill=YEL); d.line((2, 2, W - 3, 2), fill=H('#E8505F'))
        for x0 in (2, W - 7):
            d.rectangle((x0, 4, x0 + 4, Hh - 5), fill=BLUE); d.line((x0 + 2, 4, x0 + 2, Hh - 5), fill=WHITE)
    else:
        base = {'press': HJ_D, 'lock': GREY}.get(kind, HJ)
        hanji(im, (0, 0, W - 1, Hh - 1), seed, base)
        if kind == 'lock': d.rectangle((1, 1, W - 2, Hh - 2), fill=GREY)
        d.rectangle((1, Hh - 3, W - 2, Hh - 2), fill=HJ_DD if kind != 'lock' else H('#6B7282'))
        if kind != 'lock':
            d.rectangle((1, 1, W - 2, 2), fill=YEL if kind == 'hover' else BLUE)
            for x0 in (2, W - 5): d.rectangle((x0, 4, x0 + 2, Hh - 5), fill=RED); d.line((x0 + 1, 4, x0 + 1, Hh - 5), fill=WHITE)
    d.rectangle((0, 0, W - 1, Hh - 1), outline=YEL if kind == 'hover' else INK)
    if kind == 'hover': d.rectangle((1, 1, W - 2, Hh - 2), outline=INK)
    for p in [(0, 0), (W - 1, 0), (0, Hh - 1), (W - 1, Hh - 1)]: im.putpixel(p, (0, 0, 0, 0))
    return im
def tab(on):
    im = Image.new('RGBA', (128, 32)); d = ImageDraw.Draw(im); hanji(im, (0, 0, 127, 31), 7)
    d.rectangle((1, 29, 126, 30), fill=HJ_DD)
    if on: d.rectangle((1, 25, 126, 30), fill=BLUE); d.line((1, 24, 126, 24), fill=INK); d.line((1, 26, 126, 26), fill=YEL)
    d.rectangle((0, 0, 127, 31), outline=INK)
    return im
def card(color, seed=5):
    im = panel(64, corner=True, seed=seed, thin=True); d = ImageDraw.Draw(im)
    d.rectangle((8, 8, 55, 14), fill=color); d.line((8, 15, 55, 15), fill=INK)
    if color == WHITE: d.line((8, 8, 55, 8), fill=BLUE)
    return im
def bar_frame():
    im = Image.new('RGBA', (64, 16)); d = ImageDraw.Draw(im); hanji(im, (0, 0, 63, 15), 9)
    d.rectangle((0, 0, 63, 15), outline=INK); d.rectangle((3, 3, 60, 12), fill=GROOVE, outline=INK)
    for x0 in (1, 61): d.line((x0, 2, x0, 13), fill=RED)
    return im
def fill(c, cd, hi):
    im = Image.new('RGBA', (16, 8), c); d = ImageDraw.Draw(im); d.line((0, 0, 15, 0), fill=hi); d.line((0, 7, 15, 7), fill=cd); return im
parts = {'frame_panel_64': panel(64), 'frame_panel_128': panel(128),
         'button_basic_128x32': button('basic'), 'button_hover_128x32': button('hover'), 'button_press_128x32': button('press'),
         'button_lock_128x32': button('lock'), 'button_primary_128x32': button('primary'),
         'tab_on_128x32': tab(True), 'tab_off_128x32': tab(False), 'bar_frame_64x16': bar_frame(),
         'card_common_64': card(YEL), 'card_exclusive_64': card(RED), 'card_consumable_64': card(BLUE), 'card_coop_64': card(WHITE),
         'fill_hp': fill(RED, RED_D, H('#F07A87')), 'fill_exp': fill(BLUE, BLUE_D, H('#7FA6E0')), 'fill_boss': fill(RED_D, INK, RED), 'fill_grudge': fill(INK, INK, H('#3F4352'))}
for n, im in parts.items(): im.save(os.path.join(D, n + '.png'))
print('v3 parts', len(parts))
