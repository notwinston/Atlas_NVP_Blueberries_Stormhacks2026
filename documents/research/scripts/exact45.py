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

