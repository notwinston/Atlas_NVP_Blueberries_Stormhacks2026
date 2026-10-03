# Trees in H2: verify ln3 bound, bisector criterion, clearance, and test the Spec's wedge layout
import numpy as np, math, itertools
from math import cos, sin, pi, cosh, sinh, acosh, log, tan, sqrt, radians, degrees
def rot(t): return np.array([[cos(t),-sin(t),0],[sin(t),cos(t),0],[0,0,1.0]])
def boost(d): return np.array([[cosh(d),0,sinh(d)],[0,1,0],[sinh(d),0,cosh(d)]])
def mink(a,b): return a[...,0]*b[...,0]+a[...,1]*b[...,1]-a[...,2]*b[...,2]
def lcross(a,b):  # Lorentz cross product: normal n with <n,a>=<n,b>=0
    return np.stack([a[...,1]*b[...,2]-a[...,2]*b[...,1], a[...,2]*b[...,0]-a[...,0]*b[...,2], -(a[...,0]*b[...,1]-a[...,1]*b[...,0])],axis=-1)
def build(children_fn, depth, root_children=None):
    # children_fn(node_depth, idx) -> list of (theta, length). returns nodes (frames), edges (i,j)
    frames=[np.eye(3)]; edges=[]; par=[-1]; dep=[0]; frontier=[0]
    for d in range(depth):
        nxt=[]
        for i in frontier:
            ch = root_children if (d==0 and root_children is not None) else children_fn(d,i)
            for (th,l) in ch:
                frames.append(frames[i]@rot(th)@boost(l)); par.append(i); dep.append(d+1); edges.append((i,len(frames)-1)); nxt.append(len(frames)-1)
        frontier=nxt
    return np.array(frames), np.array(edges), np.array(par), np.array(dep)
def analyse(frames,edges,par,label):
    P=frames[:,:,2]                                   # node points
    G=-(P[:,None,0]*P[None,:,0]+P[:,None,1]*P[None,:,1]-P[:,None,2]*P[None,:,2])
    D=np.arccosh(np.maximum(G,1.0)); n=len(P)
    np.fill_diagonal(D,np.inf)
    adj=np.zeros((n,n),bool); adj[edges[:,0],edges[:,1]]=True; adj|=adj.T
    Dn=np.where(adj,np.inf,D)
    min_nonadj=Dn.min()
    # edge crossings: segments (A,B),(C,D) cross iff strictly opposite sides both ways
    A=P[edges[:,0]]; B=P[edges[:,1]]; N=lcross(A,B); N/=np.sqrt(np.abs(mink(N,N)))[:,None]
    sA=(N[:,None,:]*np.array([1,1,-1])*A[None,:,:]).sum(-1)   # <N_i, A_j>
    sB=(N[:,None,:]*np.array([1,1,-1])*B[None,:,:]).sum(-1)
    opp=(sA*sB< -1e-9)
    cross=opp&opp.T
    share=(edges[:,None,0]==edges[None,:,0])|(edges[:,None,0]==edges[None,:,1])|(edges[:,None,1]==edges[None,:,0])|(edges[:,None,1]==edges[None,:,1])
    cross&=~share
    ncross=int(cross.sum()//2)
    # clearance between non-adjacent edges approx via sampled points (5 per edge)
    ts=np.linspace(0,1,5)
    S=[]
    for t in ts:
        X=(1-t)*A+t*B; X/=np.sqrt(-mink(X,X))[:,None]; S.append(X)
    S=np.concatenate(S); eid=np.tile(np.arange(len(edges)),len(ts))
    GG=-(S[:,None,0]*S[None,:,0]+S[:,None,1]*S[None,:,1]-S[:,None,2]*S[None,:,2])
    DD=np.arccosh(np.maximum(GG,1.0))
    sh=share[eid][:,eid]
    DD=np.where(sh,np.inf,DD)
    print(f"{label:62s} nodes={n:5d} min node-node (non-adjacent) = {min_nonadj:6.3f}  edge crossings = {ncross:4d}  min clearance between non-adjacent edges ~ {DD.min():6.3f}")
    return min_nonadj,ncross,DD.min()

ln3=log(3)
print("ln 3 =",ln3," 2*acosh(1/sin(pi/3)) =",2*acosh(1/sin(pi/3)), " -2 ln tan(pi/6) =",-2*log(tan(pi/6)))
print("T-junction bound 2*acosh(1/sin(pi/4)) =",2*acosh(sqrt(2)), " -2 ln tan(pi/8) =",-2*log(tan(pi/8)))
for D in (3,4,5,6): print(f"  degree {D} equal cones: l_min = -2 ln tan(pi/(2D)) = {-2*log(tan(pi/(2*D))):.4f}")
depth=8
for l in (log(2), 0.9, ln3, 1.2, 1.5):
    fr,ed,pa,de=build(lambda d,i:[(pi/3,l),(-pi/3,l)], depth, root_children=[(0,l),(2*pi/3,l),(-2*pi/3,l)])
    analyse(fr,ed,pa,f"symmetric 120deg trivalent, l={l:.4f} ({'<' if l<ln3-1e-9 else '>='} ln3)")
for l in (1.0613, 1.5, 1.7627, 2.1226):
    fr,ed,pa,de=build(lambda d,i:[(pi/2,l),(-pi/2,l)], depth)
    analyse(fr,ed,pa,f"T-junction (+-90deg), l={l:.4f} (bound 1.7627)")
# Spec 6.7 wedge layout: theta = -wedge/2 + wedge*(i+0.5)/k ; child.wedge=min(wedge/k*SHRINK, pi/2); len = 1.5 m / R=2.2
def spec_layout(depth, root_wedge, SHRINK, l):
    frames=[np.eye(3)]; edges=[]; wed=[root_wedge]; frontier=[0]; par=[-1]
    for d in range(depth):
        nxt=[]
        for i in frontier:
            k=2
            for c in range(k):
                th=-wed[i]/2+wed[i]*(c+0.5)/k
                frames.append(frames[i]@rot(th)@boost(l)); wed.append(min(wed[i]/k*SHRINK, pi/2)); par.append(i); edges.append((i,len(frames)-1)); nxt.append(len(frames)-1)
        frontier=nxt
    return np.array(frames),np.array(edges),np.array(par)
for (rw,sh,l) in [(pi,1.0,1.5/2.2),(pi,2.0,1.5/2.2),(2*pi,2.0,1.5/2.2),(pi,2.0,2.0/2.2),(pi,2.0,1.5/1.0)]:
    fr,ed,pa=spec_layout(depth,rw,sh,l)
    analyse(fr,ed,pa,f"SPEC wedge layout root_wedge={degrees(rw):.0f} SHRINK={sh} l={l:.3f}u")
# asymmetric (Horsfield-like): major child deviates 20deg, minor 55deg (angle between = 75deg); bisector criterion needs cosh(l/2) sin(theta_min/2) >= 1
th1,th2=radians(20),radians(55)
lmin=2*acosh(1/sin((th1+th2)/2))
print("asymmetric angles 20/55: theta_min = 75deg -> l_min =",lmin)
for l in (1.2, lmin, lmin*1.15):
    fr,ed,pa,de=build(lambda d,i:[(th1,l),(-th2,l)], depth)
    analyse(fr,ed,pa,f"asymmetric 20/55deg, l={l:.3f} (bound {lmin:.3f})")
