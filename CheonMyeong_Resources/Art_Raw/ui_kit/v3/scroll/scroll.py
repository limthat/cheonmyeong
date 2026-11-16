# 한지 두루마리 (가로 256x64 / 세로 128x256) + 펼쳐지는 연출
import os, random
from PIL import Image, ImageDraw
import mock as M
from mock import H, INK, INK2, CREAM, RED, BLUE, YEL, SH, SH32, GA, GAB

W = os.path.dirname(os.path.abspath(__file__)); OUT = W + '/scroll'; os.makedirs(OUT, exist_ok=True)
HJ, HJ_D, HJ_DD, WHITE = H('#F1E6CC'), H('#DCCBA5'), H('#C9B48C'), H('#FFFDF5')
WOOD = [H('#2B1E1A'), H('#46302A'), H('#6A4A3C'), H('#96776D'), H('#6A4A3C'), H('#46302A')]
GOLD, GOLD_D = H('#F2C230'), H('#B8861E')
B = 24  # 축 + 말린 부분 (9-slice 고정 테두리)

def hscroll(w=256, h=64, seed=4):
    """축이 세로로 선 가로 두루마리"""
    r = random.Random(seed); im = Image.new('RGBA', (w, h)); d = ImageDraw.Draw(im); px = im.load()
    # 종이
    d.rectangle((16, 8, w - 17, h - 9), fill=HJ)
    for _ in range(w * h // 18):
        x, y = r.randrange(17, w - 17), r.randrange(9, h - 9); px[x, y] = HJ_D if r.random() < .7 else WHITE
    d.line((16, 8, w - 17, 8), fill=INK); d.line((16, h - 9, w - 17, h - 9), fill=INK)
    d.line((16, 9, w - 17, 9), fill=WHITE); d.line((16, h - 10, w - 17, h - 10), fill=HJ_DD)
    # 오방색 안쪽 선 (적 + 청)
    d.line((16, 12, w - 17, 12), fill=RED); d.line((16, h - 13, w - 17, h - 13), fill=RED)
    d.line((16, 14, w - 17, 14), fill=BLUE); d.line((16, h - 15, w - 17, h - 15), fill=BLUE)
    for side in (0, 1):
        def X(x): return x if side == 0 else w - 1 - x
        # 말린 종이 (원통 명암)
        roll = [HJ_DD, HJ_D, HJ, WHITE, WHITE, HJ, HJ, HJ_D, HJ_D, HJ_DD, HJ_DD, INK2]
        for i, c in enumerate(roll): d.line((X(12 + i), 6, X(12 + i), h - 7), fill=c)
        d.line((X(12), 5, X(23), 5), fill=INK); d.line((X(12), h - 6, X(23), h - 6), fill=INK)
        d.line((X(23), 5, X(23), h - 6), fill=INK) if side == 0 else d.line((X(23), 5, X(23), h - 6), fill=INK)
        # 나무 축
        for i, c in enumerate([INK] + WOOD + [INK]): d.line((X(4 + i), 3, X(4 + i), h - 4), fill=c)
        # 축 끝 (황색 꼭지 + 적색 고리)
        for (y0, y1) in [(0, 4), (h - 5, h - 1)]:
            xa, xb = sorted((X(3), X(12))); d.rectangle((xa, y0, xb, y1), fill=GOLD, outline=INK)
            d.line((xa + 1, y1 - 1 if y0 == 0 else y0 + 1, xb - 1, y1 - 1 if y0 == 0 else y0 + 1), fill=GOLD_D)
            d.point((xa + 2, y0 + 1), fill=WHITE)
        ya, yb = 5, h - 6
        xa, xb = sorted((X(4), X(11))); d.line((xa, ya, xb, ya), fill=RED); d.line((xa, yb, xb, yb), fill=RED)
    return im

def vscroll(w=128, h=256): return hscroll(h, w, seed=9).rotate(-90, expand=True)

def scr_face(base, w, h, vertical, draw):
    """9-slice로 늘린 두루마리 + 내용"""
    im = M.nine(base, w, h, B) if not vertical else nine_v(base, w, h)
    s = M.Scr(im); draw(s); return s.im
def nine_v(base, w, h):  # 세로형: 위아래 B, 좌우 8
    rot = base.rotate(90, expand=True)              # 가로형으로 눕혀서
    return M.nine(rot, h, w, B).rotate(-90, expand=True)

def opened(full, p, vertical):
    """p: 0~1. 가로는 가운데서 양옆으로, 세로는 위에서 아래로"""
    w, h = full.size
    if not vertical:
        pw = int(round((w - 2 * B) * p)); cx = w // 2; out = Image.new('RGBA', (2 * B + pw, h))
        out.alpha_composite(full.crop((0, 0, B, h)), (0, 0))
        if pw: out.alpha_composite(full.crop((cx - pw // 2, 0, cx - pw // 2 + pw, h)), (B, 0))
        out.alpha_composite(full.crop((w - B, 0, w, h)), (B + pw, 0))
        return out, (w - out.width) // 2, 0
    ph = int(round((h - 2 * B) * p)); out = Image.new('RGBA', (w, 2 * B + ph))
    out.alpha_composite(full.crop((0, 0, w, B)), (0, 0))
    if ph: out.alpha_composite(full.crop((0, B, w, B + ph)), (0, B))
    out.alpha_composite(full.crop((0, h - B, w, h)), (0, B + ph))
    return out, 0, 0
ease = lambda t: t * t * (3 - 2 * t)

if __name__ == '__main__':
    HS, VS = hscroll(), vscroll()
    HS.save(OUT + '/scroll_h_256x64.png'); VS.save(OUT + '/scroll_v_128x256.png')
    # 내용 그리기
    def title(s): s.t(130, 23, '이야기 보관함', SH, INK2, 'mm')
    def story(s):
        s.t(100, 46, '1화 · 억쇠 등장', SH, INK2, 'mm'); s.d.line((40, 58, 160, 58), fill=RED)
        M.para(s, 26, 70, '달밤마다 문 닫은 장터에 다시 불이 켜진다. 장이 서던 자리마다 억울하게 죽은 넋이 모여들고, 그 한가운데 억쇠가 선다.', 148, GA, INK2, 15)
        s.put(M.portrait(0, 32), 54, 182); s.put(M.portrait(1, 32), 114, 182)
        s.t(70, 222, '하영', GA, INK2, 'mm'); s.t(130, 222, '한주원', GA, INK2, 'mm')
        s.btn(50, 238, 100, 22, '다시 보기', 'primary')
    def banner(s): s.t(120, 25, '봉 인 성 공', SH, RED, 'mm')
    def talk(s):
        s.t(40, 20, '억쇠', GAB, RED); s.t(40, 40, '"한 맺힌 것들이 이렇게나 많은데…"', GA, INK2)
    F_title = scr_face(HS, 260, 46, False, title)
    F_story = scr_face(VS, 200, 290, True, story)
    F_ban = scr_face(HS, 240, 50, False, banner)
    F_talk = scr_face(HS, 300, 70, False, talk)
    pos = {'title': (190, 6), 'story': (60, 60), 'ban': (340, 92), 'talk': (300, 236)}
    def frame(pt, ps, pb, pk):
        s = M.Scr(M.BG, 0.55)
        for key, F, p, v in [('title', F_title, pt, False), ('story', F_story, ps, True), ('ban', F_ban, pb, False), ('talk', F_talk, pk, False)]:
            if p <= 0: continue
            im, dx, dy = opened(F, ease(min(1, p)), v); x, y = pos[key]; s.put(im, x + dx, y + dy)
        s.t(470, 172, '가로 두루마리', GA, CREAM, 'mm'); s.t(160, 354, '세로 두루마리', GA, CREAM, 'mm')
        return s.im
    still = frame(1, 1, 1, 1); still.resize((1920, 1080), Image.NEAREST).save(OUT + '/scroll_preview.png')
    gif = []
    for i in range(46):
        gif.append(frame(i / 10, (i - 8) / 16, (i - 20) / 10, (i - 28) / 12))
    for _ in range(14): gif.append(gif[-1])
    gif = [g.convert('RGB') for g in gif]
    gif[0].save(OUT + '/scroll_open_640.gif', save_all=True, append_images=gif[1:], duration=60, loop=0)
    # 부품 시트 (x3)
    sh = Image.new('RGBA', (420, 270), H('#152143')); sh.alpha_composite(HS, (10, 10)); sh.alpha_composite(VS, (282, 6))
    for k, p in enumerate([.0, .35, .7]):
        im, dx, _ = opened(HS, p, False); sh.alpha_composite(im, (10 + dx, 90 + k * 60))
    sh.resize((1260, 810), Image.NEAREST).save(OUT + '/scroll_parts_x3.png')
    print('ok', len(gif))
