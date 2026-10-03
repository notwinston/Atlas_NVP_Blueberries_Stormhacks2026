import numpy as np, itertools, random
from math import *
def boost(dx, dz):
    r = hypot(dx, dz)
    if r < 1e-15: return np.eye(3)
    ux, uz = dx/r, dz/r; ch, sh = cosh(r), sinh(r)
    return np.array([[1+(ch-1)*ux*ux, (ch-1)*ux*uz, sh*ux],[(ch-1)*ux*uz, 1+(ch-1)*uz*uz, sh*uz],[sh*ux, sh*uz, ch]])
def rot(a):
    c,s = cos(a), sin(a); return np.array([[c,-s,0],[s,c,0],[0,0,1.0]])
eta = np.diag([1,1,-1.0])
phi=(1+sqrt(5))/2
m5 = asinh(sqrt(cos(2*pi/5))); m6 = asinh(sqrt(cos(2*pi/6)))
print("{4,5}: cosh(2m)=%.9f (phi=%.9f)  sinh(2m)=%.9f (sqrt(phi)=%.9f)"%(cosh(2*m5),phi,sinh(2*m5),sqrt(phi)))
print("{4,6}: cosh(2m)=%.9f  sinh(2m)=%.9f (sqrt3=%.9f)"%(cosh(2*m6),sinh(2*m6),sqrt(3)))

# tile step generators: G_i = Rot(i*90) * BoostX(2m) * Rot(180) * Rot(-i*90)?  check vertex relation
def gens(m):
    return [rot(i*pi/2)@boost(2*m,0)@rot(pi)@rot(-i*pi/2) for i in range(4)]
# frame of neighbour across edge i (0=+x,1=+z,2=-x,3=-z) expressed with same axis orientation: use pure boosts instead
def step(m,i): return boost(2*m*cos(i*pi/2), 2*m*sin(i*pi/2))
for q,m in ((5,m5),(6,m6)):
    # walking around a vertex: step +x, then +z(in new frame), -x, -z ... using pure boosts in parallel-transported frame = rolling a square
    C=np.eye(3)
    for k in range(q):
        C = C@step(m,k%4)
    print("q=%d: after q centre-to-centre steps with 90deg turns: pos="%q, np.round(C@np.array([0,0,1.0]),9), " frame rot(deg)=%.3f"%degrees(atan2(C[1,0],C[0,0])))

# BFS the {4,5} tiling: tile frames via pure-boost steps (frames differ by rotations but centres are right)
def bfs_centres(m, maxd, origin=np.eye(3)):
    seen=[]; frontier=[np.eye(3)]; cents=[np.array([0,0,1.0])]
    allM=[np.eye(3)]
    while frontier:
        nf=[]
        for M in frontier:
            for i in range(4):
                N = M@step(m,i)
                c = N@np.array([0,0,1.0])
                if acosh(max(1,c[2]))>maxd+2*m: continue
                dup=False
                for c2 in cents:
                    if -(c[0]*c2[0]+c[1]*c2[1]-c[2]*c2[2]) < cosh(0.2): dup=True;break
                if not dup:
                    cents.append(c); nf.append(N)
        frontier=nf
    return cents
for q,m in ((5,m5),(6,m6)):
    cents=bfs_centres(m,3.6)
    ds=np.array([acosh(max(1,c[2])) for c in cents])
    print("q=%d tiles with centre within d: "%q + "  ".join("d=%.1f:%d"%(d,(ds<=d).sum()) for d in (1.1,1.5,2,2.5,3,3.5)))

# motion parallax check: bearing rate to a point at distance d when moving perpendicular
def logmap(p):
    s=hypot(p[0],p[1]); return np.array([p[0],p[1]])/s*asinh(s)
for d in (0.5,1,2,4):
    P = boost(d,0)@np.array([0,0,1.0]); eps=1e-5
    Cinv = np.linalg.inv(boost(0,eps))
    t = logmap(Cinv@P); bearing = atan2(t[1],t[0])
    print("d=%.1f: bearing rate %.4f  coth(d)=%.4f  (Euclid 1/d=%.4f)"%(d, -bearing/eps, 1/tanh(d), 1/d))

