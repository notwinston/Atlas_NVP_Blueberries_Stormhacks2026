import numpy as np, math
ETA=np.diag([1.0,1.0,-1.0])
def boost(dx,dy):
    d=math.hypot(dx,dy)
    if d<1e-15: return np.eye(3)
    ux,uy=dx/d,dy/d; ch,sh=math.cosh(d),math.sinh(d)
    return np.array([[1+(ch-1)*ux*ux,(ch-1)*ux*uy,sh*ux],[(ch-1)*ux*uy,1+(ch-1)*uy*uy,sh*uy],[sh*ux,sh*uy,ch]])
def rot(a):
    c,s=math.cos(a),math.sin(a); return np.array([[c,-s,0],[s,c,0],[0,0,1.0]])
O=np.array([0,0,1.0]); p,q=4,5
r_in=math.acosh(math.cos(math.pi/q)/math.sin(math.pi/p)); R_c=math.acosh(1/(math.tan(math.pi/p)*math.tan(math.pi/q)))
gens=[rot(2*math.pi*k/p)@boost(2*r_in,0)@rot(math.pi) for k in range(p)]
tiles=[np.eye(3)]; keys={(0,0)}; fr=[np.eye(3)]
while fr:
    nx=[]
    for M in fr:
        for g in gens:
            N=M@g; c=N@O
            if math.acosh(max(1.0,c[2]))>5.8: continue
            k=(round(c[0],5),round(c[1],5))
            if k in keys: continue
            keys.add(k); tiles.append(N); nx.append(N)
    fr=nx
C=np.array([M@O for M in tiles])
# positions: uniform in the tile (Klein square), exploit symmetry: one octant is enough but sample all
h=math.tanh(r_in)
rng=np.random.default_rng(7)
gaze=np.linspace(-math.pi,math.pi,1440,endpoint=False)
res={2.0:[0,0,999],2.5:[0,0,999],3.0:[0,0,999],3.5:[0,0,999]}
cnt={r:[999,0] for r in res}
for it in range(1500):
    kx,ky=rng.uniform(-h,h,2)
    kr=math.hypot(kx,ky); d0=math.atanh(kr) if kr>0 else 0.0
    px,py=((kx/kr*d0, ky/kr*d0) if kr>0 else (0.0,0.0))
    W=boost(px,py); Wi=ETA@W.T@ETA
    P=(Wi@C.T).T; d=np.arccosh(np.maximum(1.0,P[:,2])); brg=np.arctan2(P[:,1],P[:,0])
    ang=np.where(d>R_c,np.arcsin(np.minimum(1.0,math.sinh(R_c)/np.sinh(np.maximum(d,1e-9)))),math.pi)
    for r in res:
        cand=d<=r+R_c+1e-9
        idx=np.where(cand)[0]
        dif=np.abs((brg[idx][None,:]-gaze[:,None]+math.pi)%(2*math.pi)-math.pi)
        drawn=((d[idx][None,:]<=R_c)|(dif<=math.radians(70)+ang[idx][None,:])).sum(axis=1)
        res[r][0]=max(res[r][0],int(drawn.max())); res[r][1]=max(res[r][1],int(cand.sum())); res[r][2]=min(res[r][2],int(cand.sum()))
        c=int((d<=r+1e-9).sum()); cnt[r][0]=min(cnt[r][0],c); cnt[r][1]=max(cnt[r][1],c)
for r in res:
    print(f"rho={r}: centres {cnt[r][0]}-{cnt[r][1]}; touching {res[r][2]}-{res[r][1]}; drawn worst case {res[r][0]}")
