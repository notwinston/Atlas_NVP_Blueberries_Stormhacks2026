import numpy as np
from math import *
from scipy.optimize import brentq

# --- 1. Verify Lambert relation numerically by constructing the quad in the hyperboloid model
def boost(dx, dz):
    r = hypot(dx, dz)
    if r < 1e-15: return np.eye(3)
    ux, uz = dx/r, dz/r
    ch, sh = cosh(r), sinh(r)
    return np.array([[1+(ch-1)*ux*ux, (ch-1)*ux*uz, sh*ux],
                     [(ch-1)*ux*uz, 1+(ch-1)*uz*uz, sh*uz],
                     [sh*ux, sh*uz, ch]])
def rot(a):
    c,s = cos(a), sin(a)
    return np.array([[c,-s,0],[s,c,0],[0,0,1]])
eta = np.diag([1,1,-1])
def mdot(p,q): return p@eta@q
def dist(p,q): return acosh(max(1.0,-mdot(p,q)))

def corner_angle(m1, m2):
    # symmetric quad = Klein rectangle |x|<=tanh m1, |y|<=tanh m2 ; corner angle via hyperboloid tangent vectors
    kx, ky = tanh(m1), tanh(m2)
    def hyp(x,y):
        w = 1/sqrt(1-x*x-y*y); return np.array([x*w,y*w,w])
    c = hyp(kx,ky); a = hyp(kx,0.0); b = hyp(0.0,ky)
    def tang(p,q):  # unit tangent at p towards q
        v = q + mdot(p,q)*p
        return v/sqrt(mdot(v,v))
    return acos(np.clip(mdot(tang(c,a),tang(c,b)),-1,1))

print("== Lambert check: sinh(m1)sinh(m2) vs cos(corner angle)")
for m1,m2 in [(0.5306,0.5306),(0.4,0.7),(0.3,0.9),(0.6585,0.6585),(0.5,1.0)]:
    ang = corner_angle(m1,m2)
    print(f" m1={m1:.4f} m2={m2:.4f}  sinh*sinh={sinh(m1)*sinh(m2):.6f}  cos(angle)={cos(ang):.6f}  angle={degrees(ang):.3f} deg")

# --- 2. L for rooms (midlines W, D = wall-to-wall) for q
def L_for(W, D, q):
    f = lambda L: sinh(W/(2*L))*sinh(D/(2*L)) - cos(2*pi/q)
    return brentq(f, 0.05, 1000)
print("\n== L (m) such that an equiangular quad with midlines W x D has corner 2pi/q")
print(" room      q=5(needs W=D)   q=6     q=8    | wall lengths at q=6 (m)   corner-to-centre q6 (m) vs Euclid")
for W,D in [(2,2),(3,3),(3,4),(4,5),(6,8),(1.5,1.5),(1.0,1.0),(2.5,3.5)]:
    Ls = {q: L_for(W,D,q) for q in (5,6,8)}
    L6 = Ls[6]; m1, m2 = W/(2*L6), D/(2*L6)
    # wall half-lengths: tanh(a') = tanh(a) cosh(b)  (side opposite a in Lambert quad)
    wallW = 2*L6*atanh(tanh(m1)*cosh(m2))   # wall parallel to W-midline (length along x), at y = +-D/2
    wallD = 2*L6*atanh(tanh(m2)*cosh(m1))
    kx,ky = tanh(m1),tanh(m2); rc = L6*atanh(hypot(kx,ky))
    print(f" {W}x{D}:   {Ls[5]:.3f}{'' if abs(W-D)<1e-9 else '*'}        {Ls[6]:.3f}   {Ls[8]:.3f}  | {wallW:.2f} x {wallD:.2f}   {rc:.2f} vs {hypot(W,D)/2:.2f}")
print(" (* q=5 with W!=D: the quad exists but cannot tile -- odd q needs a square tile)")

# regular {4,5},{4,6} constants
for q in (5,6,7,8):
    m = asinh(sqrt(cos(2*pi/q))); e = 2*acosh(cos(pi/4)/sin(pi/q)); R = acosh(1/tan(pi/4)/tan(pi/q))
    area = 2*pi - 4*2*pi/q
    print(f" {{4,{q}}}: inradius m={m:.4f}  centre spacing 2m={2*m:.4f}  edge e={e:.4f}  circumradius={R:.4f}  tile area={area:.4f} L^2  corner={360/q:.1f} deg")

# --- 3. Rolling a closed Euclidean square loop of side s into H2 (L=1): residual isometry
def roll_square(s, n=4, turn=pi/2):
    C = np.eye(3)
    heading = 0.0
    for i in range(n):
        C = C @ boost(s*cos(heading), s*sin(heading))
        heading += turn
    return C
print("\n== Rolling a physical square loop (side s metres, area s^2) with curvature radius L")
print(" s(m)  L(m)   s/L    virtual displacement(m)  frame rotation(deg)   A/L^2(deg)")
for s,L in [(1,2.827),(2,2.827),(3,2.827),(2,1.885),(2,2.278),(2,4.0),(2,10.0),(1.0,0.942),(3.0,2.827)]:
    C = roll_square(s/L)
    p = C@np.array([0,0,1.0])
    d = acosh(max(1.0,p[2]))*L
    # rotation: compare C with pure boost to p
    B = boost(*( (np.array([p[0],p[1]])/max(1e-12,hypot(p[0],p[1])))*acosh(max(1.0,p[2])) ))
    Rm = np.linalg.inv(B)@C
    ang = degrees(atan2(Rm[1,0],Rm[0,0]))
    print(f" {s:.1f}  {L:.3f}  {s/L:.3f}   {d:.3f}                   {ang:8.2f}            {degrees(s*s/L/L):.2f}")
