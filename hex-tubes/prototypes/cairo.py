import math
t=(-2+math.sqrt(28))/12
def ep(c,d):
    A = c[0]%2==1
    if A: return (c[0]+t,c[1]+t) if d in [(0,1),(1,0)] else (c[0]-t,c[1]-t)
    return (c[0]-t,c[1]+t) if d in [(0,1),(-1,0)] else (c[0]+t,c[1]-t)
def pent(i,j):
    cs=[(i,j),(i+1,j),(i+1,j+1),(i,j+1)];out=[]
    for k,c in enumerate(cs):
        if (c[0]+c[1])%2==0: out.append(c);continue
        p=cs[k-1];n=cs[(k+1)%4]
        a=ep(c,(p[0]-c[0],p[1]-c[1]));b=ep(c,(n[0]-c[0],n[1]-c[1]))
        out.append(a)
        if b!=a: out.append(b)
    return out
def area(P): return sum(P[k][0]*P[(k+1)%len(P)][1]-P[(k+1)%len(P)][0]*P[k][1] for k in range(len(P)))/2
tot=0;edges={}
for i in range(6):
  for j in range(6):
    P=pent(i,j); assert len(P)==5,(i,j,P); a=area(P); assert a>0; tot+=a
    for k in range(5):
      e=(tuple(round(x,6) for x in P[k]),tuple(round(x,6) for x in P[(k+1)%5]))
      edges[e]=edges.get(e,0)+1
print(t,tot, [round(math.dist(*[pent(0,0)[k],pent(0,0)[(k+1)%5]]),4) for k in range(5)])
# every interior directed edge should have reverse present
bad=[e for e in edges if (e[1],e[0]) not in edges and all(0.5<x<5.5 for p in e for x in p)]
print("unmatched interior edges",len(bad), "dup",sum(v>1 for v in edges.values()))
