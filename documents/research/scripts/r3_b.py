# Exact {4,5} BFS in Z[phi] + float cross-check + Holonomy word-rule check + {4,6} integer BFS
import math, time, collections
from math import sqrt, cosh, sinh, acosh, pi, cos, sin

# ---- Z[phi] numbers as (a,b) = a + b*phi, phi^2 = phi + 1
def zadd(x,y): return (x[0]+y[0], x[1]+y[1])
def zneg(x): return (-x[0], -x[1])
def zmul(x,y):
    a,b = x; c,d = y
    return (a*c + b*d, a*d + b*c + b*d)
PHI=(0,1); ONE=(1,0); ZERO=(0,0)
phi=(1+sqrt(5))/2
def zf(x): return x[0]+x[1]*phi

# Tile frame M in SO+(2,1). Representation: M = [[A11,A12,s*B1],[A21,A22,s*B2],[s*C1,s*C2,D]] with s = sqrt(phi)
# entries A,B,C,D in Z[phi]. product: (s*x)*(s*y) = phi*x*y
class EM:
    __slots__=("A","B","C","D")
    def __init__(s,A,B,C,D): s.A=A; s.B=B; s.C=C; s.D=D
    def mul(s,o):
        A=[[None]*2 for _ in range(2)]
        for i in range(2):
            for j in range(2):
                v=ZERO
                for k in range(2): v=zadd(v, zmul(s.A[i][k], o.A[k][j]))
                v=zadd(v, zmul(PHI, zmul(s.B[i], o.C[j])))
                A[i][j]=v
        B=[None]*2
        for i in range(2):
            v=ZERO
            for k in range(2): v=zadd(v, zmul(s.A[i][k], o.B[k]))
            v=zadd(v, zmul(s.B[i], o.D))
            B[i]=v
        C=[None]*2
        for j in range(2):
            v=ZERO
            for k in range(2): v=zadd(v, zmul(s.C[k], o.A[k][j]))
            v=zadd(v, zmul(s.D, o.C[j]))
            C[j]=v
        D=ZERO
        for k in range(2): D=zadd(D, zmul(PHI, zmul(s.C[k], o.B[k])))
        D=zadd(D, zmul(s.D, o.D))
        return EM(A,B,C,D)
    def centre(s): return (s.B[0], s.B[1], s.D)   # (X,Y,T): actual = (sqrt(phi) X, sqrt(phi) Y, T)
    def tofloat(s):
        sp=sqrt(phi)
        return [[zf(s.A[0][0]), zf(s.A[0][1]), sp*zf(s.B[0])],
                [zf(s.A[1][0]), zf(s.A[1][1]), sp*zf(s.B[1])],
                [sp*zf(s.C[0]), sp*zf(s.C[1]), zf(s.D)]]
I_=EM([[ONE,ZERO],[ZERO,ONE]],[ZERO,ZERO],[ZERO,ZERO],ONE)
R_=EM([[ZERO,(-1,0)],[ONE,ZERO]],[ZERO,ZERO],[ZERO,ZERO],ONE)       # rot +90deg
T_=EM([[PHI,ZERO],[ZERO,ONE]],[ONE,ZERO],[ONE,ZERO],PHI)            # boost along x by 2r: cosh=phi, sinh=sqrt(phi)
Rk=[I_, R_, R_.mul(R_), R_.mul(R_).mul(R_)]
# step k: go through edge k, then turn so that edge 2 of new tile faces back?  frame' = M * R^k * T
STEP=[Rk[k].mul(T_) for k in range(4)]

