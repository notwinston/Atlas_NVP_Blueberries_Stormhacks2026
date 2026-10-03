# LOD ladder: screen-space chord error of a Klein-straight n x n floor grid under the log-map (H2xE) warp, vs tile distance.
import numpy as np, math
from math import cos, sin, pi, cosh, sinh, acosh, tanh, sqrt, tan, degrees, atan
exec(open('r3_d.py').read().split("results={}")[0])   # gen, mink, hdist, tile_boundary, view_boost
p,q=4,5
counts,allM=gen(p,q,8)
r_=acosh(cos(pi/q)/sin(pi/p)); R_=acosh(1/(tan(pi/p)*tan(pi/q))); k=tanh(r_)
def klein_to_h(u,v):
    w=1/np.sqrt(1-u*u-v*v); return np.stack([u*w,v*w,w],axis=-1)
def logmap(P):   # (...,3) hyperboloid -> tangent coords (units)
    s=np.sqrt(P[...,0]**2+P[...,1]**2); d=np.arcsinh(s)
    f=np.where(s>1e-12, d/np.maximum(s,1e-12), 1.0)
    return np.stack([P[...,0]*f,P[...,1]*f],axis=-1)
def seg_err(n, G, L, eye=1.6, sub=5):
    # all grid segments of an n x n Klein grid on the tile; returns max angular error (deg) seen from eye
    g=np.linspace(-k,k,n+1)
    segs=[]
    for i in range(n+1):
        for j in range(n):
            segs.append(((g[i],g[j]),(g[i],g[j+1]))); segs.append(((g[j],g[i]),(g[j+1],g[i])))
    A=np.array([s[0] for s in segs]); B=np.array([s[1] for s in segs])
    worst=0
    PA=klein_to_h(A[:,0],A[:,1])@G.T; PB=klein_to_h(B[:,0],B[:,1])@G.T
    tA=logmap(PA)*L; tB=logmap(PB)*L
    for s in range(1,sub):
        t=s/sub
        M=(1-t)*A+t*B
        PM=klein_to_h(M[:,0],M[:,1])@G.T; tM=logmap(PM)*L
        tLin=(1-t)*tA+t*tB
        v1=np.stack([tM[:,0],-eye*np.ones(len(tM)),tM[:,1]],axis=-1); v2=np.stack([tLin[:,0],-eye*np.ones(len(tM)),tLin[:,1]],axis=-1)
        c=(v1*v2).sum(-1)/np.linalg.norm(v1,axis=-1)/np.linalg.norm(v2,axis=-1)
        worst=max(worst, np.degrees(np.arccos(np.clip(c,-1,1))).max())
    return worst
C=np.array([M[:,2] for M in allM]); dc=np.arccosh(np.maximum(1,C[:,2]))
bands=[(0,0.01),(0.9,1.2),(1.2,1.8),(1.8,2.5),(2.5,3.2),(3.2,4.0),(4.0,5.0),(5.0,6.0)]
for L in (0.8,1.37,2.2):
    print(f"\nL = {L} m per unit (tile edge = {1.2537*L:.2f} m). max angular error in deg (x12 = px) by tile-centre distance band and grid n:")
    print("  band(units)   #tiles " + " ".join(f"n={n:<2d}   " for n in (1,2,3,4,6,8,12,16)))
    for (lo,hi) in bands:
        idx=np.where((dc>=lo)&(dc<hi))[0]
        if len(idx)==0: continue
        samp=idx[:: max(1,len(idx)//24)]
        row=[]
        for n in (1,2,3,4,6,8,12,16):
            row.append(max(seg_err(n, allM[i], L) for i in samp))
        print(f"  {lo:3.1f}-{hi:3.1f}   {len(idx):5d}  "+" ".join(f"{e:7.3f}" for e in row))
# viewer-offset sanity: viewer standing at a vertex of its tile, error for own tile
V=view_boost(tile_boundary(p,q)[1][0])
for L in (0.8,2.2):
    print("viewer at vertex, own tile, L=",L, [round(seg_err(n, V, L),3) for n in (1,2,3,4,6,8,12,16)])
