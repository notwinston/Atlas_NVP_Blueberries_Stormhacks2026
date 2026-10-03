# Budget numbers: tiles within hyperbolic radius d, frustum-culled counts, angular sizes
import math, time, collections, random
from math import cos, sin, pi, cosh, sinh, acosh, asinh, atanh, sqrt, tan, atan2, degrees, radians, asin, atan, tanh
import numpy as np

def gen(p,q,depth):
    r=acosh(cos(pi/q)/sin(pi/p)); ch,sh=cosh(2*r),sinh(2*r)
    T=np.array([[ch,0,sh],[0,1,0],[sh,0,ch]])
    def rot(t): return np.array([[cos(t),-sin(t),0],[sin(t),cos(t),0],[0,0,1.0]])
    steps=[rot(2*pi*k/p)@T@rot(pi) for k in range(p)]
    def key(M): return (round(M[0,2]*1e3), round(M[1,2]*1e3), round(M[2,2]*1e3))
    seen={}
    def find(M):
        k=key(M)
        for dx in (-1,0,1):
            for dy in (-1,0,1):
                for dz in (-1,0,1):
                    if (k[0]+dx,k[1]+dy,k[2]+dz) in seen: return True
        return False
    I=np.eye(3); seen[key(I)]=1; frontier=[I]; counts=[1]; allM=[I]
    for d in range(1,depth+1):
        nxt=[]
        for M in frontier:
            for S in steps:
                N=M@S
                if not find(N):
                    seen[key(N)]=1; nxt.append(N); allM.append(N)
        counts.append(len(nxt)); frontier=nxt
    return counts, allM

def mink(a,b): return a[0]*b[0]+a[1]*b[1]-a[2]*b[2]
def hdist(a,b): return acosh(max(1.0,-mink(a,b)))

def tile_boundary(p,q,nsamp=6):
    # boundary sample points of root tile in hyperboloid coords (Klein square straight edges)
    R=acosh(1/(tan(pi/p)*tan(pi/q)))
    verts=[np.array([sinh(R)*cos(pi/p+2*pi*k/p), sinh(R)*sin(pi/p+2*pi*k/p), cosh(R)]) for k in range(p)]
    pts=[]
    for k in range(p):
        a=verts[k]; b=verts[(k+1)%p]
        for i in range(nsamp):
            t=i/nsamp
            v=(1-t)*a+t*b; v=v/sqrt(-mink(v,v)); pts.append(v)   # geodesic = projective line
    return np.array(pts), verts

def view_boost(pos):
    # isometry taking pos -> origin (0,0,1): rotate to x axis, boost back
    x,y,t=pos
    th=atan2(y,x); d=acosh(max(1.0,t))
    Rz=np.array([[cos(-th),-sin(-th),0],[sin(-th),cos(-th),0],[0,0,1.0]])
    B=np.array([[cosh(d),0,-sinh(d)],[0,1,0],[-sinh(d),0,cosh(d)]])
    return B@Rz

results={}
for (p,q,depth) in [(4,5,11),(4,6,10)]:
    t0=time.time(); counts,allM=gen(p,q,depth); 
    a_=acosh(cos(pi/p)/sin(pi/q)); r_=acosh(cos(pi/q)/sin(pi/p)); R_=acosh(1/(tan(pi/p)*tan(pi/q)))
    area=pi*(p-2)-p*2*pi/q
    print(f"\n=== {{{p},{q}}}: generated {len(allM)} tiles to graph depth {depth} in {time.time()-t0:.1f}s; coordination {counts}")
    bpts,verts=tile_boundary(p,q)
    C=np.array([M[:,2] for M in allM])               # centres
    # viewer positions: centre, edge midpoint, vertex
    viewers={'centre':np.array([0,0,1.0]), 'edge-mid':np.array([sinh(r_),0,cosh(r_)]), 'vertex':verts[0]}
    allB=np.einsum('nij,kj->nki', np.array(allM), bpts)   # (n, k, 3) boundary points of each tile
    for vname,vp in viewers.items():
        V=view_boost(vp)
        Cv=C@V.T
        dc=np.arccosh(np.maximum(1.0,Cv[:,2]))
        Bv=allB@V.T
        dB=np.arccosh(np.maximum(1.0,Bv[:,:,2]))
        dmin=np.minimum(dB.min(axis=1), dc)             # nearest point of tile (boundary samples or centre)
        ang_c=np.arctan2(Cv[:,1],Cv[:,0])
        angB=np.arctan2(Bv[:,:,1],Bv[:,:,0])
        row=[]
        for d in (2,3,4,5,6):
            n_centre=int((dc<=d).sum()); n_touch=int((dmin<=d).sum())
            est_area=2*pi*(cosh(d)-1)/area
            # frustum cull: horizontal FOV 100 deg total, average & max over 72 headings; tile visible if any boundary sample/centre within +-50deg, or viewer inside tile (dmin==0-ish)
            idx=np.where(dmin<=d)[0]
            vis=[]
            for h in np.linspace(0,2*pi,72,endpoint=False):
                rel=np.angle(np.exp(1j*(angB[idx]-h)))
                relc=np.angle(np.exp(1j*(ang_c[idx]-h)))
                inside=(np.abs(rel)<=radians(50)).any(axis=1) | (np.abs(relc)<=radians(50)) | (dc[idx]<R_)
                vis.append(int(inside.sum()))
            row.append((d,n_centre,n_touch,est_area,np.mean(vis),max(vis)))
        results[(p,q,vname)]=row
        print(f" viewer at {vname}:  d | centres<=d | tiles touching disc | area est 2pi(cosh d-1)/A | in 100deg FOV mean | max")
        for (d,nc,nt,ea,vm,vx) in row: print(f"   {d} | {nc:5d} | {nt:5d} | {ea:8.1f} | {vm:7.1f} | {vx:4d}")
    # check depth coverage: max centre distance covered fully? min distance of deepest-level tiles
    lastlevel=C[-counts[-1]:]
    print(" min centre distance of deepest BFS level:", np.arccosh(lastlevel[:,2]).min(), "(disc radius must stay below this minus circumradius for counts to be complete)")
    # graph distance needed to cover hyperbolic radius d (touching): 
    lev=np.concatenate([[i]*c for i,c in enumerate(counts)])
    V=np.eye(3); dc=np.arccosh(np.maximum(1.0,C[:,2])); dB=np.arccosh(np.maximum(1.0,allB[:,:,2])).min(axis=1); dmin=np.minimum(dB,dc)
    for d in (2,3,4,5,6):
        print(f"   radius {d}: max graph depth among touching tiles = {lev[dmin<=d].max()} ; max graph depth among centres<=d = {lev[dc<=d].max() if (dc<=d).any() else 0}")
    # angular size table
    print(" angular size of a tile seen from distance d (centre distance): full-width angle = 2*asin(sinh(rho)/sinh(d)), rho=circumradius / inradius")
    for d in (1.5,2,3,4,5,6,7):
        wc=2*degrees(asin(min(1,sinh(R_)/sinh(d)))); wi=2*degrees(asin(min(1,sinh(r_)/sinh(d))))
        print(f"   d={d}: circ {wc:6.2f} deg ({wc*12:6.1f} px @12px/deg), in {wi:6.2f} deg ; vertical angle of 3m wall at L=1.0m: {degrees(atan(3.0/(d*1.0))):5.1f} deg, L=2.2m: {degrees(atan(3.0/(d*2.2))):5.1f} deg")