# exact check: s = 1.0613 L -> displacement should be s (one tile), rotation such that 5 steps close
s = 2*asinh(sqrt(cos(2*pi/5)))
C5 = roll_square(s, n=5)
print(" 5 steps of 2m with 90deg turns ({5,4} pentagon): returns to start? |C5 - Rot(?)| pos =", np.round(C5@np.array([0,0,1.0]),6))
s6 = 2*asinh(sqrt(cos(2*pi/6)))
C6 = roll_square(s6, n=6)
print(" 6 steps (q=6):", np.round(C6@np.array([0,0,1.0]),6))

# rectangle a x b loop for q=6: 6 steps alternate a,b
def roll_rect(a,b,n):
    C = np.eye(3); h=0.0
    for i in range(n):
        s = a if i%2==0 else b
        C = C @ boost(s*cos(h), s*sin(h)); h += pi/2
    return C
L = L_for(3,4,6)
print(" 3x4 loop, q=6, L=%.3f: after 6 legs pos ="%L, np.round(roll_rect(3/L,4/L,6)@np.array([0,0,1.0]),6))

# --- 4. tiles within hyperbolic radius d : N ~ area ratio
print("\n== tiles within radius d (units of L): N ~ 2pi(cosh d -1)/tile_area")
for q in (5,6):
    A = 2*pi-8*pi/q
    print(f" q={q}: " + "  ".join(f"d={d}: {2*pi*(cosh(d)-1)/A:.0f}" for d in (1,1.5,2,2.5,3,3.5,4,5)))

# --- 5. Klein-rectangle chart distortion (room rect -> tile): local metric scale along x at (x,0) and transverse
print("\n== Klein-rectangle chart: local scale (virtual metres per physical metre), 3x3 room, q=5, L=2.827")
L = L_for(3,3,5); m = 1.5/L; t = tanh(m)
for X,Z in [(0,0),(0.75,0),(1.5,0),(0.75,0.75),(1.5,1.5),(1.5,0.75)]:
    kx, kz = t*X/1.5, t*Z/1.5
    k2 = kx*kx+kz*kz
    # Klein metric: ds^2 = (dk^2)/(1-k^2) + (k.dk)^2/(1-k^2)^2
    J = t/1.5  # dk/dX
    def scale(dx,dz):
        dk = np.array([dx,dz])*J
        kd = kx*dk[0]+kz*dk[1]
        return L*sqrt((dk@dk)/(1-k2) + kd*kd/(1-k2)**2)
    print(f" at ({X},{Z}) m: scale along x={scale(1,0):.3f}  along z={scale(0,1):.3f}  diag={scale(1/sqrt(2),1/sqrt(2)):.3f}")

# --- 6. Exp-chart (fixed) distortion: transverse stretch sinh(r)/r
print("\n== exp-chart transverse stretch sinh(r/L)/(r/L)")
for r,L in [(1.5,2.827),(2.12,2.827),(2.5,2.278),(3.2,2.9),(5,4.9)]:
    print(f" r={r} L={L}: {sinh(r/L)/(r/L):.3f}")

# --- 7. hypercycle: wall-anchored (Weeks-style) tracking: translation gain cosh(delta/L), curvature tanh(delta/L)/L
print("\n== wall-anchored tracking at offset delta from wall (L=2.827): gain, turn rate deg/m, radius m")
L=2.827
for dlt in (0.3,0.5,1.0,1.5):
    print(f" delta={dlt}: gain={cosh(dlt/L):.3f}  {degrees(tanh(dlt/L)/L):.2f} deg/m  radius={L/tanh(dlt/L):.1f} m")

# --- 8. geodesic parallel to wall diverges (rolling): start offset delta, walk s
print("\n== rolling: walk s parallel to a virtual wall starting delta away (L=2.827): virtual wall distance at end")
for dlt in (0.3,0.5,1.0):
    for s in (1.5,3.0):
        d = asinh(sinh(dlt/L)*cosh(s/L))*L
        print(f" delta={dlt} s={s}: {d:.2f} m")

# --- 9. chord error of straight segment length l in log chart ~ l^2/(8 L) * k ; brute force: geodesic at distance d from viewer
print("\n== max chord deviation (mm) of a geodesic edge of length l seen in the log chart, viewer at perpendicular distance d")
def logmap(p):
    s = hypot(p[0],p[1]); 
    return np.array([p[0],p[1]])/s*asinh(s) if s>1e-12 else np.zeros(2)
for L in (0.94,1.885,2.827,4.0):
  for l in (0.1,0.25,0.5,1.0):
    worst=0
    for d in (0.5,1.0,2.0,4.0):
      for off in np.linspace(0,6,25):   # along-wall offset of segment start (m)
        # geodesic: points B(d,0) * Bz(t) origin ; i.e. wall perpendicular to x axis at distance d
        def pt(t): return boost(d/L,0)@boost(0,t/L)@np.array([0,0,1.0])
        a = logmap(pt(off))*L; b = logmap(pt(off+l))*L; mid = logmap(pt(off+l/2))*L
        dev = np.linalg.norm(mid-(a+b)/2)
        worst=max(worst,dev)
    print(f" L={L} l={l}: {worst*1000:.1f} mm", end=" |")
  print()
