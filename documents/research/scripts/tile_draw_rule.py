"""Check the tile draw rule in three dimensions, for any head pitch.
Rule: a tile is drawn when its centre is within rho + Rc of the walker, and its bounding disc on the floor is not
      wholly outside one of the four side planes of the view frustum widened by a margin
      (a disc of radius r centred at c is outside a plane with inward normal n when n.c + r*|n_floor| < 0).
Checks: (1) coverage: every floor point inside the true view frustum and nearer than rho lies in a drawn tile;
        (2) the worst-case number of drawn tiles."""
import numpy as np, math, sys
ETA=np.diag([1.0,1.0,-1.0])
def boost(dx,dy):
    d=math.hypot(dx,dy)
    if d<1e-15: return np.eye(3)
    ux,uy=dx/d,dy/d; ch,sh=math.cosh(d),math.sinh(d)
    return np.array([[1+(ch-1)*ux*ux,(ch-1)*ux*uy,sh*ux],[(ch-1)*ux*uy,1+(ch-1)*uy*uy,sh*uy],[sh*ux,sh*uy,ch]])
def rot(a):
    c,s=math.cos(a),math.sin(a); return np.array([[c,-s,0],[s,c,0],[0,0,1.0]])
O=np.array([0,0,1.0]); p,q=4,5
r_in=math.acosh(math.cos(math.pi/q)/math.sin(math.pi/p)); Rc=math.acosh(1/(math.tan(math.pi/p)*math.tan(math.pi/q)))
gens=[rot(2*math.pi*k/p)@boost(2*r_in,0)@rot(math.pi) for k in range(p)]
tiles=[np.eye(3)]; keys={(0,0)}; fr=[np.eye(3)]
while fr:
    nx=[]
    for M in fr:
        for g in gens:
            N=M@g; c=N@O
            if math.acosh(max(1.0,c[2]))>6.0: continue
            k=(round(c[0],5),round(c[1],5))
            if k in keys: continue
            keys.add(k); tiles.append(N); nx.append(N)
    fr=nx
C=np.array([M@O for M in tiles])
HM=math.radians(float(sys.argv[1]) if len(sys.argv)>1 else 70.0)
VM=math.radians(float(sys.argv[3]) if len(sys.argv)>3 else 58.0)
RHO=float(sys.argv[2]) if len(sys.argv)>2 else 3.0
H=1.6; HF,VF=math.radians(50.0),math.radians(47.7)
rng=np.random.default_rng(3); hk=math.tanh(r_in)
gx=np.tan(np.linspace(-HF,HF,13)); gy=np.tan(np.linspace(-VF,VF,13))
RX,RY=np.meshgrid(gx,gy); rays=np.stack([RX.ravel(),RY.ravel(),np.ones(RX.size)],1)     # camera space: x right, y up, z forward
worst=0; viol=0; nrays=0; worst_by_pitch={}
for L in (1.41,1.885,2.83):
    for it in range(40):
        kx,ky=rng.uniform(-hk,hk,2); kr=math.hypot(kx,ky); dd=math.atanh(kr) if kr>0 else 0
        W=boost(kx/kr*dd,ky/kr*dd) if kr>0 else np.eye(3)
        P=((ETA@W.T@ETA)@C.T).T
        d=np.arccosh(np.maximum(1.0,P[:,2])); b=np.arctan2(P[:,1],P[:,0])
        cand=d<=RHO+Rc+1e-9
        idx=np.where(cand)[0]; dc=d[idx]; bc=b[idx]; Pc=P[idx]
        cen=np.stack([L*dc*np.cos(bc), -H*np.ones(len(idx)), L*dc*np.sin(bc)],1)          # world: x, y up, z ; head at origin
        dist3=np.linalg.norm(cen,axis=1)
        with np.errstate(invalid="ignore",divide="ignore"):
            half=np.where(dc>Rc,np.arcsin(np.minimum(1.0,math.sinh(Rc)/np.sinh(np.maximum(dc,1e-9)))),math.pi/2)
        r_chart=L*Rc*np.ones(len(idx))      # the log chart never stretches lengths, so the tile lies inside this disc
        for yaw in np.linspace(0,2*math.pi,16,endpoint=False):
            for pitch_deg in (-20,0,10,25,40,55,70,90):
                th=math.radians(pitch_deg)
                fwd=np.array([math.cos(yaw)*math.cos(th), -math.sin(th), math.sin(yaw)*math.cos(th)])
                right=np.array([-math.sin(yaw),0.0,math.cos(yaw)])
                up=np.cross(right,fwd); up=up/np.linalg.norm(up)
                drawn=np.ones(len(idx),bool)
                for sx,ang in ((1,HM),(-1,HM)):                       # left and right planes
                    nrm=math.cos(ang)*(-sx)*right+math.sin(ang)*fwd      # inward normal
                    drawn&=(cen@nrm)+r_chart*np.hypot(nrm[0],nrm[2])>=0
                for sy,ang in ((1,VM),(-1,VM)):                       # top and bottom planes
                    nrm=math.cos(ang)*(-sy)*up+math.sin(ang)*fwd
                    drawn&=(cen@nrm)+r_chart*np.hypot(nrm[0],nrm[2])>=0
                drawn|=dc<=Rc
                n=int(drawn.sum()); worst=max(worst,n); worst_by_pitch[pitch_deg]=max(worst_by_pitch.get(pitch_deg,0),n)
                # coverage: rays of the true frustum that hit the floor within rho
                dirs=rays[:,0:1]*right[None,:]+rays[:,1:2]*up[None,:]+rays[:,2:3]*fwd[None,:]
                down=dirs[:,1]<-1e-6
                t=-H/dirs[down,1]; hit=dirs[down]*t[:,None]
                rr=np.hypot(hit[:,0],hit[:,2])/L
                ok=rr<=RHO
                if not ok.any(): continue
                hit=hit[ok]; rr=rr[ok]; aa=np.arctan2(hit[:,2],hit[:,0])
                X=np.stack([np.sinh(rr)*np.cos(aa),np.sinh(rr)*np.sin(aa),np.cosh(rr)],1)
                G=X[:,2:3]*Pc[None,:,2]-X[:,0:1]*Pc[None,:,0]-X[:,1:2]*Pc[None,:,1]         # cosh of distance to each candidate centre
                owner=np.argmin(G,axis=1)
                nrays+=len(owner); viol+=int((~drawn[owner]).sum())
print(f"frustum half-angles {math.degrees(HM):.0f} x {math.degrees(VM):.0f} deg, rho {RHO}: worst drawn {worst}; coverage violations {viol} of {nrays} floor rays")
print("worst drawn by pitch-down angle:",worst_by_pitch)
