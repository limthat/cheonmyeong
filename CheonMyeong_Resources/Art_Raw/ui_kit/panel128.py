# 창 · 패널 128x128 — 더 밝은 한지 + 오방색 띠 (단청 무늬 대신)
import random, sys
from PIL import Image, ImageDraw
sys.path.insert(0, '.')
H = lambda c: tuple(int(c[i:i + 2], 16) for i in (1, 3, 5)) + (255,)
INK = H('#0B0A10'); BLUE = H('#2A5CAA'); RED = H('#D7263D'); YEL = H('#F2C230'); WHITE = H('#FFFDF5')
HJ = H('#F1E6CC'); HJ_L = H('#FBF4E4'); HJ_D = H('#DCCBA5')
def hanji(im, box, seed=1):
    x0, y0, x1, y1 = box; r = random.Random(seed); ImageDraw.Draw(im).rectangle(box, fill=HJ)
    for _ in range(int((x1 - x0) * (y1 - y0) * 0.05)): im.putpixel((r.randint(x0, x1), r.randint(y0, y1)), HJ_D if r.random() < .5 else HJ_L)
    for _ in range(int((x1 - x0) * (y1 - y0) * 0.003)):
        x, y = r.randint(x0, x1 - 4), r.randint(y0, y1)
        for k in range(r.randint(2, 5)): im.putpixel((min(x1, x + k), y), HJ_D)
def panel(S=128):
    im = Image.new('RGBA', (S, S)); hanji(im, (0, 0, S - 1, S - 1)); d = ImageDraw.Draw(im)
    # 바깥→안: 흑1 · 적3 · 백1 · 청3 · 황2 · 흑1  (두께 11)
    i = 0
    for c, w in [(INK, 1), (RED, 3), (WHITE, 1), (BLUE, 3), (YEL, 2), (INK, 1)]:
        for _ in range(w): d.rectangle((i, i, S - 1 - i, S - 1 - i), outline=c); i += 1
    # 청 띠 위 흰 점 (리듬)
    for x in range(8, S - 8, 6): im.putpixel((x, 6), WHITE); im.putpixel((x, S - 7), WHITE)
    for y in range(8, S - 8, 6): im.putpixel((6, y), WHITE); im.putpixel((S - 7, y), WHITE)
    # 모서리: 오방 무늬 16x16 (가운데 황, 동 청 · 서 백 · 남 적 · 북 흑)
    for cx, cy in [(0, 0), (S - 16, 0), (0, S - 16), (S - 16, S - 16)]:
        d.rectangle((cx, cy, cx + 15, cy + 15), fill=WHITE, outline=INK)
        d.polygon([(cx + 8, cy + 1), (cx + 14, cy + 7), (cx + 8, cy + 7)], fill=BLUE); d.polygon([(cx + 14, cy + 8), (cx + 8, cy + 14), (cx + 8, cy + 8)], fill=RED)
        d.polygon([(cx + 7, cy + 14), (cx + 1, cy + 8), (cx + 7, cy + 8)], fill=WHITE); d.polygon([(cx + 1, cy + 7), (cx + 7, cy + 1), (cx + 7, cy + 7)], fill=INK)
        d.polygon([(cx + 8, cy + 1), (cx + 14, cy + 8), (cx + 8, cy + 14), (cx + 1, cy + 8)], outline=INK)
        d.rectangle((cx + 6, cy + 6, cx + 9, cy + 9), fill=YEL, outline=INK)
    return im
if __name__ == '__main__':
    from variants import nine, GA, SH
    P = panel(); P.save('frame_panel_128.png')
    old = Image.open('kit/frame_panel_64.png').convert('RGBA')
    bg = Image.open('/mnt/user-data/uploads/c/CM/cheonmyeong/Assets/Art/Lobby/background-Sheet.png').convert('RGBA').resize((640, 360), Image.BOX)
    bg.alpha_composite(Image.new('RGBA', (640, 360), (11, 16, 37, 185)))
    d = ImageDraw.Draw(bg); d.fontmode = '1'
    for k, (im, b, lab) in enumerate([(old, 9, '지금 (B)'), (P, 16, '수정안 · 밝은 한지 + 오방색')]):
        x = 16 + k * 316; bg.alpha_composite(nine(im, 292, 250, b), (x, 70))
        d.text((x + 146, 50), lab, font=SH, fill=(250, 246, 228), anchor='mm')
        tc = (43, 30, 26)
        d.text((x + 146, 104), '설정', font=SH, fill=tc, anchor='mm')
        for j, (t, v) in enumerate([('전체 음량', .8), ('배경음', .6), ('효과음', .9)]):
            y = 130 + j * 40; d.text((x + 30, y), t, font=GA, fill=tc)
            d.rectangle((x + 30, y + 16, x + 262, y + 28), fill=(11, 10, 16)); d.rectangle((x + 32, y + 18, x + 32 + int(228 * v), y + 26), fill=(42, 92, 170) if k else (47, 111, 106))
        d.text((x + 146, 270), '오방: 청 · 적 · 황 · 백 · 흑', font=GA, fill=tc, anchor='mm')
    bg.resize((1920, 1080), Image.NEAREST).save('panel_compare.png'); P.resize((384, 384), Image.NEAREST).save('frame_panel_128_x3.png'); print('ok')
