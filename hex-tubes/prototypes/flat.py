import numpy as np, math, matplotlib; matplotlib.use('Agg')
import matplotlib.pyplot as plt
from proto import cairo, newell
def surf(u,v):
    r=1.0-0.55*v+0.12*math.sin(v*6); cx=0.6*math.sin(v*2.5); a=2*math.pi*u
    p=np.array([cx+r*math.cos(a), r*math.sin(a), v*7]); return p*0.15, np.array([math.cos(a),math.sin(a),0])  # ~1m tall sample
N=12; M=round(N*7/(2*math.pi*0.7))
key={};V=[];NR=[];F=[]
for j in range(M):
    for i in range(N):
        f=[]
        for x,y in cairo(i,j):
            x=x%N; y=min(max(y,0),M); k=(round(x,6),round(y,6))
            if k not in key: key[k]=len(V); p,n=surf(x/N,y/M); V.append(p); NR.append(n)
            if not f or f[-1]!=key[k]: f.append(key[k])
        if f[0]==f[-1]: f.pop()
        F.append(f)
V=np.array(V)
def ek(a,b): return (min(a,b),max(a,b))
EF={}
for fi,f in enumerate(F):
    for k in range(len(f)): EF.setdefault(ek(f[k],f[(k+1)%len(f)]),[]).append(fi)
def local(f,out):
    P=V[f]; n=newell(P)
    if n@out<0: n=-n
    d=P[1]-P[0]; d-=n*(d@n); e1=d/np.linalg.norm(d); e2=np.cross(n,e1)
    return [np.array([(p-P[0])@e1,(p-P[0])@e2]) for p in P]
def rot(p,t): c,s=math.cos(t),math.sin(t); return np.array([p[0]*c-p[1]*s,p[0]*s+p[1]*c])
score=[];cut=[];labels=[];y=0;gap=0.02;tab=0.008;err=[]
for j in range(M):
    pos=[];ss=[];cc=[]
    for i in range(N):
        fi=j*N+i;f=F[fi];L=local(f,NR[f[0]])
        if i>0:
            pf=F[fi-1];sh=[(k,pf.index(f[k])) for k in range(len(f)) if f[k] in pf]
            la,lb=L[sh[0][0]],L[sh[1][0]];A,B=pos[-1][sh[0][1]],pos[-1][sh[1][1]]
            t=math.atan2(*(B-A)[::-1])-math.atan2(*(lb-la)[::-1])
            L=[A+rot(p-la,t) for p in L]
            err.append(abs(np.linalg.norm(lb-la)-np.linalg.norm(B-A)))
        pos.append(L);c=np.mean(L,0)
        for k in range(len(f)):
            a,b=L[k],L[(k+1)%len(f)];o=[x for x in EF[ek(f[k],f[(k+1)%len(f)])] if x!=fi];o=o[0] if o else -1
            if o==fi-1 and i>0: ss.append((a,b));continue
            if o==fi+1 and i<N-1: continue
            if tab>0 and (o//N==j+1 or (i==N-1 and o==j*N)):
                ss.append((a,b));l=np.linalg.norm(b-a);t_=(b-a)/l;nr=np.array([-t_[1],t_[0]])
                if nr@((a+b)/2-c)<0: nr=-nr
                if l>2*tab: a2=a+nr*tab+t_*tab;b2=b+nr*tab-t_*tab;cc+= [(a,a2),(a2,b2),(b2,b)]
                else: ap=(a+b)/2+nr*l/2;cc+=[(a,ap),(ap,b)]
            else: cc.append((a,b))
    first=np.mean(pos[0],0);last=np.mean(pos[-1],0);turn=-math.atan2(*(last-first)[::-1])
    pts=[rot(p,turn) for s_ in ss+cc for p in s_];mnx=min(p[0] for p in pts);mny=min(p[1] for p in pts);mxy=max(p[1] for p in pts)
    off=np.array([-mnx,y-mxy])
    score+=[(rot(a,turn)+off,rot(b,turn)+off) for a,b in ss];cut+=[(rot(a,turn)+off,rot(b,turn)+off) for a,b in cc]
    labels.append(("R%d"%(j+1),(-0.04,y-(mxy-mny)/2)));y-=(mxy-mny)+gap
print("max shared-edge mismatch (mm):",max(err)*1000)
fig,ax=plt.subplots(figsize=(8,11))
for a,b in cut: ax.plot([a[0],b[0]],[a[1],b[1]],color='black',lw=0.6)
for a,b in score: ax.plot([a[0],b[0]],[a[1],b[1]],color='tab:red',lw=0.6,ls='--')
for t,(x,yy) in labels: ax.text(x,yy,t,fontsize=7,va='center')
ax.set_aspect('equal');ax.axis('off');ax.set_title("Flat pattern sample: black = cut, red dashed = score (with 8 mm glue tabs)",fontsize=9)
plt.tight_layout();plt.savefig('/mnt/project-files/hex-tubes/flat-pattern-preview.png',dpi=120)
