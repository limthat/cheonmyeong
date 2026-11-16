# HUD 부품 (Frame.aseprite 색 · 디테일 기준)
import os
from PIL import Image, ImageDraw
D = os.path.dirname(os.path.abspath(__file__))
H = lambda c: tuple(int(c[i:i + 2], 16) for i in (1, 3, 5)) + (255,)
INK=H('#080710'); WL=H('#FFF7EC'); HJ=H('#F3E8CB'); W=H('#FCF6F6'); TAN=H('#C3A781'); TAND=H('#B69D7B')
BR=H('#916A3E'); BRD=H('#61473A'); Y=H('#E7BA41'); BL=H('#2D518B'); R=H('#B42C3B'); RD=H('#882131')
FILLS = {'hp': (R, H('#D9545F')), 'exp': (BL, H('#5A7CB8')), 'boss': (RD, R), 'grudge': (H('#2A1A33'), H('#4B2E5E'))}

def fill(k):
    c, t = FILLS[k]; im = Image.new('RGBA', (16, 8), c); d = ImageDraw.Draw(im)
    d.line((0, 0, 15, 0), fill=t); return im

def bar_frame(w=64, h=16):
    im = Image.new('RGBA', (w, h)); d = ImageDraw.Draw(im)
    d.rectangle((0, 0, w - 1, h - 1), fill=INK); d.rectangle((1, 1, w - 2, h - 2), fill=HJ)
    d.line((1, 1, w - 2, 1), fill=WL); d.line((1, h - 2, w - 2, h - 2), fill=TAN)
    d.rectangle((3, 3, w - 4, h - 4), fill=BRD, outline=INK)
    for x0 in (1, w - 3):
        d.rectangle((x0, 3, x0 + 1, h - 4), fill=Y); d.point((x0, 3), fill=WL)
    return im

def corner(d, x, y, sx, sy):  # Frame 모서리: 황 삼각 + 적 빗금
    for i in range(6):
        for j in range(6 - i): d.point((x + sx * i, y + sy * j), fill=Y)
    for k in range(5): d.point((x + sx * k, y + sy * (4 - k)), fill=R)

def boss_frame(w=256, h=64):
    im = Image.new('RGBA', (w, h)); d = ImageDraw.Draw(im); top = 14
    o = 0
    for c, t in [(INK, 1), (R, 2), (W, 1), (BL, 3), (Y, 2), (BR, 1)]:
        d.rectangle((o, top + o, w - 1 - o, h - 1 - o), fill=c); o += t
    d.rectangle((o, top + o, w - 1 - o, h - 1 - o), fill=HJ)
    for (x, y, sx, sy) in [(1, top + 1, 1, 1), (w - 2, top + 1, -1, 1), (1, h - 2, 1, -1), (w - 2, h - 2, -1, -1)]: corner(d, x, y, sx, sy)
    gx0, gx1 = 22, w - 23
    d.rectangle((gx0, top + 14, gx1, top + 27), fill=BRD, outline=INK)   # 보스 체력 홈 (안 12)
    d.rectangle((gx0, top + 31, gx1, top + 40), fill=BRD, outline=INK)   # 원한 홈 (안 8)
    for x in (12, w - 17):  # 양 끝 뇌문
        d.rectangle((x, top + 15, x + 4, top + 19), outline=TAN); d.point((x + 2, top + 17), fill=TAN)
        d.rectangle((x, top + 34, x + 4, top + 38), outline=TAN); d.point((x + 2, top + 36), fill=TAN)
        d.line((x + 2, top + 20, x + 2, top + 33), fill=TAN)
    d.rectangle((16, 0, 112, top + 6), fill=INK); d.rectangle((18, 2, 110, top + 4), fill=H('#1A1420'))
    d.line((18, 2, 110, 2), fill=RD); d.point((18, top + 4), fill=Y); d.point((110, top + 4), fill=Y)
    return im

