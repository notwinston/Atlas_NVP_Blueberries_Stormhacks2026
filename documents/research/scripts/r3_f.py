# Binary-tree-in-{4,5} snapping rules: brute-force injectivity with exact Z[phi] ids
import sys, time, collections
from exact45 import *
F=T_; Lm=Rk[1].mul(T_); Rm=Rk[3].mul(T_)
MOVE={'F':F,'L':Lm,'R':Rm}
def test_rule(name, children_fn, init_state, depth, start=I_):
    # children_fn(state) -> list of (move_string, new_state); a tree node = end of move_string
    seen={start.centre():''}; allcells={start.centre()}   # node tiles; allcells includes intermediate corridor tiles
    frontier=[(start,init_state,'')]; ok=True; first=None; n_nodes=1
    for d in range(1,depth+1):
        nxt=[]
        for (M,st,w) in frontier:
            for (mv,ns) in children_fn(st):
                N=M; ww=w
                for ch in mv:
                    N=N.mul(MOVE[ch]); ww+=ch; c=N.centre()
                    if c in allcells:
                        ok=False
                        if first is None: first=(d,ww)
                    allcells.add(c)
                nxt.append((N,ns,ww)); n_nodes+=1
        frontier=nxt
        if not ok: break
    print(f"{name:55s} depth {d:2d} nodes {n_nodes:7d} tiles {len(allcells):7d} injective={ok} {'first collision '+str(first) if first else ''}")
    return ok, allcells
t=time.time()
# R1: alternate-turn rule: children = F (state unchanged) or the turn opposite to last turn
test_rule("R1 {F, opposite-of-last-turn}", lambda s:[('F',s),('L' if s=='R' else 'R', 'L' if s=='R' else 'R')], 'R', 15)
# R2: T-junction after k straight tiles: generation = F^k then L or R
for k in (0,1,2,3):
    test_rule(f"R2 T-junction, k={k} straight tiles then L|R", lambda s,k=k:[('F'*k+'L',s),('F'*k+'R',s)], 0, 15 if k<2 else 13)
# R2b: T junction: turn then k straights
for k in (1,2):
    test_rule(f"R2b turn then {k} straight: (L F^k | R F^k)", lambda s,k=k:[('L'+'F'*k,s),('R'+'F'*k,s)], 0, 14)
# R3: {F, L} only / {F,R} only
test_rule("R3 {F, L} always", lambda s:[('F',s),('L',s)], 0, 14)
# R4: Y: children = (F then L) and R ... asymmetric
test_rule("R4 {F, L} after R ; {F, R} after L ; {L,R} after F", lambda s:{'F':[('L','L'),('R','R')],'L':[('F','F'),('R','R')],'R':[('F','F'),('L','L')]}[s], 'F', 15)
# R5: alternating T: at even generations fork L|R, odd generations fork after one straight
test_rule("R5 (FL|FR) with one extra F every other generation", lambda s:[('F'*(1+s)+'L',1-s),('F'*(1+s)+'R',1-s)], 0, 14)
print("time %.1fs"%(time.time()-t))

# Holonomy validity language: injective?
def valid_children(prevR,Lok):
    out=[('F',(False,Lok))]
    if not prevR: out.append(('R',(True,True)))
    if Lok: out.append(('L',(False,False)))
    return out
seen={I_.centre():''}; frontier=[]; BR={'N':Rk[1].mul(T_), 'W':Rk[2].mul(T_), 'S':Rk[3].mul(T_), 'E':T_}
for b,S in BR.items():
    seen[S.centre()]=b; frontier.append((S,(False,True),b))
dup=0; total=5
for d in range(2,11):
    nxt=[]
    for (M,st,w) in frontier:
        for ch,ns in valid_children(*st):
            N=M.mul(MOVE[ch]); c=N.centre()
            if c in seen: dup+=1
            else: seen[c]=w+ch
            nxt.append((N,ns,w+ch)); total+=1
    frontier=nxt
print("Holonomy-valid words up to length 10:",total,"distinct tiles:",len(seen),"duplicates:",dup)
import math
cnt,seen_bfs,_=bfs_exact(10)
ball=sum(cnt[:8]); 
covered=sum(1 for c,dd in seen_bfs.items() if dd<=7 and c in seen)
print("tiles within graph distance 7:",ball,"covered by Holonomy-valid words of length<=10:",covered)
