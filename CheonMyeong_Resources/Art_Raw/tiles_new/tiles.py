# 새 타일 시안 (32x32, 천명 팔레트, 위에서 본 시점, 이어지는 타일) + 맵 장면
import random, math, os
from PIL import Image, ImageDraw
W = os.path.dirname(os.path.abspath(__file__)); OUT = W + '/tiles_new'; os.makedirs(OUT, exist_ok=True)
H = lambda c: tuple(int(c[i:i + 2], 16) for i in (1, 3, 5)) + (255,)
INK = H('#0B0A10')
E0, E1, E2, E3 = H('#2B1E1A'), H('#46302A'), H('#6A4A3C'), H('#96776D')
S0, S1, S2, S3 = H('#2A2C38'), H('#3F4352'), H('#5A6070'), H('#7D8494')
G0, G1, G2, G3 = H('#12241C'), H('#1F3D2C'), H('#2F5E3E'), H('#4E8A5A')
RED, YEL, CREAM, HJ = H('#B51E2C'), H('#E3B23C'), H('#FAF6E4'), H('#E2D2B0')
T = 32

def dirt(seed):
    r = random.Random(seed); im = Image.new('RGBA', (T, T), E1); p = im.load()
    for _ in range(90): p[r.randrange(T), r.randrange(T)] = r.choice([E2, E2, E0])
    for _ in range(4):  # 작은 자갈 (감싸서 이어짐)
        x, y = r.randrange(T), r.randrange(T)
        for dx, dy, c in [(0, 0, S2), (1, 0, S2), (0, 1, S1), (1, 1, S1), (0, -1, S3)]: p[(x + dx) % T, (y + dy) % T] = c
    return im
def stone(seed, n=7):
    r = random.Random(seed); pts = [(r.uniform(0, T), r.uniform(0, T)) for _ in range(n)]
    base = [r.choice([S2, S2, S1]) for _ in pts]; im = Image.new('RGBA', (T, T)); p = im.load()
    def near(x, y):
        best = []
        for i, (px, py) in enumerate(pts):
            dx = min(abs(x - px), T - abs(x - px)); dy = min(abs(y - py), T - abs(y - py)); best.append((dx * dx + dy * dy, i))
        best.sort(); return best
    cell = [[near(x + .5, y + .5) for x in range(T)] for y in range(T)]
    for y in range(T):
        for x in range(T):
            (d1, i), (d2, _) = cell[y][x][:2]
            if math.sqrt(d2) - math.sqrt(d1) < 1.1: p[x, y] = S0; continue
            c = base[i]
            up = cell[(y - 1) % T][x][0][1]; lf = cell[y][(x - 1) % T][0][1]; dn = cell[(y + 1) % T][x][0][1]; rt = cell[y][(x + 1) % T][0][1]
            if up != i or lf != i: c = S3
            elif dn != i or rt != i: c = S1
            p[x, y] = c
    return im
def grass(seed):
    r = random.Random(seed); im = Image.new('RGBA', (T, T), G1); p = im.load()
    for _ in range(70): p[r.randrange(T), r.randrange(T)] = G0
    for _ in range(26):
        x, y = r.randrange(T), r.randrange(T)
        p[x, y] = G2; p[x, (y - 1) % T] = G3 if r.random() < .5 else G2
    return im
def path(seed):
    im = dirt(seed); d = ImageDraw.Draw(im)
    for (x, y, w, h) in [(6, 3, 18, 10), (9, 18, 16, 10)]:
        d.ellipse((x, y + 1, x + w, y + h + 1), fill=S0); d.ellipse((x, y, x + w, y + h), fill=S2); d.arc((x + 1, y + 1, x + w - 1, y + h - 1), 180, 290, fill=S3)
    return im
def wall_top(seed):  # 흙돌담 위 (기와)
    im = Image.new('RGBA', (T, T), S0); d = ImageDraw.Draw(im)
    for x in range(0, T, 8):
        d.rectangle((x, 0, x + 7, 21), fill=S1); d.line((x + 7, 0, x + 7, 21), fill=S0); d.line((x + 1, 0, x + 1, 20), fill=S2)
        d.ellipse((x, 18, x + 7, 25), fill=S2, outline=S0)
    d.rectangle((0, 26, T - 1, T - 1), fill=INK); return im
