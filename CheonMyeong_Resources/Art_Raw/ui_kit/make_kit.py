# 천명 UI 기준 프레임 (오방색 + 한지). python3 make_kit.py
import random, os
from PIL import Image, ImageDraw, ImageFont
D=os.path.dirname(os.path.abspath(__file__))
H=lambda c:tuple(int(c[i:i+2],16) for i in (1,3,5))+(255,)
INK=H('#0B0A10'); INK2=H('#2B1E1A')
HJ_L=H('#EFE3C8'); HJ=H('#E2D2B0'); HJ_D=H('#C9B48C')
RED=H('#B51E2C'); RED_D=H('#781526'); TEAL=H('#2F6F6A'); BLUE=H('#284987')
YEL=H('#E3B23C'); WHITE=H('#FAF6E4'); NAVY=H('#152143'); NAVY_D=H('#0B1025')
def hanji(im,box,seed=1):
    x0,y0,x1,y1=box; r=random.Random(seed); d=ImageDraw.Draw(im)
    d.rectangle(box,fill=HJ)
    for _ in range(int((x1-x0)*(y1-y0)*0.06)):
        x=r.randint(x0,x1); y=r.randint(y0,y1); im.putpixel((x,y),HJ_D if r.random()<0.55 else HJ_L)
    for _ in range(int((x1-x0)*(y1-y0)*0.004)):  # 섬유 짧은 선
        x=r.randint(x0,x1-3); y=r.randint(y0,y1); L=r.randint(2,4)
        for k in range(L):
            if x+k<=x1: im.putpixel((x+k,y),HJ_D)
def band_frame(size,seed=1,band=True,corner=True):
    W,Hh=size; im=Image.new('RGBA',size,(0,0,0,0)); d=ImageDraw.Draw(im)
    hanji(im,(0,0,W-1,Hh-1),seed)
    if band:
        # 바깥에서 안으로: 먹1 · 적1 · 청록2(흰 점) · 황1 · 먹1
        layers=[INK,RED,TEAL,TEAL,YEL,INK]
        for i,c in enumerate(layers): d.rectangle((i,i,W-1-i,Hh-1-i),outline=c)
        for x in range(4,W-4,4):
            im.putpixel((x,2),WHITE); im.putpixel((x,Hh-3),WHITE)
        for y in range(4,Hh-4,4):
            im.putpixel((2,y),WHITE); im.putpixel((W-3,y),WHITE)
        if corner:
            for cx,cy in [(0,0),(W-9,0),(0,Hh-9),(W-9,Hh-9)]:
                d.rectangle((cx,cy,cx+8,cy+8),fill=TEAL,outline=INK)
                # 마름모(적) + 가운데 흰 점 + 황 테
                for (px,py) in [(4,2),(3,3),(4,3),(5,3),(2,4),(3,4),(5,4),(6,4),(3,5),(4,5),(5,5),(4,6)]: im.putpixel((cx+px,cy+py),RED)
                im.putpixel((cx+4,cy+4),WHITE)
                for k in range(1,8): im.putpixel((cx+k,cy+1),YEL); im.putpixel((cx+k,cy+7),YEL)
    else:
        d.rectangle((0,0,W-1,Hh-1),outline=INK)
    return im
def button(size,kind='basic',seed=3):
    W,Hh=size; im=Image.new('RGBA',size,(0,0,0,0)); d=ImageDraw.Draw(im)
    if kind=='primary':
        d.rectangle((0,0,W-1,Hh-1),fill=RED); d.rectangle((1,Hh-3,W-2,Hh-2),fill=RED_D)
        d.line((2,1,W-3,1),fill=H('#D9505A') if False else YEL)
    else:
        hanji(im,(0,0,W-1,Hh-1),seed)
        tone={'hover':None,'press':HJ_D,'lock':H('#7D8494')}.get(kind)
        if tone: d.rectangle((1,1,W-2,Hh-2),fill=tone)
        d.rectangle((1,Hh-3,W-2,Hh-2),fill=HJ_D if kind!='press' else H('#96776D'))
    d.rectangle((0,0,W-1,Hh-1),outline=YEL if kind=='hover' else INK)
    if kind=='hover': d.rectangle((1,1,W-2,Hh-2),outline=INK)
    for p in [(0,0),(W-1,0),(0,Hh-1),(W-1,Hh-1)]: im.putpixel(p,(0,0,0,0))
    return im
def card(color,seed=5):
    im=band_frame((64,64),seed,band=True,corner=False); d=ImageDraw.Draw(im)
    d.rectangle((6,6,57,12),fill=color); d.line((6,13,57,13),fill=INK)
    return im
def tab(selected):
    im=button((128,32),'basic',7); d=ImageDraw.Draw(im)
    if selected: d.rectangle((1,26,126,29),fill=TEAL); d.line((1,25,126,25),fill=INK)
    return im
def bar_frame():
    im=Image.new('RGBA',(64,16),(0,0,0,0)); d=ImageDraw.Draw(im)
    hanji(im,(0,0,63,15),9); d.rectangle((0,0,63,15),outline=INK)
    d.rectangle((3,3,60,12),fill=H('#6A4A3C')); d.rectangle((3,3,60,12),outline=INK)
    return im
def fill(c,cd):
    im=Image.new('RGBA',(16,8),c); d=ImageDraw.Draw(im); d.line((0,0,15,0),fill=WHITE if c==TEAL else (H('#3F4352') if c==INK else HJ_L)); d.line((0,7,15,7),fill=cd); return im
out=D
parts={'frame_panel_64.png':band_frame((64,64)),'button_basic_128x32.png':button((128,32)),'button_hover_128x32.png':button((128,32),'hover'),
 'button_press_128x32.png':button((128,32),'press'),'button_lock_128x32.png':button((128,32),'lock'),'button_primary_128x32.png':button((128,32),'primary'),
 'tab_on_128x32.png':tab(True),'tab_off_128x32.png':tab(False),'bar_frame_64x16.png':bar_frame(),
 'card_common_64.png':card(YEL),'card_exclusive_64.png':card(RED),'card_consumable_64.png':card(BLUE),'card_coop_64.png':card(WHITE),
 'fill_hp.png':fill(RED,RED_D),'fill_exp.png':fill(TEAL,BLUE),'fill_boss.png':fill(RED_D,INK2),'fill_grudge.png':fill(INK,INK)}
for n,im in parts.items(): im.save(os.path.join(out,n))
print('parts',len(parts))
