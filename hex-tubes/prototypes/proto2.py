import numpy as np, sys
from proto import build, newell, resid
V,F,nf,bands,s=build()
lam=float(sys.argv[1]); iters=int(sys.argv[2])
V0=V.copy()
for it in range(iters):
    A=np.zeros((len(V),3,3)); b=np.zeros((len(V),3))
    for f in F:
        P=V[f]; n=newell(P); d=P.mean(0)@n
        A[f]+=np.outer(n,n); b[f]+=n*d
    # regularize toward current position
    A+=lam*np.eye(3); b+=lam*V
    V=np.linalg.solve(A,b[...,None])[...,0]
    if (it+1) in (1,5,10,20,40,80,160): print(it+1,resid(V,F), np.abs(V-V0).max())
