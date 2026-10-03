import math, itertools, collections, time
from math import cos, sin, pi, cosh, sinh, acosh, asinh, atanh, sqrt, tan, log

def consts(p,q):
    ch_a = cos(pi/p)/sin(pi/q)          # half-edge
    ch_r = cos(pi/q)/sin(pi/p)          # inradius
    ch_R = 1/(tan(pi/p)*tan(pi/q))      # circumradius
    a,r,R = acosh(ch_a), acosh(ch_r), acosh(ch_R)
    area = pi*(p-2) - p*2*pi/q
    return a,r,R,area

for (p,q) in [(4,5),(4,6),(5,4),(7,3),(4,7),(4,8),(3,7),(6,4)]:
    a,r,R,area = consts(p,q)
    # verify right triangle (centre, edge-mid, vertex): cosh R = cosh r cosh a ; angles pi/p at centre, pi/q at vertex
    chk1 = cosh(R) - cosh(r)*cosh(a)
    # angle at centre: tan(pi/p) = tanh(a)/sinh(r)
    chk2 = math.tan(pi/p) - math.tanh(a)/sinh(r)
    # angle at vertex: tan(pi/q) = tanh(r)/sinh(a)
    chk3 = math.tan(pi/q) - math.tanh(r)/sinh(a)
    # area by Gauss-Bonnet of triangle x 2p : 2p*(pi - pi/2 - pi/p - pi/q)
    area2 = 2*p*(pi/2 - pi/p - pi/q)
    print(f"{{{p},{q}}}: half-edge a={a:.6f} edge={2*a:.6f} inradius r={r:.6f} 2r={2*r:.6f} circumR={R:.6f} area={area:.6f} (={area/pi:.4f}pi) chk={chk1:.1e},{chk2:.1e},{chk3:.1e},{area-area2:.1e} cosh2r={cosh(2*r):.6f} sinh2r={sinh(2*r):.6f} cosh(edge)={cosh(2*a):.6f}")
phi=(1+sqrt(5))/2
print("phi",phi,"sqrt(phi)",sqrt(phi), "1+2/sqrt5", 1+2/sqrt(5))