def slot(kind):
    bc, cc = {'skill': (BL, Y), 'awaken': (Y, R), 'portrait': (R, Y)}[kind]
    im = Image.new('RGBA', (32, 32)); d = ImageDraw.Draw(im)
    d.rectangle((0, 0, 31, 31), fill=INK); d.rectangle((1, 1, 30, 30), fill=bc)
    d.line((1, 1, 30, 1), fill=WL if kind != 'skill' else H('#5A7CB8'))
    d.rectangle((4, 4, 27, 27), fill=INK); d.rectangle((5, 5, 26, 26), fill=HJ); d.line((5, 5, 26, 5), fill=TAN)
    for (x, y) in [(1, 1), (29, 1), (1, 29), (29, 29)]: d.rectangle((x, y, x + 1, y + 1), fill=cc)
    return im

def nine(im, w, h, b):
    Wd, Hd = im.size; out = Image.new('RGBA', (w, h))
    xs = [(0, b, 0, b), (b, Wd - b, b, w - b), (Wd - b, Wd, w - b, w)]; ys = [(0, b, 0, b), (b, Hd - b, b, h - b), (Hd - b, Hd, h - b, h)]
    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if dx1 > dx0 and dy1 > dy0: out.paste(im.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.NEAREST), (dx0, dy0))
    return out

if __name__ == '__main__':
    OUT = D + '/out'; os.makedirs(OUT, exist_ok=True)
    parts = {'hud_bar_frame_64x16': bar_frame(), 'hud_boss_frame_256x64': boss_frame(),
             'hud_slot_skill_32': slot('skill'), 'hud_slot_awaken_32': slot('awaken'), 'hud_slot_portrait_32': slot('portrait')}
    for k in FILLS: parts[f'hud_fill_{k}_16x8'] = fill(k)
    for n, im in parts.items(): im.save(f'{OUT}/{n}.png')
    # 미리보기 (640x360 위에 실제 배치 -> x3)
    pv = Image.new('RGBA', (640, 360), H('#1B2233')); d = ImageDraw.Draw(pv)
    for y in range(0, 360, 32):
        for x in range(0, 640, 32): d.rectangle((x, y, x + 31, y + 31), outline=H('#222A3D'))
    pv.alpha_composite(parts['hud_boss_frame_256x64'], (192, 6))
    pv.alpha_composite(nine(parts['hud_fill_boss_16x8'], 170, 12, 1), (215, 34))
    pv.alpha_composite(nine(parts['hud_fill_grudge_16x8'], 70, 8, 1), (215, 51))
    for i in range(2):
        y = 8 + i * 40; pv.alpha_composite(parts['hud_slot_portrait_32'], (8, y))
        pv.alpha_composite(nine(parts['hud_bar_frame_64x16'], 96, 12, 4), (44, y + 2))
        pv.alpha_composite(nine(parts['hud_fill_hp_16x8'], [70, 55][i], 6, 1), (48, y + 5))
    for i in range(6): pv.alpha_composite(parts['hud_slot_skill_32'], (8 + i * 34, 318))
    pv.alpha_composite(parts['hud_slot_awaken_32'], (600, 318))
    pv.alpha_composite(nine(parts['hud_bar_frame_64x16'], 640 - 16, 10, 4), (8, 306))
    pv.alpha_composite(nine(parts['hud_fill_exp_16x8'], 300, 4, 1), (12, 309))
    pv.resize((1920, 1080), Image.NEAREST).save(OUT + '/_preview_1920x1080.png')
    sheet = Image.new('RGBA', (300, 120), H('#1B2233'))
    x = 6
    for n in ['hud_bar_frame_64x16', 'hud_fill_hp_16x8', 'hud_fill_exp_16x8', 'hud_fill_boss_16x8', 'hud_fill_grudge_16x8', 'hud_slot_skill_32', 'hud_slot_awaken_32', 'hud_slot_portrait_32']:
        sheet.alpha_composite(parts[n], (x, 6)); x += parts[n].width + 6
    sheet.alpha_composite(parts['hud_boss_frame_256x64'], (6, 48))
    sheet.resize((1200, 480), Image.NEAREST).save(OUT + '/_parts_x4.png')
    print(len(parts))
