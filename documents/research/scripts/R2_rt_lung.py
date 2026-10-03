import json, sys, collections, re, numpy as np
from rt_probe import fetch_rt
def vol_and_zmin(ds, prefix):
    names={r.ROINumber:r.ROIName for r in ds.StructureSetROISequence}
    A=collections.defaultdict(float)
    for rc in getattr(ds,'ROIContourSequence',[]):
        nm=names.get(rc.ReferencedROINumber,'')
        if nm.lower().startswith(prefix.lower()):
            for c in getattr(rc,'ContourSequence',[]):
                p=np.array(c.ContourData,dtype=float).reshape(-1,3); x,y=p[:,0],p[:,1]
                A[round(float(p[0,2]),1)]+=0.5*abs(np.dot(x,np.roll(y,-1))-np.dot(y,np.roll(x,-1)))
    if not A: return None
    zs=sorted(A); dz=float(np.median(np.diff(zs))) if len(zs)>1 else 3.0
    return sum(A.values())*dz/1e6, min(zs), max(zs)   # litres
for p in sys.argv[1:]:
    d=json.load(open(f'tcia/series_{p}.json'))
    bystudy=collections.defaultdict(list)
    for s in d: bystudy[s['StudyInstanceUID']].append(s)
    fb=sorted([v for v in bystudy.values() if any(s['Modality']=='RTSTRUCT' for s in v)], key=lambda v:str(v[0].get('StudyDate')))
    rs=[s for s in fb[0] if s['Modality']=='RTSTRUCT']
    ph=lambda s: float(re.search(r'Gated,\s*([\d.]+)%',s['SeriesDescription']).group(1))
    print('patient',p)
    for s in sorted(rs,key=ph):
        ds=fetch_rt(s['SeriesInstanceUID'])
        r=vol_and_zmin(ds,'RLung'); l=vol_and_zmin(ds,'LLung')
        fr=getattr(ds,'ReferencedFrameOfReferenceSequence',None)
        f=lambda v: 'none' if v is None else '%.2f L zmin %.1f'%(v[0],v[1])
        t=vol_and_zmin(ds,'Tumor'); h=vol_and_zmin(ds,'Heart')
        print('  phase %3d%%  RLung %s | LLung %s | Heart %s | Tumor %s'%(ph(s), f(r), f(l), f(h), f(t)))
    m=[x for x in json.load(open('tcia/motion_all.json')) if x['patient']==p][0]
    C=np.array([m['cen_by_phase'][k] for k in sorted(m['cen_by_phase'],key=int)])
    print('  tumour centroid (LR,AP,SI) by phase 0..90:'); print('  ', C.tolist())
