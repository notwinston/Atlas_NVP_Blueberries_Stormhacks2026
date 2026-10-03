from math import *
from scipy.optimize import brentq
def L_for(W, D, q):
    f = lambda L: sinh(W/(2*L))*sinh(D/(2*L)) - cos(2*pi/q)
    return brentq(f, 0.05, 1000)
print("loop-fit L (walk loop a x b closes after q right-angle turns):")
for a,b,q in [(2,2,5),(2,3,6),(3,3,5),(3,4,6),(2,2,6),(3,3,6),(1.2,1.2,5),(1.0,1.0,5),(1.5,1.5,5),(2.2,2.2,5),(3.2,3.2,5),(1.6,1.6,5),(2.1,2.1,5)]:
    print(f"  loop {a}x{b} m, q={q}: L = {L_for(a,b,q):.3f} m")
print("\nsquare laps of side s at radius L: vertices lie on a virtual circle; central angle per side, ring radius, sides per virtual revolution, frame rotation/lap")
for s,L in [(2.0,1.885),(2.0,2.5),(2.0,2.827),(2.0,3.5),(1.0,2.827),(3.0,2.827),(1.1,1.04)]:
    x = (cosh(s/L)-1)/2
    if x<1:
        al = acos(x); rho = asinh(sinh(s/(2*L))/sin(al/2))*L
        print(f"  s={s} L={L}: s/L={s/L:.3f} central angle/side={degrees(al):.1f} deg  ring radius={rho:.2f} m  sides per rev={2*pi/al:.2f}  net virtual shift per lap (chord of 4 sides)={2*L*asinh(sinh(rho/L)*abs(sin(4*al/2))):.2f} m")
# parallax-equivalent distance
print("\nmotion-parallax equivalent Euclidean distance L*tanh(d/L):")
for L in (1.0,1.885,2.827):
    print("  L=%.3f: "%L + "  ".join("d=%g m -> %.2f m"%(d, L*tanh(d/L)) for d in (1,2,4,8,16)))
# apparent width scale: w/(L sinh(d/L)) vs w/d
print("\nangular size ratio hyperbolic/Euclid = (d/L)/sinh(d/L):")
for L in (1.0,1.885,2.827):
    print("  L=%.3f: "%L + "  ".join("d=%g m -> %.2f"%(d, (d/L)/sinh(d/L)) for d in (1,2,4,6,8)))
