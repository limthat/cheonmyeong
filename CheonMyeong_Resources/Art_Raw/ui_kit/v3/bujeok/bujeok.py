# 신통력 부적 카드 + 불타는 연출 (아래 -> 위)
import os, random, math
import numpy as np
from PIL import Image, ImageDraw
import mock as M
from mock import H, INK, INK2, CREAM, RED, RED_D, BLUE, YEL, GREY, SH, GA, GAB

W = os.path.dirname(os.path.abspath(__file__)); OUT = W + '/bujeok'; os.makedirs(OUT, exist_ok=True)
CW, CH = 128, 224          # 부적 한 장 (640x360 기준, x3 = 384x672)
PAD = 16                   # 불티가 밖으로 튈 여백
PAPER, PAPER_L, PAPER_D = H('#F1D27A'), H('#F8E2A0'), H('#D9B04E')
JU, JU_D = H('#C8202F'), H('#8E1626')   # 주사(붉은 먹)
BAND = {'common': (H('#E0A82E'), INK2), 'exclusive': (RED, CREAM), 'consumable': (BLUE, CREAM), 'coop': (H('#FFFDF5'), INK2)}

def talisman(kind, seed=3):
    r = random.Random(seed); im = Image.new('RGBA', (CW, CH)); px = im.load()
    # 거친 가장자리
    lcut = [r.choice([0, 0, 0, 1]) for _ in range(CH)]; rcut = [r.choice([0, 0, 0, 1]) for _ in range(CH)]
    bcut = [r.choice([0, 0, 1, 1, 2]) for _ in range(CW)]
    for y in range(CH):
        for x in range(CW):
            if x < lcut[y] or x >= CW - rcut[y] or y >= CH - bcut[x]: continue
            v = r.random(); c = PAPER_L if v < .06 else PAPER_D if v < .10 else PAPER
            if y < 3 or x < 2 + lcut[y] or x > CW - 3 - rcut[y]: c = PAPER_D
            px[x, y] = c
    d = ImageDraw.Draw(im)
    # 바탕 부적 무늬 (옅은 주사 획)
    ov = Image.new('RGBA', (CW, CH)); od = ImageDraw.Draw(ov); a = (200, 32, 47, 46)
    od.line((64, 40, 64, 196), fill=a, width=3)
    for y0 in (70, 110, 150):
        od.arc((30, y0 - 18, 98, y0 + 18), 200, 340, fill=a, width=2)
    od.line((40, 180, 88, 180), fill=a, width=2); od.line((44, 188, 84, 196), fill=a, width=2)
    for k in range(6): od.line((22 + k * 17, 202, 30 + k * 17, 210), fill=a, width=2)
    im.alpha_composite(ov)
    # 붓으로 그은 이중 테두리
    def wob(x0, y0, x1, y1, w):
        for y in range(y0, y1 + 1):
            j = r.choice([0, 0, 0, 1])
            for t in range(w): px[x0 + j + t, y] = JU; px[x1 - j - t, y] = JU
        for x in range(x0, x1 + 1):
            j = r.choice([0, 0, 0, 1])
            for t in range(w): px[x, y0 + j + t] = JU; px[x, y1 - j - t] = JU
    wob(5, 5, CW - 6, CH - 7, 2); wob(10, 10, CW - 11, CH - 12, 1)
    # 머리띠 (종류 색)
    # 머리 칸 (빈 칸 · 색은 직접 채움)
    d.rectangle((13, 13, CW - 14, 32), fill=PAPER, outline=JU_D)
    if False:
        d.rectangle((CW // 2 - 7, CH - 30, CW // 2 + 7, CH - 16), fill=JU, outline=JU_D)
        d.line((CW // 2 - 3, CH - 26, CW // 2 + 3, CH - 26), fill=PAPER_L); d.line((CW // 2, CH - 26, CW // 2, CH - 19), fill=PAPER_L)
        d.line((CW // 2 - 4, CH - 20, CW // 2 + 4, CH - 20), fill=PAPER_L)
    return im

# ---- 불타는 순서 지도: 값이 작을수록 먼저 탐 (아래부터) ----
def vnoise(w, h, cell, seed):
    rs = np.random.RandomState(seed); g = rs.rand(h // cell + 2, w // cell + 2)
    ys, xs = np.mgrid[0:h, 0:w] / cell; x0, y0 = xs.astype(int), ys.astype(int); fx, fy = xs - x0, ys - y0
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a = g[y0, x0] * (1 - fx) + g[y0, x0 + 1] * fx; b = g[y0 + 1, x0] * (1 - fx) + g[y0 + 1, x0 + 1] * fx
    return a * (1 - fy) + b * fy
def burn_map(w=CW, h=CH, seed=7):
    n = vnoise(w, h, 24, seed) * .65 + vnoise(w, h, 8, seed + 1) * .35
    y = np.mgrid[0:h, 0:w][0] / (h - 1)
    m = (1 - y) * .84 + n * .16
    return (m - m.min()) / (m.max() - m.min())

EMBER = [H('#FFF3C2'), H('#FFE08A'), H('#F2C230'), H('#E8742A'), H('#D7263D')]
CHAR = np.array(H('#2B1E1A')[:3], float)
def burn(card, m, t, sparks):
    """card: RGBA (CW,CH), t: 0~1 진행. 돌려주는 건 (CW+2PAD, CH+2PAD) 프레임"""
    a = np.array(card).astype(float); out = a.copy()
    e = m - (t * 1.12 - .06)                     # <0 이면 타서 없어짐
    gone = (e < 0) | ((1 - np.mgrid[0:CH, 0:CW][0] / (CH - 1)) < t * 1.12 - .26); ember = (e >= 0) & (e < .035); char = (e >= .035) & (e < .10)
    out[gone, 3] = 0
    k = np.clip(1 - (e - .035) / .065, 0, 1)[..., None]
    out[..., :3] = np.where(char[..., None] & (a[..., 3:] > 0), out[..., :3] * (1 - k * .85) + CHAR * k * .85, out[..., :3])
    idx = np.clip((e / .035 * len(EMBER)).astype(int), 0, len(EMBER) - 1)
    pal = np.array([c for c in EMBER], float)
    sel = ember & (a[..., 3] > 0)
    out[sel] = pal[idx[sel]]
    fr = Image.new('RGBA', (CW + 2 * PAD, CH + 2 * PAD)); fr.alpha_composite(Image.fromarray(out.astype('uint8')), (PAD, PAD))
    # 불티 · 연기
    d = ImageDraw.Draw(fr); ys, xs = np.nonzero(sel)
    rs = sparks['r']
    if len(xs):
        for _ in range(4):
            i = rs.randrange(len(xs)); sparks['p'].append([xs[i] + PAD, ys[i] + PAD, rs.choice([-1, 0, 0, 1]) * .3, -rs.uniform(1.2, 2.6), rs.randint(5, 10), rs.random() < .35])
    for p in sparks['p']:
        p[0] += p[2]; p[1] += p[3]; p[4] -= 1
        if p[4] <= 0 or not (0 <= p[1] < fr.height and 0 <= p[0] < fr.width): continue
        if p[5]: d.rectangle((p[0], p[1], p[0] + 1, p[1] + 1), fill=(90, 96, 112, 110))   # 연기
        else: d.point((p[0], p[1]), fill=EMBER[1] if p[4] > 5 else EMBER[3] if p[4] > 2 else RED)
    sparks['p'] = [p for p in sparks['p'] if p[4] > 0]
    return fr

# ---- 카드 내용 (모의 화면용) ----
CARDS = [('common', '공통', (0, 16), '벽력', 'Lv 2 → 3', '번개가 근처 원귀 3마리에 떨어진다. 피해 +20%', 3),
         ('exclusive', '전용 · 하영', (1, 0), '검기', '새 신통력', '베기에 검기가 실려 2칸 더 멀리 닿는다', 1),
         ('consumable', '일회용', (2, 0), '주먹밥', '바로 사용', '체력 30% 회복. 칸을 차지하지 않는다', -1)]
def card_face(kind, cat, ic, nm, lv, desc, st):
    s = M.Scr(talisman(kind)); _, tc = BAND[kind]
    s.t(64, 22, cat, GAB, INK2, 'mm'); s.put(M.icon(*ic, 48), 40, 40)
    s.t(64, 104, nm, SH, INK2, 'mm'); s.t(64, 122, lv, GA, RED_D if lv == '새 신통력' else INK2, 'mm')
    M.para(s, 16, 138, desc, 98)
    if st >= 0: s.stars(36, 196, 5, st, 9)
    return s.im

def glow(w, h, c=(242, 194, 48), a0=110):
    g = Image.new('RGBA', (w + 24, h + 24)); d = ImageDraw.Draw(g)
    for i in range(12): d.rounded_rectangle((i, i, w + 23 - i, h + 23 - i), 6, outline=c + (int(a0 * (i + 1) / 12),), width=1)
    return g

def screen(chosen=None, faces=None, frames=None, lift=0):
    s = M.Scr(M.FLOOR, 0.7)
    s.put(M.nine(M.KIT['frame_panel_64'], 280, 40, 12), 180, 8); s.t(320, 28, '레벨 업 · 부적을 고르세요', SH, INK2, 'mm')
    s.plain(16, 14, 96, 24); s.t(64, 26, '리롤권  1 / 1', GAB, INK2, 'mm')
    s.plain(528, 14, 96, 24); s.t(576, 26, '남은 시간 08', GAB, RED, 'mm')
    for k in range(3):
        x = 98 + k * 156; y = 54
        if frames and frames[k] is not None: s.put(frames[k], x - PAD, y - PAD); continue
        if k == chosen: s.put(glow(CW, CH), x - 12, y - 12 - lift); s.put(faces[k], x, y - lift)
        else: s.put(faces[k], x, y)
    if chosen is None:
        for k in range(3):
            x = 98 + k * 156 + 64 - 13; s.plain(x, 290, 26, 26); d = s.d
            d.arc((x + 5, 295, x + 21, 311), 30, 320, fill=INK, width=2); d.polygon([(x + 19, 293), (x + 24, 299), (x + 17, 300)], fill=INK)
        s.btn(220, 324, 200, 28, '이 부적으로', 'primary', SH)
    return s.im

if __name__ == '__main__':
    kinds = ['common', 'exclusive', 'consumable', 'coop']
    tal = {k: talisman(k) for k in kinds}
    for f in os.listdir(OUT):
        if f.startswith('bujeok_') and f.endswith('_128x224.png'): os.remove(f'{OUT}/{f}')
    tal['common'].save(f'{OUT}/bujeok_128x224.png')
    m = burn_map(); Image.fromarray((m * 255).astype('uint8')).save(f'{OUT}/burn_map_128x224.png')
    ramp = Image.new('RGBA', (len(EMBER) * 8 + 16, 4))
    for i, c in enumerate(EMBER[::-1] + [H('#2B1E1A'), H('#2B1E1A')]): ImageDraw.Draw(ramp).rectangle((i * 8, 0, i * 8 + 7, 3), fill=c)
    ramp.save(f'{OUT}/burn_ramp.png')
    # 단독 시트: 공통 부적 12프레임
    N = 12; sp = {'r': random.Random(1), 'p': []}; fw, fh = CW + 2 * PAD, CH + 2 * PAD
    sheet = Image.new('RGBA', (fw * 4, fh * 3))
    for i in range(N): sheet.alpha_composite(burn(tal['common'], m, (i + 1) / N, sp), ((i % 4) * fw, (i // 4) * fh))
    sheet.save(f'{OUT}/bujeok_burn-Sheet_12f_160x256.png')
    # 모의 화면 정지 + 움직이는 연출
    faces = [card_face(*c) for c in CARDS]
    screen(None, faces).save(M.OUT + '/11_upgrade.png')
    gif = []; maps = [burn_map(seed=7), None, burn_map(seed=19)]
    for i in range(8): gif.append(screen(None, faces))
    for i in range(6): gif.append(screen(1, faces, lift=min(4, i)))
    sps = [{'r': random.Random(2), 'p': []}, None, {'r': random.Random(3), 'p': []}]
    T = 22
    for i in range(T + 8):
        t = min(1, (i + 1) / T)
        fr = [burn(faces[0], maps[0], t, sps[0]), None, burn(faces[2], maps[2], t, sps[2])]
        gif.append(screen(1, faces, fr, lift=4))
    big = [g.resize((1280, 720), Image.NEAREST).convert('P', palette=Image.ADAPTIVE, colors=255) for g in gif]
    big[0].save(f'{OUT}/burn_preview.gif', save_all=True, append_images=big[1:], duration=[90] * 14 + [70] * (len(big) - 14), loop=0, disposal=2)
    gif[24].save(f'{OUT}/burn_mid.png')
    print('ok', len(gif))
