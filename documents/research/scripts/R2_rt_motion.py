import json, sys, io, zipfile, urllib.request, time, collections, re
import numpy as np, pydicom
from rt_probe import fetch_rt
def contours(ds, prefix):
    names={r.ROINumber:r.ROIName for r in ds.StructureSetROISequence}
    out=[]
    for rc in getattr(ds,'ROIContourSequence',[]):
        nm=names.get(rc.ReferencedROINumber,'')
        if nm.lower().startswith(prefix.lower()):
            for c in getattr(rc,'ContourSequence',[]):
                out.append(np.array(c.ContourData,dtype=float).reshape(-1,3))
    return out
def inpoly(px,py,poly):
    x=poly[:,0]; y=poly[:,1]; n=len(x); inside=np.zeros(px.shape,bool)
    j=n-1
    for i in range(n):
        cond=((y[i]>py)!=(y[j]>py))
        with np.errstate(divide='ignore',invalid='ignore'):
            xint=(x[j]-x[i])*(py-y[i])/(y[j]-y[i]+1e-12)+x[i]
        inside^=cond&(px<xint)
        j=i
    return inside
def raster(cs_by_phase, h=1.0):
    allp=np.vstack([p for cs in cs_by_phase.values() for p in cs])
    zs=sorted(set(np.round(allp[:,2],1)))
    dz=float(np.median(np.diff(zs))) if len(zs)>1 else 3.0
    x0,y0=allp[:,0].min()-2,allp[:,1].min()-2; x1,y1=allp[:,0].max()+2,allp[:,1].max()+2
    xs=np.arange(x0,x1,h); ys=np.arange(y0,y1,h); PX,PY=np.meshgrid(xs,ys,indexing='ij')
    zidx={z:i for i,z in enumerate(zs)}
    masks={}
    for ph,cs in cs_by_phase.items():
        m=np.zeros((len(xs),len(ys),len(zs)),bool)
        for p in cs:
            k=zidx[round(float(p[0,2]),1)]
            m[:,:,k]^=inpoly(PX,PY,p)   # XOR handles holes
        masks[ph]=m
    return masks,(xs,ys,np.array(zs)),h*h*dz/1000.0
def analyse(p, which=0):
    d=json.load(open(f'tcia/series_{p}.json'))
    bystudy=collections.defaultdict(list)
    for s in d: bystudy[s['StudyInstanceUID']].append(s)
    fb=sorted([v for v in bystudy.values() if any(s['Modality']=='RTSTRUCT' for s in v)], key=lambda v:str(v[0].get('StudyDate')))
    v=fb[which]; rs=[s for s in v if s['Modality']=='RTSTRUCT']
    def ph(s):
        m=re.search(r'Gated,\s*([\d.]+)%',s['SeriesDescription'] or ''); return float(m.group(1)) if m else -1
    tum={}; names=collections.Counter()
    for s in sorted(rs,key=ph):
        ds=fetch_rt(s['SeriesInstanceUID'])
        if ds is None: continue
        for r in ds.StructureSetROISequence: names[re.sub(r'_c\d+$','',r.ROIName)]+=1
        cs=contours(ds,'Tumor')
        if cs: tum[ph(s)]=cs
    if len(tum)<2: return dict(patient=p, err='no tumour contours', names=dict(names))
    masks,(xs,ys,zs),vv=raster(tum)
    X,Y,Z=np.meshgrid(xs,ys,zs,indexing='ij')
    cen={}; vol={}
    for k,m in masks.items():
        n=m.sum(); vol[k]=n*vv
        cen[k]=(float(X[m].mean()),float(Y[m].mean()),float(Z[m].mean()))
    C=np.array([cen[k] for k in sorted(cen)])
    rng=C.max(0)-C.min(0)
    union=np.zeros_like(next(iter(masks.values())))
    inter=np.ones_like(union)
    for m in masks.values(): union|=m; inter&=m
    itv=union.sum()*vv; gmean=float(np.mean(list(vol.values())))
    # gated 30-70% window (around exhale 50%)
    g=np.zeros_like(union)
    for k in (30.0,40.0,50.0,60.0,70.0):
        if k in masks: g|=masks[k]
    # time-weighted mean position & nearest phase
    mp=C.mean(0); kk=sorted(cen); dist=[float(np.linalg.norm(np.array(cen[k])-mp)) for k in kk]
    # hysteresis: max separation between inhale path (50->90->0?) approximate: distance between phases with same SI on the two halves
    si=C[:,2]; order=np.argsort(si)
    return dict(patient=p, study=str(v[0].get('StudyDate'))[:10], phases=len(tum), names=dict(names),
                range_LR_AP_SI_mm=[round(float(x),1) for x in rng],
                gtv_mean_cc=round(gmean,1), gtv_min_cc=round(min(vol.values()),1), gtv_max_cc=round(max(vol.values()),1),
                itv_cc=round(float(itv),1), itv_over_gtv=round(float(itv/gmean),2), core_cc=round(float(inter.sum()*vv),1),
                gated3070_cc=round(float(g.sum()*vv),1), midpos_nearest_phase=kk[int(np.argmin(dist))], midpos_err_mm=round(min(dist),1),
                cen_by_phase={str(int(k)):[round(c,1) for c in cen[k]] for k in kk}, dz=float(np.median(np.diff(zs))) if len(zs)>1 else None)
if __name__=="__main__":
    out=[]
    for p in sys.argv[1:]:
        t0=time.time()
        try: r=analyse(p)
        except Exception as e: r=dict(patient=p, err=repr(e))
        r['secs']=round(time.time()-t0,1); out.append(r)
        print(json.dumps(r), flush=True)
    json.dump(out,open('tcia/motion_all.json','w'),indent=1)
