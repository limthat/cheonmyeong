from PIL import Image, ImageDraw, ImageFont
import os
D=os.path.dirname(os.path.abspath(__file__)); G=os.path.join(D,'../../글꼴')
L=lambda n:Image.open(os.path.join(D,n)).convert('RGBA')
def nine(im,w,h,b):
    W,H=im.size; out=Image.new('RGBA',(w,h),(0,0,0,0))
    xs=[(0,b,0,b),(b,W-b,b,w-b),(W-b,W,w-b,w)]; ys=[(0,b,0,b),(b,H-b,b,h-b),(H-b,H,h-b,h)]
    for sx0,sx1,dx0,dx1 in xs:
        for sy0,sy1,dy0,dy1 in ys:
            if dx1>dx0 and dy1>dy0:
                tile=im.crop((sx0,sy0,sx1,sy1)); tw,th=tile.size
                if (sx0,sx1)==(b,W-b) and (sy0,sy1)==(b,H-b):
                    for yy in range(dy0,dy1,th):
                        for xx in range(dx0,dx1,tw): out.paste(tile.crop((0,0,min(tw,dx1-xx),min(th,dy1-yy))),(xx,yy))
                elif (sx0,sx1)==(b,W-b):
                    for xx in range(dx0,dx1,tw): out.paste(tile.crop((0,0,min(tw,dx1-xx),dy1-dy0)) if tile.size[1]==dy1-dy0 else tile.resize((tw,dy1-dy0),Image.NEAREST).crop((0,0,min(tw,dx1-xx),dy1-dy0)),(xx,dy0))
                elif (sy0,sy1)==(b,H-b):
                    for yy in range(dy0,dy1,th): out.paste(tile.resize((dx1-dx0,th),Image.NEAREST).crop((0,0,dx1-dx0,min(th,dy1-yy))),(dx0,yy))
                else:
                    out.paste(tile.resize((dx1-dx0,dy1-dy0),Image.NEAREST),(dx0,dy0))
    return out
SH=ImageFont.truetype(os.path.join(G,'Suhgung16.ttf'),16); GA=ImageFont.truetype(os.path.join(G,'Galmuri11.ttf'),12)
INK=(11,10,16,255); CREAM=(250,246,228,255); INK2=(43,30,26,255)
S=Image.new('RGBA',(640,360),(21,33,67,255)); d=ImageDraw.Draw(S); d.fontmode='1'
def put(im,x,y): S.alpha_composite(im,(x,y))
def text(x,y,s,f,c,anchor='la'): d.text((x,y),s,font=f,fill=c,anchor=anchor)
text(16,10,'천명 UI 기준 프레임 · 오방색 + 한지',SH,CREAM)
# 큰 패널
put(nine(L('frame_panel_64.png'),300,190,9),16,36)
text(166,52,'설정',SH,INK2,'ma')
put(nine(L('tab_on_128x32.png'),80,22,4),36,72); text(76,83,'오디오',GA,INK2,'mm')
put(nine(L('tab_off_128x32.png'),80,22,4),36,98); text(76,109,'그래픽',GA,INK2,'mm')
put(nine(L('tab_off_128x32.png'),80,22,4),36,124); text(76,135,'키 설정',GA,INK2,'mm')
for i,(lab,fl) in enumerate([('전체 음량','fill_exp.png'),('배경음','fill_exp.png'),('효과음','fill_exp.png')]):
    y=78+i*34; text(130,y,lab,GA,INK2)
    put(nine(L('bar_frame_64x16.png'),160,14,4),130,y+14); put(nine(L(fl),int(152*(0.8-0.2*i)),8,2),134,y+17)
put(nine(L('button_basic_128x32.png'),90,26,4),60,186); text(105,199,'뒤로',GA,INK2,'mm')
put(nine(L('button_primary_128x32.png'),90,26,4),180,186); text(225,199,'저장',GA,CREAM,'mm')
# 버튼 상태
text(336,36,'버튼 상태',GA,CREAM)
for i,(n,lab,c) in enumerate([('button_basic_128x32.png','기본',INK2),('button_hover_128x32.png','마우스 올림',INK2),('button_press_128x32.png','누름',INK2),('button_lock_128x32.png','잠김',CREAM),('button_primary_128x32.png','주요 버튼',CREAM)]):
    x=336+(i%3)*98; y=52+(i//3)*34
    put(nine(L(n),90,26,4),x,y); text(x+45,y+13,lab,GA,c,'mm')
# 카드
text(336,124,'신통력 카드 (위쪽 띠 = 분류)',GA,CREAM)
for i,(n,lab) in enumerate([('card_common_64.png','공통'),('card_exclusive_64.png','전용'),('card_consumable_64.png','일회용'),('card_coop_64.png','협동')]):
    x=336+i*72; put(nine(L(n),64,96,14),x,140); text(x+32,170,lab,SH,INK2,'mm'); text(x+32,196,'Lv 1',GA,INK2,'mm')
# HUD 바
text(16,240,'HUD 바 (채움 = 오방색)',GA,CREAM)
for i,(lab,fl,v) in enumerate([('체력','fill_hp.png',.75),('경험치','fill_exp.png',.4),('보스 체력','fill_boss.png',.9),('원한','fill_grudge.png',.3)]):
    x=16+(i%2)*310; y=258+(i//2)*40; text(x,y,lab,GA,CREAM)
    put(nine(L('bar_frame_64x16.png'),290,16,4),x,y+14); put(nine(L(fl),max(4,int(282*v)),10,2),x+4,y+17)
text(16,342,'제목·이름 Suhgung16 · 본문·숫자 갈무리11 · 적 #B51E2C 청 #2F6F6A 황 #E3B23C 백 한지 흑 #0B0A10',GA,(125,132,148,255))
S.save(os.path.join(D,'UI_Style_Sheet_640x360.png')); S.resize((1920,1080),Image.NEAREST).save(os.path.join(D,'UI_Style_Sheet_1920x1080.png')); print('ok')
