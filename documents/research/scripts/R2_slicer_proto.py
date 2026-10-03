import numpy as np, itertools, time, math
def slice_tets(P, T, n, d, E):
    """P: (V,4) points; T: (M,4) tet indices; hyperplane n.x = d; E: (3,4) orthonormal basis of the hyperplane.
    Returns triangles in 3D slice coords (K,3,3)."""
    s = P @ n - d                      # signed distances per vertex
    tris = []
    for tet in T:
        st = s[tet]; pos = [i for i in range(4) if st[i] > 0]; neg = [i for i in range(4) if st[i] <= 0]
        if len(pos) in (0, 4): continue
        def X(i, j):
            a, b = tet[i], tet[j]; t = s[a] / (s[a] - s[b]); return E @ (P[a] + t * (P[b] - P[a]))
        if len(pos) == 1 or len(pos) == 3:
            lone, others = (pos[0], neg) if len(pos) == 1 else (neg[0], pos)
            q = [X(lone, o) for o in others]; tris.append(q)
        else:
            a, b = pos; c, e = neg
            q = [X(a, c), X(a, e), X(b, e), X(b, c)]     # cyclic order around the quad
            tris.append([q[0], q[1], q[2]]); tris.append([q[0], q[2], q[3]])
    return np.array(tris)
def area(tris):
    return 0.5 * np.linalg.norm(np.cross(tris[:,1]-tris[:,0], tris[:,2]-tris[:,0]), axis=1).sum() if len(tris) else 0.0
def basis(n):
    # orthonormal basis of the hyperplane orthogonal to n (Gram-Schmidt on the standard basis)
    B = []
    for e in np.eye(4):
        v = e - (e @ n) * n
        for b in B: v -= (v @ b) * b
        if np.linalg.norm(v) > 1e-6: B.append(v / np.linalg.norm(v))
        if len(B) == 3: break
    return np.array(B)
# ---------- Test 1: tesseract boundary = 8 cubes x 6 Kuhn tets = 48 tets
P = np.array(list(itertools.product([0,1], repeat=4)), float)
idx = {tuple(p.astype(int)): i for i, p in enumerate(P)}
T = []
for ax in range(4):
    for val in (0, 1):
        free = [a for a in range(4) if a != ax]
        for perm in itertools.permutations(free):          # 6 Kuhn tets per cube
            v = [0,0,0,0]; v[ax] = val; path = [tuple(v)]
            for a in perm:
                v = list(v); v[a] = 1; path.append(tuple(v))
            T.append([idx[p] for p in path])
T = np.array(T); print('tesseract boundary tets:', len(T))
n = np.ones(4) / 2.0; E = basis(n)
for c, name, exp in [(0.5,'tetrahedron edge c*sqrt2', math.sqrt(3)*(0.5*math.sqrt(2))**2), (2.0,'octahedron edge sqrt2', 2*math.sqrt(3)*2)]:
    tr = slice_tets(P, T, n, c/2.0, E)                     # x+y+z+w = c  <=>  n.x = c/2
    print(f'  slice x+y+z+w={c}: {len(tr)} tris, area {area(tr):.4f}, expected {exp:.4f} ({name})')
# ---------- Test 2: worldtube of a moving sphere (Lujan), prisms -> 3 tets (staircase split)
def icosphere(sub=2):
    t=(1+5**0.5)/2
    V=[(-1,t,0),(1,t,0),(-1,-t,0),(1,-t,0),(0,-1,t),(0,1,t),(0,-1,-t),(0,1,-t),(t,0,-1),(t,0,1),(-t,0,-1),(-t,0,1)]
    F=[(0,11,5),(0,5,1),(0,1,7),(0,7,10),(0,10,11),(1,5,9),(5,11,4),(11,10,2),(10,7,6),(7,1,8),(3,9,4),(3,4,2),(3,2,6),(3,6,8),(3,8,9),(4,9,5),(2,4,11),(6,2,10),(8,6,7),(9,8,1)]
    V=[np.array(v,float)/np.linalg.norm(v) for v in V]
    for _ in range(sub):
        cache={}; F2=[]
        def mid(a,b):
            k=(min(a,b),max(a,b))
            if k not in cache:
                m=V[a]+V[b]; V.append(m/np.linalg.norm(m)); cache[k]=len(V)-1
            return cache[k]
        for a,b,c in F:
            ab,bc,ca=mid(a,b),mid(b,c),mid(c,a); F2+=[(a,ab,ca),(b,bc,ab),(c,ca,bc),(ab,bc,ca)]
        F=F2
    return np.array(V),np.array(F)
