import numpy as np, math
from math import cos, sin, pi, cosh, sinh, acosh, tan, radians
exec(open('r3_d.py').read().split("results={}")[0])
def tri(n, ceiling=True): return (4 if ceiling else 2)*n*n + 8*n
for (p,q,depth,bands) in [(4,5,11,[(1.8,12),(3.2,6),(4.2,4),(5.2,2),(99,1)]),(4,6,10,[(2.4,12),(3.6,6),(4.6,4),(5.6,2),(99,1)])]:
    counts,allM=gen(p,q,depth)
    r_=acosh(cos(pi/q)/sin(pi/p)); R_=acosh(1/(tan(pi/p)*tan(pi/q)))
    bpts,verts=tile_boundary(p,q)
    C=np.array([M[:,2] for M in allM]); allB=np.einsum('nij,kj->nki', np.array(allM), bpts)
    print(f"\n{{{p},{q}}} LOD bands (centre distance < x -> grid n): {bands}; tris/tile (floor+ceiling+4 walls) = {[tri(n) for _,n in bands]}")
    print(" cull d | tiles drawn (no FOV cull) | per-LOD counts | tris/eye (no cull) | tiles in 100deg FOV (worst of 3 viewer pos) | tris/eye FOV-culled | draws/eye SRP path | draws/eye instanced (LOD meshes)")
    for d in (2,3,4,5,6):
        worst=None
        for vp in (np.array([0,0,1.0]), np.array([sinh(r_),0,cosh(r_)]), verts[0]):
            V=view_boost(vp); Cv=C@V.T; dc=np.arccosh(np.maximum(1.0,Cv[:,2])); Bv=allB@V.T
            dmin=np.minimum(np.arccosh(np.maximum(1.0,Bv[:,:,2])).min(axis=1),dc)
            idx=np.where(dmin<=d)[0]
            lod=np.zeros(len(idx),int); t=np.zeros(len(idx))
            for k,i in enumerate(idx):
                for li,(lim,n) in enumerate(bands):
                    if dc[i]<lim: lod[k]=li; t[k]=tri(n); break
            angB=np.arctan2(Bv[idx][:,:,1],Bv[idx][:,:,0]); angc=np.arctan2(Cv[idx,1],Cv[idx,0])
            best=(0,0)
            for h in np.linspace(0,2*pi,72,endpoint=False):
                ins=(np.abs(np.angle(np.exp(1j*(angB-h))))<=radians(50)).any(axis=1)|(np.abs(np.angle(np.exp(1j*(angc-h))))<=radians(50))|(dc[idx]<R_)
                if ins.sum()>best[0]: best=(int(ins.sum()), int(t[ins].sum()))
            rec=(len(idx), [int((lod==li).sum()) for li in range(len(bands))], int(t.sum()), best[0], best[1])
            if worst is None or rec[3]>worst[3]: worst=rec
        nl=sum(1 for c in worst[1] if c>0)
        print(f"   {d}    | {worst[0]:5d} | {worst[1]} | {worst[2]:7d} | {worst[3]:4d} | {worst[4]:7d} | {worst[3]:4d} | {nl}")
