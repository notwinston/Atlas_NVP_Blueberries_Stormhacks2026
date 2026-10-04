"""Worst-case number of tiles drawn under the draw rule of section 8.5, dense sampling (counts only).
Usage: python3 draw_count_dense.py <rho>"""
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
RHO=float(sys.argv[1]); HM,VM=math.radians(62.0),math.radians(52.0); H=1.6
rng=np.random.default_rng(7); hk=math.tanh(r_in)
yaws=np.linspace(0,2*math.pi,720,endpoint=False)
PITCH=(-20,0,10,25,40,55,70,80,90)
worst={pd:0 for pd in PITCH}
for L in (1.41,1.885,2.83):
    for it in range(600):
        kx,ky=rng.uniform(-hk,hk,2); kr=math.hypot(kx,ky); dd=math.atanh(kr) if kr>0 else 0
        W=boost(kx/kr*dd,ky/kr*dd) if kr>0 else np.eye(3)
        P=((ETA@W.T@ETA)@C.T).T
        d=np.arccosh(np.maximum(1.0,P[:,2])); b=np.arctan2(P[:,1],P[:,0])
        idx=np.where(d<=RHO+Rc+1e-9)[0]; dc=d[idx]; bc=b[idx]
        cx=L*dc*np.cos(bc); cz=L*dc*np.sin(bc); r=L*Rc
        near=dc<=Rc
        cy,sy=np.cos(yaws)[:,None],np.sin(yaws)[:,None]            # yaw × tile
        f_h=cx[None,:]*cy+cz[None,:]*sy                             # component along the horizontal gaze direction
        r_h=-cx[None,:]*sy+cz[None,:]*cy                            # component to the right
        for pd in PITCH:
            th=math.radians(pd); ct,st=math.cos(th),math.sin(th)
            # forward = (cy*ct, -st, sy*ct); right = (-sy, 0, cy); up = right x forward
            fwd_dot=f_h*ct+H*st                                     # centre (x, -H, z) . forward
            up_dot=f_h*st-H*ct                                      # centre . up, with up = (cy*st, ct, sy*st)
            ok=np.ones_like(f_h,bool)
            for sx in (1,-1):
                # inward normal n = cos(HM)*(-sx)*right + sin(HM)*fwd ; floor part of n has length hypot(cos HM, sin HM * ct)
                val=math.cos(HM)*(-sx)*r_h+math.sin(HM)*fwd_dot
                ok&=val+r*math.hypot(math.cos(HM),math.sin(HM)*ct)>=0
            for sy_ in (1,-1):
                # n = cos(VM)*(-sy)*up + sin(VM)*fwd ; its floor part: |cos(VM)*(-sy)*st + sin(VM)*ct|
                val=math.cos(VM)*(-sy_)*up_dot+math.sin(VM)*fwd_dot
                ok&=val+r*abs(math.cos(VM)*(-sy_)*st+math.sin(VM)*ct)>=0
            ok|=near[None,:]
            worst[pd]=max(worst[pd],int(ok.sum(axis=1).max()))
print(f"rho {RHO}: worst drawn by pitch-down angle {worst}; level {worst[0]}; any pitch {max(worst.values())}")
