from PIL import Image, ImageDraw
import make_hud as M
H=M.H; INK=M.INK; HJ=M.HJ; WL=M.WL; TAN=M.TAN; TAND=M.TAND; BL=M.BL; Y=M.Y; R=M.R
def node(k):
    bc, face, top = {'locked': (TAND, H('#E2D6B8'), TAN), 'open': (BL, HJ, H('#5A7CB8')), 'learned': (Y, WL, WL)}[k]
    im=Image.new('RGBA',(32,32)); d=ImageDraw.Draw(im)
    d.rectangle((0,0,31,31),fill=INK); d.rectangle((1,1,30,30),fill=bc); d.line((1,1,30,1),fill=top)
    d.rectangle((3,3,28,28),fill=INK); d.rectangle((4,4,27,27),fill=face)
    if k=='learned':
        for (x,y) in [(1,1),(29,1),(1,29),(29,29)]: d.rectangle((x,y,x+1,y+1),fill=R)
    return im
def line(k):
    c={'locked':TAND,'learned':Y}[k]; im=Image.new('RGBA',(8,8)); ImageDraw.Draw(im).rectangle((0,3,7,4),fill=c); return im
import os
for k in ['locked','open','learned']: node(k).save(f'out/sinnaerim_node_{k}_32.png')
for k in ['locked','learned']: line(k).save(f'out/sinnaerim_line_{k}_8.png')
sh=Image.new('RGBA',(160,48),H('#1B2233'))
for i,k in enumerate(['locked','open','learned']): sh.alpha_composite(node(k),(6+i*40,8))
for i,k in enumerate(['locked','learned']): sh.alpha_composite(line(k).resize((24,8)),(126,14+i*16))
sh.resize((640,192),Image.NEAREST).save('out/_sinnaerim_nodes_x4.png')
