# Simulates the strip-based genie warp (same maths as the C# overlay) on a real panel screenshot.
from PIL import Image, ImageDraw
import math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = Image.open(os.path.join(HERE, '..', 'docs', 'panel-dark.jpg')).convert('RGB').resize((408, 660), Image.LANCZOS)
W, H = SRC.size
N = 96
D = float(sys.argv[1]) if len(sys.argv) > 1 else 0.75       # how far the collapse is staggered along the panel
SIDE = -1                                                   # bubble is on the left of the panel
BUBBLE_R = 32

def smooth(q): return q * q * (3 - 2 * q)
def lerp(a, b, t): return a + (b - a) * t

def frame(p, canvas_w=620, canvas_h=700, panel_left=190):
    img = Image.new('RGB', (canvas_w, canvas_h), (22, 22, 30))
    d = ImageDraw.Draw(img)
    ax, ay = 70, 330                                         # bubble centre (canvas coords)
    sw0 = W / N
    order = range(N - 1, -1, -1) if SIDE < 0 else range(N)   # far strips first, near strips on top
    for i in order:
        u = 1 - (i + .5) / N if SIDE < 0 else (i + .5) / N   # 1 = nearest the bubble
        q = max(0.0, min(1.0, p * (1 + D) - (1 - u) * D))
        e = smooth(q)
        xi = panel_left + (i + .5) * sw0
        cx = lerp(xi, ax, e)
        cy = lerp(H / 2, ay, e) if False else lerp(canvas_h / 2 - 0, ay, e)
        hh = lerp(H / 2, BUBBLE_R * .5, e)
        sw = lerp(sw0 + 1.2, 1.5, e)
        strip = SRC.crop((int(i * sw0), 0, int((i + 1) * sw0 + 1), H))
        dw, dh = max(1, round(sw)), max(1, round(2 * hh))
        img.paste(strip.resize((dw, dh), Image.BILINEAR), (round(cx - dw / 2), round(cy - hh)))
    # the bubble
    d.ellipse((ax - BUBBLE_R, ay - BUBBLE_R, ax + BUBBLE_R, ay + BUBBLE_R), fill=(140, 92, 255), outline=(220, 200, 255), width=2)
    return img

ps = [0, .2, .35, .5, .65, .8, .92, 1]
fr = [frame(p).resize((310, 350), Image.LANCZOS) for p in ps]
S = Image.new('RGB', (4 * 312, 2 * 352), (10, 10, 14))
for k, f in enumerate(fr): S.paste(f, ((k % 4) * 312, (k // 4) * 352))
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, '..', 'docs', 'genie-sim.jpg')
S.save(out, quality=88); print('saved', out)
