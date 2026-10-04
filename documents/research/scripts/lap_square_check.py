import numpy as np, math
ETA=np.diag([1.0,1.0,-1.0])
def boost(dx,dy):
    d=math.hypot(dx,dy)
    if d<1e-15: return np.eye(3)
    ux,uy=dx/d,dy/d; ch,sh=math.cosh(d),math.sinh(d)
    return np.array([[1+(ch-1)*ux*ux,(ch-1)*ux*uy,sh*ux],[(ch-1)*ux*uy,1+(ch-1)*uy*uy,sh*uy],[sh*ux,sh*uy,ch]])
def rot(a):
    c,s=math.cos(a),math.sin(a); return np.array([[c,-s,0],[s,c,0],[0,0,1.0]])
def inv(M): return ETA@M.T@ETA
O=np.array([0,0,1.0])
s_m=2.0; k=1.0612750619050357; L=s_m/k
# walker pose as an isometry F (frame): F@O = position, F's x-axis = heading
# start at the lap corner (-1,-1) m in tile zero's log chart, heading +x
start=np.array([-1.0,-1.0])/L
F0=boost(*start)          # parallel transport from the centre: axes stay 'chart-aligned' at first order
# one side: move s along heading, then turn left 90 degrees
step=boost(k,0)@rot(math.pi/2)
F=F0.copy(); corners=[F0@O]
for i in range(4):
    F=F@step; corners.append(F@O)
def chart(P):
    d=math.acosh(max(1.0,P[2])); 
    if d<1e-12: return np.zeros(2)
    return np.array([P[0],P[1]])/math.sinh(d)*d*L
print("lap corners in tile-zero chart (m):",[tuple(np.round(chart(c),2)) for c in corners])
g=F@inv(F0)               # isometry of the plane that carries the start pose to the pose after one lap
disp=math.acosh((inv(F0)@F@O)[2])*L
print("displacement after one lap: %.4f m (s = %.1f)"%(disp,s_m))
loc=inv(F0)@F             # one lap in the walker's own frame
print("frame turn after one lap (deg):", round(math.degrees(math.atan2(loc[1,0],loc[0,0])) ,3))
g5=np.linalg.matrix_power(loc,5); print("five laps = identity, error:",np.abs(g5-np.eye(3)).max())
# fixed point of g (elliptic): eigenvector with eigenvalue 1, timelike
w,v=np.linalg.eig(g); i=np.argmin(np.abs(w-1)); fp=np.real(v[:,i]); fp=fp/ math.sqrt(abs(fp@ETA@fp)); fp=fp*np.sign(fp[2])
dc=math.acosh(fp[2]); print("rotation centre: %.4f L = %.3f m from the room centre; chart position (m): %s"%(dc,dc*L,np.round(chart(fp),3)))
ang=math.degrees(math.acos((np.trace(g)-1)/2)); print("rotation angle about it (deg):",round(ang,3))
Rc=0.8425; post=np.array([1,1])/math.sqrt(2)*Rc*L
print("tile-zero posts at (±%.3f, ±%.3f) m; lap start to nearest post: %.3f m"%(post[0],post[1],np.linalg.norm(np.array([-1,-1])+post)))
pv=boost(-Rc/math.sqrt(2),-Rc/math.sqrt(2))@O
print("rotation centre to nearest post: %.3f L"%min(math.acosh(max(1,-(fp@ETA@(boost(a*Rc/math.sqrt(2),b*Rc/math.sqrt(2))@O)))) for a in (-1,1) for b in (-1,1)))
print("distance from rotation centre to each lap corner (L):",[round(math.acosh(max(1,-(fp@ETA@c))),4) for c in corners])
# glued cell rule
import numpy as np
W=np.arange(1.40,7.4001,0.001); n=np.maximum(1,np.round(W/1.5)); c=W/n
print("glued cell range over 1.4-7.4 m: %.3f - %.3f"%(c.min(),c.max()))
print("2.4/3 < 0.8 ->",2.4/3<0.8,"; with 0.75 threshold ->",2.4/3<0.75)
# view-size ratio in H2: (d/L)/sinh(d/L)
for d in (5.0,5.65,6.0): print("apparent size ratio at %.2f m (L=1.885): %.3f"%(d,(d/L)/math.sinh(d/L)))