def wall_body(seed):
    r = random.Random(seed); im = Image.new('RGBA', (T, T), E2); d = ImageDraw.Draw(im); p = im.load()
    for _ in range(60): p[r.randrange(T), r.randrange(T)] = E3 if r.random() < .5 else E1
    for _ in range(5):
        x, y = r.randrange(2, T - 8), r.randrange(2, T - 7); d.ellipse((x, y, x + 7, y + 5), fill=S2, outline=S0)
    d.line((0, T - 1, T - 1, T - 1), fill=E0); return im
def shadow(w, h, a=110):
    im = Image.new('RGBA', (w, h)); ImageDraw.Draw(im).ellipse((0, 0, w - 1, h - 1), fill=(11, 10, 16, a)); return im
def tree():  # 당산나무 96x112
    im = Image.new('RGBA', (96, 112)); d = ImageDraw.Draw(im); r = random.Random(5)
    d.rectangle((40, 60, 56, 108), fill=E1, outline=INK); d.line((44, 64, 44, 104), fill=E2); d.polygon([(40, 108), (30, 111), (66, 111), (56, 108)], fill=E1, outline=INK)
    for cx, cy, rr in [(48, 34, 30), (24, 46, 20), (72, 46, 20), (36, 20, 18), (62, 22, 18)]:
        d.ellipse((cx - rr, cy - rr, cx + rr, cy + rr), fill=G1, outline=INK)
    for _ in range(160):
        x, y = r.randrange(6, 90), r.randrange(4, 66)
        if im.getpixel((x, y))[3] and im.getpixel((x, y))[:3] == G1[:3]: im.putpixel((x, y), r.choice([G2, G2, G3, G0]))
    d.line((38, 76, 58, 76), fill=CREAM, width=2)  # 금줄
    for x in (42, 48, 54): d.rectangle((x, 78, x + 1, 83), fill=CREAM if x != 48 else RED)
    return im
def stall():  # 빈 좌판 64x44
    im = Image.new('RGBA', (64, 44)); d = ImageDraw.Draw(im)
    d.rectangle((2, 14, 61, 32), fill=E2, outline=INK); d.line((3, 15, 60, 15), fill=E3)
    for x in range(6, 60, 8): d.line((x, 16, x, 31), fill=E1)
    for x in (4, 57): d.rectangle((x, 32, x + 3, 42), fill=E1, outline=INK)
    d.polygon([(0, 2), (63, 2), (60, 12), (3, 12)], fill=H('#3F4352'), outline=INK)  # 천막 지붕
    for x in range(4, 60, 10): d.line((x, 3, x + 3, 11), fill=S2)
    return im
def lantern():
    im = Image.new('RGBA', (12, 20)); d = ImageDraw.Draw(im)
    d.line((5, 0, 5, 3), fill=INK); d.rectangle((1, 4, 10, 16), fill=RED, outline=INK); d.line((2, 7, 9, 7), fill=YEL); d.line((2, 13, 9, 13), fill=YEL); d.rectangle((4, 17, 7, 19), fill=INK)
    return im
def glow(rr):
    im = Image.new('RGBA', (rr * 2, rr * 2)); d = ImageDraw.Draw(im)
    for k in range(rr, 0, -3): d.ellipse((rr - k, rr - k, rr + k, rr + k), fill=(227, 178, 60, int(70 * (1 - k / rr)) + 10))
    return im
def rock():
    im = Image.new('RGBA', (20, 14)); d = ImageDraw.Draw(im); d.ellipse((0, 2, 19, 13), fill=S1, outline=INK); d.ellipse((3, 3, 12, 8), fill=S2); im.putpixel((5, 4), S3); return im
def bamboo():
    im = Image.new('RGBA', (32, 56)); d = ImageDraw.Draw(im)
    for x, h in [(3, 50), (10, 54), (17, 46), (24, 52)]:
        d.rectangle((x, 56 - h, x + 4, 55), fill=G2, outline=G0); d.line((x + 1, 56 - h + 1, x + 1, 54), fill=G3)
        for y in range(56 - h + 6, 55, 9): d.line((x, y, x + 2, y), fill=G0)
        d.polygon([(x + 1, 56 - h), (x - 5, 56 - h + 6), (x + 1, 56 - h + 3)], fill=G3)
    return im
def tuft():
    im = Image.new('RGBA', (10, 7)); d = ImageDraw.Draw(im)
    for x, h in [(1, 4), (3, 6), (5, 5), (7, 6), (8, 3)]: d.line((x, 6, x, 6 - h), fill=G2 if x % 2 else G3)
    return im