# check T_ is a Lorentz matrix numerically
def fmul(a,b): return [[sum(a[i][k]*b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
Tf=T_.tofloat()
eta=[[1,0,0],[0,1,0],[0,0,-1]]
def tr(a): return [[a[j][i] for j in range(3)] for i in range(3)]
chk=fmul(tr(Tf),fmul(eta,Tf))
print("T^t eta T =", [[round(x,12) for x in r] for r in chk])
print("boost dist", acosh(Tf[2][2]), "expected 2r", 2*acosh(cos(pi/5)/sin(pi/4)))

def bfs_exact(depth):
    seen={I_.centre():0}
    frames={I_.centre():I_}
    frontier=[I_]
    counts=[1]
    for d in range(1,depth+1):
        nxt=[]
        for M in frontier:
            for k in range(4):
                N=M.mul(STEP[k]); c=N.centre()
                if c not in seen:
                    seen[c]=d; frames[c]=N; nxt.append(N)
        counts.append(len(nxt)); frontier=nxt
    return counts, seen, frames

t=time.time(); counts, seen, frames = bfs_exact(9); print("exact {4,5} coordination seq:", counts, "total", sum(counts), "time %.2fs"%(time.time()-t))
# recurrence check
c=counts
print("ratios", [round(c[i+1]/c[i],4) for i in range(1,len(c)-1)])
# try linear recurrences a(n)=x a(n-1)+y a(n-2)+z a(n-3)
import itertools
for order in (2,3,4):
    import numpy as np
    A=np.array([[c[i+j] for j in range(order)] for i in range(1,1+order)],dtype=float)
    b=np.array([c[i+order] for i in range(1,1+order)],dtype=float)
    try:
        x=np.linalg.solve(A,b); 
        pred=[sum(x[j]*c[i+j] for j in range(order)) for i in range(1,len(c)-order)]
        print("order",order,"coeffs",np.round(x,6),"pred",np.round(pred,3),"actual",c[1+order:])
    except Exception as e: print(e)

# ---- float BFS with tolerance hashing for {p,q}
def bfs_float(p,q,depth,tol=1e-6):
    r=acosh(cos(pi/q)/sin(pi/p)); ch,sh=cosh(2*r),sinh(2*r)
    T=[[ch,0,sh],[0,1,0],[sh,0,ch]]
    def rot(t): return [[cos(t),-sin(t),0],[sin(t),cos(t),0],[0,0,1]]
    steps=[fmul(rot(2*pi*k/p),T) for k in range(p)]
    I=[[1,0,0],[0,1,0],[0,0,1]]
    def key(M): return (round(M[0][2]/1e-3), round(M[1][2]/1e-3), round(M[2][2]/1e-3))
    seen={}
    def find(M):
        k=key(M)
        for dx in (-1,0,1):
            for dy in (-1,0,1):
                for dz in (-1,0,1):
                    if (k[0]+dx,k[1]+dy,k[2]+dz) in seen: return True
        return False
    seen[key(I)]=I; frontier=[I]; counts=[1]; allM=[I]
    for d in range(1,depth+1):
        nxt=[]
        for M in frontier:
            for S in steps:
                N=fmul(M,S)
                if not find(N):
                    seen[key(N)]=N; nxt.append(N); allM.append(N)
        counts.append(len(nxt)); frontier=nxt
    return counts, allM
for (p,q,dep) in [(4,5,8),(4,6,7),(5,4,7),(7,3,8),(4,7,6),(4,8,6)]:
    t=time.time(); cf,_=bfs_float(p,q,dep); print(f"float {{{p},{q}}} coordination:",cf,"sum",sum(cf),"%.2fs"%(time.time()-t))

# ---- Holonomy word rules: branch then F/L/R; R invalid iff previous step R; L invalid iff no R between it and last L
def count_words(n, first_L_ok=True):
    # state: (prevR, Lok)
    from collections import Counter
    st=Counter({(False, first_L_ok):4})
    res=[1,4]
    for i in range(2,n+1):
        ns=Counter()
        for (prevR,Lok),cnt in st.items():
            ns[(False,Lok)]+=cnt            # F
            if not prevR: ns[(True,True)]+=cnt   # R
            if Lok: ns[(False,False)]+=cnt       # L
        st=ns; res.append(sum(st.values()))
    return res
print("Holonomy rule word counts (L ok at start):", count_words(9,True))
print("Holonomy rule word counts (L not ok at start):", count_words(9,False))
