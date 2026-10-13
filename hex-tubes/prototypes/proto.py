import math, numpy as np, sys
T=(-2+math.sqrt(28))/12; EDGE_RATIO=math.sqrt((1-T)**2+T**2)
def barEnd(c,d):
    if c[0]%2==1:
        return (c[0]+T,c[1]+T) if d in [(0,1),(1,0)] else (c[0]-T,c[1]-T)
    return (c[0]-T,c[1]+T) if d in [(0,1),(-1,0)] else (c[0]+T,c[1]-T)
def cairo(i,j):
    cs=[(i,j),(i+1,j),(i+1,j+1),(i,j+1)];out=[]
    for k,c in enumerate(cs):
        if (c[0]+c[1])%2==0: out.append(c);continue
        p=cs[k-1];n=cs[(k+1)%4]
        a=barEnd(c,(p[0]-c[0],p[1]-c[1]));b=barEnd(c,(n[0]-c[0],n[1]-c[1]))
        out.append(a)
        if b!=a: out.append(b)
    return out
def surf(u,v):
    r=0.25-0.15*v+0.03*math.sin(v*6)
    cx=0.3*math.sin(v*2.5); a=2*math.pi*u
    return np.array([cx+r*math.cos(a), r*math.sin(a), v*2.0])
def build(edge=0.05, tol=0.12, fill=0.75):
    s=edge/EDGE_RATIO
    K=200; vs=np.linspace(0,1,K+1)
    lens=[0]
    for k in range(K):
        lens.append(lens[-1]+np.mean([np.linalg.norm(surf(u,vs[k+1])-surf(u,vs[k])) for u in np.linspace(0,1,8,endpoint=False)]))
    lens=np.array(lens); L=lens[-1]
    vOf=lambda l: float(np.interp(l,lens,vs))
    C=lambda v: sum(np.linalg.norm(surf((k+1)/48,v)-surf(k/48,v)) for k in range(48))
    even=lambda x: max(4,2*round(x/2))
    bands=[];ell=0
    while True:
        N=even(C(vOf(min(ell+s/2,L)))/s); start=ell; rows=0
        while ell+s<=L+0.5*s:
            ratio=C(vOf(min(ell+s/2,L)))/(N*s)
            if rows>0 and abs(ratio-1)>tol: break
            rows+=1; ell+=s
        bands.append([start,rows,N])
        if ell+fill*s+s> L+0.5*s: break
        ell+=fill*s
    scale=L/ell
    verts=[];key={};faces=[]
    def vid(b,x,y):
        st,rows,N=bands[b]; y=min(max(y,0),rows); x=x%N
        k=(b,round(x,6),round(y,6))
        if k not in key:
            key[k]=len(verts); verts.append(surf(x/N, vOf((st+y*s)*scale)))
        return key[k]
    edges=[];fband=[]
    for b,(st,rows,N) in enumerate(bands):
        for j in range(rows):
            for i in range(N):
                f=[vid(b,x,y) for x,y in cairo(i,j)]
                f=[f[k] for k in range(len(f)) if f[k]!=f[k-1]]
                faces.append(f); fband.append(b)
        edges.append(None)
    nf=len(faces)
    for b in range(len(bands)-1):
        def ring(bb,top):
            N=bands[bb][2]; rows=bands[bb][1]
            sel=[(kx,ky) for (kb,kx,ky) in key if kb==bb and ((ky>=rows-T-1e-6) if top else (ky<=T+1e-6))]
            # keep only vertices on the band's boundary edges
            bf=[f for f,fb in zip(faces,fband) if fb==bb]
            de=set((f[k],f[(k+1)%len(f)]) for f in bf for k in range(len(f)))
            bv=set(a for a,b2 in de if (b2,a) not in de)
            pts=sorted(p for p in sel if key[(bb,)+p] in bv)
            return [(x/N,key[(bb,x,yy)]) for x,yy in pts]
        P=ring(b,True); Q=ring(b+1,False)
        i=j=0;nP=len(P);nQ=len(Q)
        while i<nP or j<nQ:
            pn=P[(i+1)%nP][0]+(1 if i+1>=nP else 0); qn=Q[(j+1)%nQ][0]+(1 if j+1>=nQ else 0)
            if j>=nQ or (i<nP and pn<qn):
                faces.append([P[i%nP][1],P[(i+1)%nP][1],Q[j%nQ][1]]); i+=1
            else:
                faces.append([P[i%nP][1],Q[(j+1)%nQ][1],Q[j%nQ][1]]); j+=1
    return np.array(verts),faces,nf,bands,s
def newell(P):
    n=np.zeros(3)
    for k in range(len(P)):
        a=P[k];b=P[(k+1)%len(P)]
        n+=[(a[1]-b[1])*(a[2]+b[2]),(a[2]-b[2])*(a[0]+b[0]),(a[0]-b[0])*(a[1]+b[1])]
    return n/np.linalg.norm(n)
def resid(V,faces):
    m=0
    for f in faces:
        if len(f)<4: continue
        P=V[f];n=newell(P);c=P.mean(0);m=max(m,np.abs((P-c)@n).max())
    return m
def shapeup(V,faces,iters):
    V=V.copy()
    for it in range(iters):
        acc=np.zeros_like(V);cnt=np.zeros(len(V))
        for f in faces:
            P=V[f];n=newell(P);c=P.mean(0)
            acc[f]+=P-np.outer((P-c)@n,n);cnt[f]+=1
        V=acc/cnt[:,None]
        if it in (0,9,24,49,99,199,399,799): print(it+1,resid(V,faces))
    return V
if __name__=="__main__":
    V,F,nf,bands,s=build()
    print("verts",len(V),"faces",len(F),"pent",nf,"bands",[(r,N) for _,r,N in bands])
    print("initial",resid(V,F))
    V2=shapeup(V,F,int(sys.argv[1]) if len(sys.argv)>1 else 200)
    print("max move",np.abs(V2-V).max())
    np.save('/tmp/claude-0/-home-claude/a07a2184-e799-546a-9e55-1ac7193d1528/scratchpad/V.npy',V2)
    import pickle;pickle.dump((F,nf),open('/tmp/claude-0/-home-claude/a07a2184-e799-546a-9e55-1ac7193d1528/scratchpad/F.pkl','wb'))
