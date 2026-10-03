import json, sys, io, zipfile, urllib.request, time, collections, re
import numpy as np, pydicom
def fetch_rt(uid):
    url="https://services.cancerimagingarchive.net/nbia-api/services/v1/getImage?SeriesInstanceUID="+uid
    for a in range(3):
        try:
            with urllib.request.urlopen(url, timeout=120) as r: b=r.read(); break
        except Exception as e:
            b=None; time.sleep(2)
    if b is None: return None
    z=zipfile.ZipFile(io.BytesIO(b))
    for n in z.namelist():
        if n.lower().endswith('.dcm'):
            return pydicom.dcmread(io.BytesIO(z.read(n)))
def roi_stats(ds):
    names={r.ROINumber:r.ROIName for r in ds.StructureSetROISequence}
    out={}
    for rc in getattr(ds,'ROIContourSequence',[]):
        nm=names.get(rc.ReferencedROINumber)
        pts=[]; area_vol=0.0
        cs=getattr(rc,'ContourSequence',[])
        zs=[]
        for c in cs:
            p=np.array(c.ContourData,dtype=float).reshape(-1,3)
            pts.append(p); zs.append(p[0,2])
            x,y=p[:,0],p[:,1]
            area_vol+=0.5*abs(np.dot(x,np.roll(y,-1))-np.dot(y,np.roll(x,-1)))
        if not pts: out[nm]=None; continue
        P=np.vstack(pts)
        uz=sorted(set(np.round(zs,2)))
        dz=np.median(np.diff(uz)) if len(uz)>1 else 3.0
        # area-weighted centroid
        cx=cy=cz=A=0.0
        for p in pts:
            x,y=p[:,0],p[:,1]
            a=0.5*abs(np.dot(x,np.roll(y,-1))-np.dot(y,np.roll(x,-1)))
            cx+=a*x.mean(); cy+=a*y.mean(); cz+=a*p[0,2]; A+=a
        out[nm]=dict(n=len(pts), vol_cc=area_vol*dz/1000.0, cen=(cx/A,cy/A,cz/A) if A>0 else tuple(P.mean(0)), zmin=P[:,2].min(), zmax=P[:,2].max(), dz=dz)
    return out
if __name__=="__main__":
    p=sys.argv[1]; which=int(sys.argv[2]) if len(sys.argv)>2 else 0
    d=json.load(open(f'tcia/series_{p}.json'))
    bystudy=collections.defaultdict(list)
    for s in d: bystudy[s['StudyInstanceUID']].append(s)
    fb=sorted([v for v in bystudy.values() if any(s['Modality']=='RTSTRUCT' for s in v)], key=lambda v:str(v[0].get('StudyDate')))
    v=fb[which]
    rs=[s for s in v if s['Modality']=='RTSTRUCT']
    def ph(s):
        m=re.search(r'Gated,\s*([\d.]+)%',s['SeriesDescription'] or ''); return float(m.group(1)) if m else -1
    res={}
    t0=time.time()
    for s in sorted(rs,key=ph):
        ds=fetch_rt(s['SeriesInstanceUID'])
        if ds is None: print('fail',ph(s)); continue
        res[ph(s)]=roi_stats(ds)
    print(f'patient {p} study {str(v[0].get("StudyDate"))[:10]}: fetched {len(res)} RTSTRUCT in {time.time()-t0:.1f}s')
    allnames=collections.Counter(n for r in res.values() for n in r)
    print('ROI names (count of phases present):', dict(allnames))
    json.dump({str(k):{n:(None if s is None else {kk:(list(vv) if isinstance(vv,tuple) else float(vv)) for kk,vv in s.items()}) for n,s in r.items()} for k,r in res.items()}, open(f'tcia/rt_{p}_{which}.json','w'))
