# Derive the canonical spanning-tree language for {4,5} using exact ids, and test Holonomy's validity rule.
import sys, itertools, collections
sys.setrecursionlimit(10000)
from exact45 import *
# heading convention: frame's +x = heading. F: M*T ; L: M*R*T ; R: M*R^3*T ; B: M*R^2*T
MOVE={'F':T_, 'L':Rk[1].mul(T_), 'R':Rk[3].mul(T_)}
BR={'N':Rk[1].mul(T_), 'W':Rk[2].mul(T_), 'S':Rk[3].mul(T_), 'E':T_}
def tile_of(word):
    M=I_
    for i,ch in enumerate(word):
        M=M.mul(BR[ch] if i==0 else MOVE[ch])
    return M.centre()
DEPTH=8
counts, seen, frames = bfs_exact(DEPTH)
# all geodesic words per tile
geo=collections.defaultdict(list)
def rec(word,M,d):
    c=M.centre()
    if seen.get(c,-1)!=d: return
    geo[c].append(word)
    if d==DEPTH: return
    for ch,S in (BR.items() if d==0 else MOVE.items()):
        rec(word+ch, M.mul(S), d+1)
rec('',I_,0)
print("tiles with geodesic words:",len(geo), "expected", sum(counts))
def valid_holonomy(w):
    # w[0] branch; subsequent F/L/R
    lastL=None
    for i in range(1,len(w)):
        ch=w[i]
        if ch=='R' and i>1 and w[i-1]=='R': return False
        if ch=='L':
            if lastL is not None and 'R' not in w[lastL+1:i]: return False
            lastL=i
    return True
bad=collections.Counter(); ex={}
for c,ws in geo.items():
    nv=sum(valid_holonomy(w) for w in ws)
    d=seen[c]
    bad[(d,nv)]+=1
    if nv!=1 and (d,nv) not in ex: ex[(d,nv)]=ws
print("(depth, #valid geodesic words under naive reading) -> #tiles:")
for k in sorted(bad): print("  ",k,bad[k])
for k,v in sorted(ex.items())[:6]: print("example",k,v)
# multiplicity of geodesic words per tile
mult=collections.Counter((seen[c],len(ws)) for c,ws in geo.items())
print("geodesic-word multiplicity by depth:", sorted(mult.items())[:30])

# ---- derive OUR canonical language: lexicographically smallest geodesic word with order F<L<R (branch order E<N<W<S)
order={'E':0,'N':1,'W':2,'S':3,'F':0,'L':1,'R':2}
canon={c:min(ws,key=lambda w:[order[ch] for ch in w]) for c,ws in geo.items()}
L=set(canon.values())
# prefix closed?
print("prefix-closed:", all(w[:-1] in L for w in L if w))
# minimal forbidden factors (in the FLR part) up to length 6
allowed_factors=set()
for w in L:
    s=w[1:]
    for i in range(len(s)):
        for j in range(i+1,min(len(s),i+7)+1): allowed_factors.add(s[i:j])
forb=[]
for n in range(1,7):
    for t in itertools.product('FLR',repeat=n):
        s=''.join(t)
        if s in allowed_factors: continue
        if any(f in s for f in forb): continue
        forb.append(s)
print("minimal forbidden factors (len<=6):",forb)
# Check: does 'avoid forbidden factors' reproduce the counts?
def count_avoid(forb,n):
    res=[1,4]; cur=['']
    for d in range(2,n+1):
        nxt=[]
        for s in cur:
            for ch in 'FLR':
                t=s+ch
                if any(t.endswith(f) for f in forb): continue
                nxt.append(t)
        cur=nxt; res.append(4*len(cur))
    return res
print("count avoiding those factors:",count_avoid(forb,DEPTH),"true",counts)
# Myhill-Nerode: number of distinct 'future sets' (depth-limited) among canonical words of depth<=DEPTH-4
def futures(w,k):
    out=[]
    def r(s,d):
        if d==k: return
        for ch in 'FLR':
            if w+s+ch in L: out.append(s+ch); r(s+ch,d+1)
    r('',0); return frozenset(out)
classes=collections.defaultdict(list)
for w in L:
    if 1<=len(w)<=DEPTH-4: classes[futures(w,4)].append(w)
print("number of cone types (states) seen among depth 1..%d words: %d"%(DEPTH-4,len(classes)))
for fs,ws in sorted(classes.items(), key=lambda kv: min(kv[1],key=len)):
    kids=sorted(x for x in fs if len(x)==1)
    print("  state rep",min(ws,key=lambda w:(len(w),w)),"n=",len(ws),"children:",kids, "suffixes:",collections.Counter(w[-2:] for w in ws).most_common(4))
