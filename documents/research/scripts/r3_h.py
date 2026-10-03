import numpy as np, math, time
from math import cos, sin, pi, cosh, sinh, acosh, sqrt, tan, log
from exact45 import *
# --- exact invariants for {4,5}
def eq(A,B): return A.A==B.A and A.B==B.B and A.C==B.C and A.D==B.D
V=Rk[1].mul(T_).mul(Rk[2])           # step through edge 1 with 'edge 0 points back' convention: R^1 * T * R_pi
P=I_
for k in range(1,7):
    P=P.mul(V); print("V^%d == I ?"%k, eq(P,I_), " centre==root centre ?", P.centre()==I_.centre())
S=[Rk[k].mul(T_).mul(Rk[2]) for k in range(4)]
print("S_k * S_0 == R^k (i.e. step then step back returns to same tile, rotated by k):", [eq(S[k].mul(S[0]), Rk[k]) for k in range(4)])
print("S_0*S_0 == I:", eq(S[0].mul(S[0]), I_))
# exact norm check phi*(X^2+Y^2) - T^2 == -1 for all centres to depth 10
cnt,seen,frames=bfs_exact(12)
print("coordination to depth 12:",cnt)
OEIS=[1,4,12,28,64,148,340,780,1792,4116,9452,21708,49856]
print("matches OEIS A377322:", cnt==OEIS[:len(cnt)])
bad=0; mx={}
for c,d in seen.items():
    X,Y,T=c
    n=zadd(zmul(PHI, zadd(zmul(X,X),zmul(Y,Y))), zneg(zmul(T,T)))
    if n!=(-1,0): bad+=1
    m=max(abs(v) for z in c for v in z); mx[d]=max(mx.get(d,0),m)
print("norm violations:",bad)
print("max |integer coefficient| by graph depth:",[mx[d] for d in sorted(mx)])
g=[mx[d+1]/mx[d] for d in range(4,12)]; print("growth per level ~",np.round(g,3), " e^(2r)=",math.exp(1.0612750619))
# straight-line worst case: T_^n
P=I_
for n in range(1,41):
    P=P.mul(T_)
    m=max(abs(v) for z in P.centre() for v in z)
    if n in (10,20,30,35,36,37,38,39,40): print("  straight n=%d tiles: max coeff = %d  bits=%d"%(n,m,m.bit_length()))
# frame entries can be larger than centre? check max over all frame entries at n=36
P=I_
for n in range(36): P=P.mul(T_)
allv=[abs(v) for row in P.A for z in row for v in z]+[abs(v) for z in P.B for v in z]+[abs(v) for z in P.C for v in z]+[abs(v) for v in P.D]
print("  n=36 max frame coeff bits:", max(allv).bit_length(), " (products of two such need 2x bits before reduction)")
# timing of exact step in python (BigInt) for reference
t=time.time(); P=I_
for i in range(20000): P=P.mul(S[1+i%3])
print("python exact step: %.2f us"%((time.time()-t)/20000*1e6), " final coeff bits", max(abs(v) for z in P.centre() for v in z).bit_length())
# --- generic {p,q} float invariants: vertex closure V^q = I, min centre separation
def gens(p,q):
    r=acosh(cos(pi/q)/sin(pi/p)); c=cosh(2*r); s=sinh(2*r)
    R=np.array([[cos(2*pi/p),-sin(2*pi/p),0],[sin(2*pi/p),cos(2*pi/p),0],[0,0,1.0]])
    T=np.array([[c,0,s],[0,1,0],[s,0,c]]); Rpi=np.diag([-1.0,-1.0,1.0])
    return R,T,Rpi,r
for (p,q) in [(4,5),(4,6),(5,4),(7,3),(3,7),(6,4),(4,8)]:
    R,T,Rpi,r=gens(p,q); V=R@T@Rpi
    W=np.linalg.matrix_power(V,q)
    closes=[np.allclose(np.linalg.matrix_power(V,k),np.eye(3),atol=1e-9) for k in range(1,q+1)]
    c2r=2*cos(pi/q)**2/sin(pi/p)**2-1
    print(f"{{{p},{q}}}: cosh(2r)={cosh(2*r):.6f} (formula 2cos^2(pi/q)/sin^2(pi/p)-1 = {c2r:.6f}); V^k==I for k=1..q: {closes}")
# edge-crossing test normal
r=acosh(cos(pi/5)/sin(pi/4)); n0=np.array([cosh(r),0,sinh(r)]); pt=np.array([sinh(r),0,cosh(r)])
print("edge normal check <p_edge_mid, n0> =", pt[0]*n0[0]+pt[1]*n0[1]-pt[2]*n0[2], " <origin,n0> =", -n0[2])
# Hyperbolica cull radii from HyperEngine constants
for v in (0.9975,0.99): print("Poincare r^2=%g -> hyperbolic distance %.3f"%(v, 2*math.atanh(sqrt(v))))
# tiles alive bound for radius-k ball in graph distance
import itertools
cs=np.cumsum(OEIS); print("cumulative tiles within graph distance n:", list(cs))