# jitter drift: standing still, white noise 1mm rms per axis per frame at 60 Hz, 60 s, L=2.827 ; integrate deltas of noisy positions
rng=np.random.default_rng(1)
for L in (2.827,0.94):
    res=[]
    for trial in range(20):
        N=3600; pos = rng.normal(0,0.001,(N,2))   # measured positions = true(0) + noise
        C=np.eye(3)
        for k in range(1,N):
            d=(pos[k]-pos[k-1])/L
            C=C@boost(d[0],d[1])
        p=C@np.array([0,0,1.0]); res.append((acosh(max(1,p[2]))*L*1000, abs(degrees(atan2(C[1,0],C[0,0])))))
    res=np.array(res); print("L=%.3f standing 60 s, 1 mm rms jitter: virtual pos drift mean %.2f mm, rot drift mean %.4f deg"%(L,res[:,0].mean(),res[:,1].mean()))

# Option 2: enumerate straight-line cell sequences in an n x m grid from user cell; count instances; check gate-side bijection
def cell_seq(p,q):
    # cells crossed by segment p->q on unit grid (generic position)
    x0,z0=p; x1,z1=q
    ts=[0,1]
    for X in range(int(floor(min(x0,x1)))+1, int(floor(max(x0,x1)))+1):
        ts.append((X-x0)/(x1-x0))
    for Z in range(int(floor(min(z0,z1)))+1, int(floor(max(z0,z1)))+1):
        ts.append((Z-z0)/(z1-z0))
    ts=sorted(ts); seq=[]
    for a,b in zip(ts[:-1],ts[1:]):
        t=(a+b)/2; c=(int(floor(x0+t*(x1-x0))),int(floor(z0+t*(z1-z0))))
        if not seq or seq[-1]!=c: seq.append(c)
    return tuple(seq)
def instances(n,m,U,rng_cheb=99,samples=200000):
    random.seed(0); inst={}
    for _ in range(samples):
        p=(U[0]+random.random(),U[1]+random.random()); q=(random.random()*n, random.random()*m)
        s=cell_seq(p,q); c=s[-1]
        if max(abs(c[0]-U[0]),abs(c[1]-U[1]))>rng_cheb: continue
        inst.setdefault(c,set()).add(s)
    return inst
for (n,m,U,r) in [(3,3,(0,0),99),(3,3,(1,1),99),(3,3,(1,0),99),(4,5,(0,0),99),(4,5,(0,0),2),(4,5,(1,2),2),(6,8,(2,3),2),(6,8,(2,3),3)]:
    inst=instances(n,m,U,r)
    tot=sum(len(v) for v in inst.values()); mx=max(len(v) for v in inst.values())
    print("grid %dx%d user %s range %s: cells %d, tile instances %d, max sheets on one cell %d"%(n,m,U,r,len(inst),tot,mx))

# gate test: sequence determined by sides of interior lattice vertices in bbox?
random.seed(2); mp={}; ok=True
U=(0,0)
for _ in range(200000):
    p=(random.random(),random.random()); q=(random.random()*3,random.random()*3)
    s=cell_seq(p,q); c=s[-1]
    verts=[(X,Z) for X in range(1,c[0]+1) for Z in range(1,c[1]+1)]
    sides=tuple(1 if ((v[0]-p[0])*(q[1]-p[1])-(v[1]-p[1])*(q[0]-p[0]))>0 else -1 for v in verts)
    key=(c,sides)
    if key in mp and mp[key]!=s: ok=False
    mp[key]=s
print("gate-side pattern determines cell sequence (3x3 from corner):",ok, " distinct patterns:",len(mp))
print("L_eff of polyhedral {4,5} with cell g: g/sqrt(2pi/5) = %.3f g ; {4,6}: %.3f g"%(1/sqrt(2*pi/5),1/sqrt(2*pi/3)))
