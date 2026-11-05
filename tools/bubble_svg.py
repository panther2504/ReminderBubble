# Generates the glass-sphere SVG for a given accent pair. Single source of truth for the look.
import sys
def lighten(c,t): return tuple(round(c[i]+(255-c[i])*t) for i in range(3))
def darken(c,t):  return tuple(round(c[i]*(1-t)) for i in range(3))
def hx(c): return '#%02x%02x%02x'%c
def svg(a=(124,92,255), b=(194,92,255), bell=True, uid='x', ground=True):
    hi=lighten(b,.62); mid=b; lo=a; deep=darken(a,.58); rimc=lighten(b,.8)
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200" width="200" height="200">
<defs>
  <radialGradient id="body{uid}" cx=".5" cy=".5" r=".78" fx=".33" fy=".24">
    <stop offset="0" stop-color="{hx(hi)}" stop-opacity=".92"/>
    <stop offset=".26" stop-color="{hx(mid)}" stop-opacity=".92"/>
    <stop offset=".62" stop-color="{hx(lo)}" stop-opacity=".95"/>
    <stop offset="1" stop-color="{hx(deep)}" stop-opacity=".98"/>
  </radialGradient>
  <radialGradient id="edge{uid}" cx=".5" cy=".5" r=".5">
    <stop offset=".78" stop-color="#000" stop-opacity="0"/>
    <stop offset="1" stop-color="#05001a" stop-opacity=".42"/>
  </radialGradient>
  <radialGradient id="refl{uid}" cx=".5" cy=".5" r=".5">
    <stop offset="0" stop-color="{hx(rimc)}" stop-opacity=".62"/>
    <stop offset=".6" stop-color="{hx(hi)}" stop-opacity=".18"/>
    <stop offset="1" stop-color="{hx(hi)}" stop-opacity="0"/>
  </radialGradient>
  <linearGradient id="gloss{uid}" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#fff" stop-opacity=".9"/>
    <stop offset=".55" stop-color="#fff" stop-opacity=".22"/>
    <stop offset="1" stop-color="#fff" stop-opacity=".02"/>
  </linearGradient>
  <linearGradient id="rim{uid}" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#fff" stop-opacity=".95"/>
    <stop offset=".42" stop-color="#fff" stop-opacity=".06"/>
    <stop offset=".6" stop-color="#fff" stop-opacity=".05"/>
    <stop offset="1" stop-color="{hx(rimc)}" stop-opacity=".75"/>
  </linearGradient>
  <linearGradient id="bellg{uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#e9e2ff"/></linearGradient>
  <radialGradient id="glow{uid}" cx=".5" cy=".5" r=".5"><stop offset=".55" stop-color="{hx(mid)}" stop-opacity=".5"/><stop offset="1" stop-color="{hx(mid)}" stop-opacity="0"/></radialGradient>
  <radialGradient id="shad{uid}" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#000" stop-opacity=".5"/><stop offset="1" stop-color="#000" stop-opacity="0"/></radialGradient>
  <filter id="bsh{uid}" x="-30%" y="-30%" width="160%" height="170%"><feDropShadow dx="0" dy="3.5" stdDeviation="3.2" flood-color="#1a0b4d" flood-opacity=".55"/></filter>
</defs>
{"<ellipse cx=\"100\" cy=\"186\" rx=\"58\" ry=\"9\" fill=\"url(#shad"+uid+")\"/>" if ground else ""}
<circle cx="100" cy="100" r="96" fill="url(#glow{uid})" opacity=".0"/>
<circle cx="100" cy="100" r="80" fill="url(#body{uid})"/>
<circle cx="100" cy="100" r="80" fill="url(#edge{uid})"/>
<ellipse cx="100" cy="146" rx="54" ry="30" fill="url(#refl{uid})"/>
<circle cx="100" cy="100" r="79.2" fill="none" stroke="url(#rim{uid})" stroke-width="1.8"/>
<path d="M 100 20.5 C 66 20.5 40 40 33 66 C 54 52 78 49 100 49 C 122 49 146 52 167 66 C 160 40 134 20.5 100 20.5 Z" fill="url(#gloss{uid})"/>
<ellipse cx="62" cy="48" rx="7" ry="4.2" transform="rotate(-38 62 48)" fill="#fff" opacity=".85"/>
{'<g filter="url(#bsh'+uid+')" transform="translate(100 101) scale(3.4) translate(-12 -11.2)"><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9z" fill="url(#bellg'+uid+')"/><path d="M10 18.5a2 2 0 0 0 4 0z" fill="url(#bellg'+uid+')"/></g>' if bell else ''}
</svg>'''
if __name__=='__main__':
    open('bubble-violet.svg','w').write(svg())
    accents={'violet':((124,92,255),(194,92,255)),'blue':((59,130,246),(34,211,238)),'teal':((20,184,166),(52,211,153)),'green':((34,197,94),(163,230,53)),'amber':((245,158,11),(249,115,22)),'rose':((244,63,94),(236,72,153))}
    cells=''.join(f'<div class="c" style="background:{bg}">{svg(*accents[k],uid=k+str(i))}<span>{k}</span></div>' for i,(k,bg) in enumerate(zip(accents,['#0e0e18','#f2f2f7','#1f6f5c','#d9c8a9','#2b2b36','#7aa7e0'])))
    open('preview.html','w').write(f'<html><body style="margin:0;background:#222;font:12px sans-serif;color:#fff"><style>.c{{display:inline-block;width:260px;height:240px;position:relative;text-align:center;padding-top:20px;box-sizing:border-box}}.c svg{{width:200px;height:200px}}.c span{{position:absolute;left:8px;bottom:6px;opacity:.6}}</style>{cells}</body></html>')
