# Verify the explicit add-only step formulas quoted in the memo, the memo's tree convention, and the bounded-table reach.
import random, math, numpy as np
from exact45 import *
def tphi(z): return (z[1], z[0]+z[1])
def neg(z): return (-z[0],-z[1])
def stepT(M):   # M*T*H, additions only
    a11,a12,a21,a22=M.A[0][0],M.A[0][1],M.A[1][0],M.A[1][1]; b1,b2=M.B; c1,c2=M.C; d=M.D
    return EM([[neg(tphi(zadd(a11,b1))), neg(a12)],[neg(tphi(zadd(a21,b2))), neg(a22)]],
              [zadd(a11,tphi(b1)), zadd(a21,tphi(b2))],
              [neg(zadd(tphi(c1),d)), neg(c2)],
              tphi(zadd(c1,d)))
def rot1(M):    # M*R (90 deg)
    return EM([[M.A[0][1], neg(M.A[0][0])],[M.A[1][1], neg(M.A[1][0])]], list(M.B), [M.C[1], neg(M.C[0])], M.D)
def rotk(M,k):
    for _ in range(k%4): M=rot1(M)
    return M
def step(M,k): return stepT(rotk(M,k))
def eq(A,B): return A.A==B.A and list(A.B)==list(B.B) and list(A.C)==list(B.C) and A.D==B.D
H_=Rk[2]
ok=True; random.seed(1)
for trial in range(300):
    M=I_; N=I_
    for i in range(random.randint(1,25)):
        k=random.randrange(4)
        M=M.mul(Rk[k]).mul(T_).mul(H_); N=step(N,k)
    ok&=eq(M,N)
print("explicit add-only Step(k) == M*R^k*T*H for 300 random words:", ok)
# canonical frame: lexicographic min of first column over 4 rotations
def canon(M):
    c=[rotk(M,j) for j in range(4)]
    return min(c,key=lambda F:(F.A[0][0],F.A[1][0],F.C[0]))
# order independence: BFS vs random walks give same canonical frame per centre
frames={}
def visit(M):
    c=M.centre(); F=canon(M); key=(tuple(map(tuple,F.A)),tuple(F.B),tuple(F.C),F.D)
    if c in frames: return frames[c]==key
    frames[c]=key; return True
good=True
front=[I_]; visit(I_)
for d in range(6):
    nx=[]
    for M in front:
        for k in range(4):
            N=step(M,k)
            if N.centre() not in frames: nx.append(N)
            good&=visit(N)
    front=nx
for trial in range(2000):
    M=I_
    for i in range(random.randint(1,6)):
        M=step(M,random.randrange(4)); good&=visit(M)
print("canonical frame is path-independent (BFS depth 6 + 2000 random walks):", good, " tiles:",len(frames))
# memo tree convention, Rule S: child = Step(3 for L, 1 for R) then Step(2)
seen={I_.centre()}; front=[I_]; inj=True; nodes=1
for d in range(14):
    nx=[]
    for M in front:
        for k in (3,1):
            A=step(M,k); B=step(A,2)
            for X in (A,B):
                c=X.centre()
                if c in seen: inj=False
                seen.add(c)
            nx.append(B); nodes+=1
    front=nx
print("Rule S with memo convention: depth 14 nodes",nodes,"tiles",len(seen),"injective:",inj)
# Rule A in memo convention: children = Step(2) (straight) or turn opposite to last turn
seen={I_.centre()}; front=[(I_,1)]; inj=True; nodes=1
for d in range(15):
    nx=[]
    for (M,last) in front:
        for (k,nl) in ((2,last),(4-last,4-last)):   # last in {1,3}; opposite = 4-last
            B=step(M,k); c=B.centre()
            if c in seen: inj=False
            seen.add(c); nx.append((B,nl)); nodes+=1
    front=nx
print("Rule A with memo convention: depth 15 nodes",nodes,"injective:",inj)
# bounded-table reach: nearest tile centre NOT contained in a depth-n table
cnt,seen_bfs,fr=bfs_exact(11)
sp=math.sqrt(phi)
for n in (7,8,9,10):
    dm=min(math.acosh(zf(c[2])) for c,dd in seen_bfs.items() if dd==n+1)
    print(f"table to depth {n}: {sum(cnt[:n+1])} tiles; nearest missing tile centre at {dm:.3f} units; minus circumradius 0.842 = {dm-0.8425:.3f} units of guaranteed coverage")
# square-walk demo number
def rot(t): return np.array([[math.cos(t),-math.sin(t),0],[math.sin(t),math.cos(t),0],[0,0,1.0]])
def boost(d): return np.array([[math.cosh(d),0,math.sinh(d)],[0,1,0],[math.sinh(d),0,math.cosh(d)]])
a=math.acosh(math.cos(math.pi/4)/math.sin(math.pi/5))
P=np.linalg.matrix_power(boost(2*a)@rot(math.pi/2),4)
print("square walk (side = tile edge, 90deg left turns): end distance", math.acosh(P[2,2]), "units =", math.acosh(P[2,2])/(2*a),"tile edges")