def jar():
    im = Image.new('RGBA', (14, 14)); d = ImageDraw.Draw(im); d.ellipse((0, 2, 13, 13), fill=E0, outline=INK); d.rectangle((4, 0, 9, 3), fill=E1, outline=INK); d.arc((2, 4, 10, 10), 180, 260, fill=E2); return im

TILES = {'wall_body_b': wall_body(21), 'wall_body_c': wall_body(22), 'dirt_a': dirt(1), 'dirt_b': dirt(2), 'dirt_c': dirt(3), 'stone_a': stone(4), 'stone_b': stone(5), 'grass_a': grass(6), 'grass_b': grass(7), 'path': path(8), 'wall_top': wall_top(9), 'wall_body': wall_body(10)}
OBJ = {'tuft': tuft(), 'tree': tree(), 'stall': stall(), 'lantern': lantern(), 'rock': rock(), 'bamboo': bamboo(), 'jar': jar()}
for k, v in {**TILES, **OBJ}.items(): v.save(OUT + f'/{k}.png')

def scene(w=640, h=360, seed=11, ha=None):
    r = random.Random(seed); im = Image.new('RGBA', (w, h))
    cols, rows = w // T + 1, h // T + 1
    for ty in range(rows):
        for tx in range(cols):
            x, y = tx * T, ty * T; cx = w // 2
            if ty == 0: k = 'wall_top'
            elif ty == 1: k = r.choice(['wall_body', 'wall_body_b', 'wall_body_c'])
            elif 6 <= tx <= cols - 8 and 4 <= ty <= 8: k = r.choice(['stone_a', 'stone_b'])
            elif abs(x + 16 - cx) < 20 and ty > 8: k = 'path'
            elif (tx <= 2 or tx >= cols - 3) and ty >= rows - 3: k = r.choice(['grass_a', 'grass_b'])
            else: k = r.choice(['dirt_a', 'dirt_b', 'dirt_c'])
            im.paste(TILES[k], (x, y))
    # 물체 (그림자 → 물체 → 빛)
    def put(o, x, y, sh=True):
        if sh: im.alpha_composite(shadow(int(o.width * 0.9), max(6, o.height // 6)), (x + o.width // 20, y + o.height - max(6, o.height // 6) // 2 - 2))
        im.alpha_composite(o, (x, y))
    # 박석 마당 테두리
    d = ImageDraw.Draw(im); x0, y0 = 6 * T, 4 * T; x1, y1 = (cols - 7) * T, 9 * T
    d.rectangle((x0 - 2, y0 - 2, x1 + 1, y1 + 1), outline=S0, width=2); d.line((x0 - 2, y1 + 2, x1 + 1, y1 + 2), fill=E0)
    for _ in range(70):
        tx, ty = r.randrange(0, w - 10), r.randrange(2 * T, h - 8)
        if not (x0 - 8 < tx < x1 and y0 - 8 < ty < y1): im.alpha_composite(OBJ['tuft'], (tx, ty))
    put(OBJ['tree'], 40, 40)
    for (x, y) in [(470, 92), (480, 196)]:
        put(OBJ['stall'], x, y)
        for jx in (x - 14, x + 66): put(OBJ['jar'], jx, y + 30)
    for (x, y) in [(150, 60), (460, 70), (560, 180), (190, 240)]:
        im.alpha_composite(glow(36), (x - 30, y - 26)); put(OBJ['lantern'], x, y, False)
    for (x, y) in [(120, 200), (330, 100), (560, 300), (90, 320), (420, 280)]: put(OBJ['rock'], x, y)
    for (x, y) in [(8, 290), (600, 280), (36, 300)]: put(OBJ['bamboo'], x, y)
    if ha is not None: put(ha, w // 2 - ha.width // 2, int(h * 0.55))
    night = Image.new('RGBA', (w, h), (21, 33, 67, 70)); im.alpha_composite(night)
    return im

if __name__ == '__main__':
    sheet = Image.new('RGBA', (T * 10 + 9 * 4, T), (0, 0, 0, 0))
    for i, k in enumerate(TILES): sheet.paste(TILES[k], (i * (T + 4), 0))
    big = Image.new('RGBA', (sheet.width, 140), (21, 33, 67, 255)); big.alpha_composite(sheet, (0, 0))
    x = 0
    for k in OBJ: big.alpha_composite(OBJ[k], (x, 140 - OBJ[k].height - 2)); x += OBJ[k].width + 8
    big.resize((big.width * 3, big.height * 3), Image.NEAREST).save(W + '/tiles_sheet.png')
    scene().resize((1920, 1080), Image.NEAREST).save(W + '/map_scene.png'); print('ok')
