# 맵 썸네일 (256x128) · 달밤의 폐장터 — 한눈에 알아보게 구성한 그림
import random, os, sys
from PIL import Image, ImageDraw
W = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, W)
import tiles as TL
H = TL.H
U = '/mnt/user-data/uploads/c/CM/cheonmyeong'
def make():
    w, h = 256, 128; im = Image.new('RGBA', (w, h)); d = ImageDraw.Draw(im); r = random.Random(4)
    sky = [H('#0B1025'), H('#152143'), H('#284987')]
    for y in range(64):  # 하늘: 3단 + 디더
        t = y / 64 * 2; a, b = int(t), min(2, int(t) + 1); f = t - int(t)
        for x in range(w): im.putpixel((x, y), sky[b] if ((x + y) % 4 == 0 and f > 0.5) or f > 0.8 else sky[a])
    for _ in range(26): im.putpixel((r.randrange(w), r.randrange(40)), H('#FAF6E4') if r.random() < .4 else H('#576775'))
    d.ellipse((196, 10, 220, 34), fill=H('#FAF6E4')); d.ellipse((204, 16, 210, 21), fill=H('#E2D2B0')); d.ellipse((210, 24, 214, 27), fill=H('#E2D2B0'))
    # 먼 산 · 지붕 실루엣
    for x in range(w):
        y0 = 50 + int(6 * abs(((x / 40) % 2) - 1)); d.line((x, y0, x, 64), fill=H('#152143'))
    for (x0, x1, yy) in [(10, 70, 52), (150, 230, 50)]:
        d.polygon([(x0 - 6, yy + 6), (x0 + 4, yy), (x1 - 4, yy), (x1 + 6, yy + 6)], fill=H('#0B1025'))
        d.rectangle((x0, yy + 6, x1, 64), fill=H('#0B1025'))
    # 흙돌담 (앞)
    for x in range(0, w, 32):
        im.paste(TL.TILES['wall_top'].crop((0, 0, 32, 26)), (x, 58)); im.paste(TL.TILES['wall_body'].crop((0, 0, 32, 16)), (x, 84 - 0))
    # 땅
    for y in range(96, h, 32):
        for x in range(0, w, 32): im.paste(TL.TILES[r.choice(['dirt_a', 'dirt_b', 'dirt_c'])], (x, y))
    d.polygon([(112, 96), (144, 96), (164, 128), (92, 128)], fill=H('#6A4A3C'))  # 가운데 길
    for (x, y) in [(118, 102), (130, 110), (114, 118)]: d.ellipse((x, y, x + 12, y + 6), fill=H('#5A6070'), outline=H('#2A2C38'))
    # 당산나무 (가운데, 담 위로 솟음)
    tree = TL.OBJ['tree']; im.alpha_composite(TL.shadow(80, 10), (88, 112)); im.alpha_composite(tree, (80, 8))
    # 좌판 · 초롱 · 장독
    for (x, y) in [(8, 84), (184, 86)]:
        im.alpha_composite(TL.shadow(58, 7), (x + 3, y + 38)); im.alpha_composite(TL.OBJ['stall'], (x, y))
    for (x, y) in [(70, 70), (176, 70), (238, 82)]:
        im.alpha_composite(TL.glow(30), (x - 24, y - 20)); im.alpha_composite(TL.OBJ['lantern'], (x, y))
    for (x, y) in [(66, 112), (178, 116)]: im.alpha_composite(TL.OBJ['jar'], (x, y))
    # 원귀 빛 (푸른 넋)
    soul = Image.open(U + '/Assets/Art/Goods/coin_xp-Sheet.png').convert('RGBA').crop((0, 0, 32, 32)).resize((10, 10), Image.NEAREST)
    for (x, y) in [(48, 66), (100, 40), (150, 58), (206, 60), (132, 30)]:
        im.alpha_composite(TL.glow(10), (x - 5, y - 5)); im.alpha_composite(soul, (x, y))
    im.alpha_composite(Image.new('RGBA', (w, h), (21, 33, 67, 40)))
    # 테두리 먹선
    d = ImageDraw.Draw(im); d.rectangle((0, 0, w - 1, h - 1), outline=H('#0B0A10'))
    return im
def locked(im):
    from PIL import ImageEnhance
    g = ImageEnhance.Color(ImageEnhance.Brightness(im).enhance(0.35)).enhance(0.2)
    d = ImageDraw.Draw(g); c = (250, 246, 228, 255); x, y = 118, 52
    d.arc((x + 4, y, x + 16, y + 12), 180, 360, fill=c, width=3); d.rectangle((x, y + 8, x + 20, y + 24), fill=c); d.rectangle((x + 9, y + 13, x + 11, y + 19), fill=(11, 10, 16, 255))
    return g
if __name__ == '__main__':
    t = make(); t.save(W + '/thumb_dalbam.png'); locked(t).save(W + '/thumb_locked.png')
    t.resize((768, 384), Image.NEAREST).save(W + '/thumb_dalbam_x3.png'); print('ok')