V,F = icosphere(2); print('template sphere: verts',len(V),'tris',len(F))
R=10.0; b=15.0; nexp=2; tau=4.0; K=10; c=10.0      # mm, mm, -, s, phases, mm per second (time scale)
def zc(t): return -b*np.cos(np.pi*t/tau)**(2*nexp)   # Lujan, z0=0 at exhale (t=tau/2), inhale at t=0
def build(periods=3, substeps=1):
    Ks=K*substeps; P4=[]; 
    for k in range(periods*Ks+1):
        t=k*tau/Ks; pts=V*R+np.array([0,0,zc(t)]); P4.append(np.c_[pts, np.full(len(V), c*t)])
    P4=np.vstack(P4); N=len(V); T=[]
    for k in range(periods*Ks):
        lo=k*N; hi=(k+1)*N
        for tri in F:
            a,bb,cc=sorted(tri)                       # staircase split by global index => consistent quad diagonals
            T+= [[lo+a,lo+bb,lo+cc,hi+cc],[lo+a,lo+bb,hi+bb,hi+cc],[lo+a,hi+a,hi+bb,hi+cc]]
    return P4,np.array(T)
P4,T4=build(); print('worldtube: 4D verts',len(P4),'tets',len(T4),'(=tris x phases x 3 x periods)')
# snapshot slice t = const (theta=0): expect sphere area 4 pi R^2 (minus faceting)
n=np.array([0,0,0,1.0]); E=basis(n); t0=5.3
tr=slice_tets(P4,T4,n,c*t0,E); print(f'  snapshot t={t0}s: {len(tr)} tris, area {area(tr):.1f} vs sphere {4*math.pi*R*R:.1f}; z-extent {tr[:,:,2].min():.2f}..{tr[:,:,2].max():.2f} (centre {zc(t0):.2f})')
# tilted slice: scanner moving at v mm/s along +z:  z - v t = z_s - v t_s
def scan(v, zs, ts):
    nn=np.array([0,0,1.0,-v/c]); nn/=np.linalg.norm(nn); E=basis(nn)
    tr=slice_tets(P4,T4,nn,nn@np.array([0,0,zs,c*ts]),E)
    # recover anatomical z of the slice points: slice basis -> 4D -> z
    if len(tr)==0: return 0,0,0
    p0=nn*(nn@np.array([0,0,zs,c*ts])); pts4=tr.reshape(-1,3)@E+p0
    return len(tr), pts4[:,2].min(), pts4[:,2].max()
for v in (1000.0, 40.0, 20.0, 10.0):
    lens=[]
    for ts in np.linspace(4.0,8.0,41):                # scan passes z=zc at different breathing phases
        k,z0,z1=scan(v, zc(ts), ts); lens.append(z1-z0)
    print(f'  helical scan v={v:6.0f} mm/s: apparent SI length min {min(lens):5.1f} / max {max(lens):5.1f} mm (true {2*R:.0f} mm)')
# timing (pure numpy/python, NOT Burst): 
t=time.time(); tr=slice_tets(P4,T4,n,c*t0,E); dt=time.time()-t; print(f'  python slice of {len(T4)} tets: {dt*1e3:.0f} ms (python loop; Burst est. 100-300x faster)')
