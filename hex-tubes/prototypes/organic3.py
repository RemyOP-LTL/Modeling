import numpy as np, math, matplotlib; matplotlib.use('Agg')
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection
from proto import cairo, newell
def surf(u,v):
    r=1.0-0.55*v+0.12*math.sin(v*6); cx=0.6*math.sin(v*2.5); a=2*math.pi*u
    return np.array([cx+r*math.cos(a), r*math.sin(a), v*7])
N=12; M=round(N*7/(2*math.pi*0.7))
light=np.array([0.5,-0.6,0.6]);light/=np.linalg.norm(light)
polys=[];cols=[]
for j in range(M):
    for i in range(N):
        P=np.array([surf((x%N)/N,min(max(y,0),M)/M) for x,y in cairo(i,j)])
        n=newell(P);c=P.mean(0);v=c[2]/7;ax_pt=np.array([0.6*math.sin(v*2.5),0,c[2]])
        if n@(c-ax_pt)<0:n=-n
        sh=0.35+0.65*max(0,n@light);polys.append(P);cols.append((0.95*sh,0.5*sh,0.45*sh))
fig=plt.figure(figsize=(6,8));ax=fig.add_subplot(projection='3d')
ax.add_collection3d(Poly3DCollection(polys,facecolors=cols,edgecolors=(0.2,0.1,0.1),linewidths=0.3))
ax.set_xlim(-2,2);ax.set_ylim(-2,2);ax.set_zlim(0,7);ax.set_box_aspect((1,1,1.75));ax.view_init(12,-60);ax.set_axis_off()
ax.set_title("Organic layout, merged shell (12 around)");plt.tight_layout()
plt.savefig('/mnt/project-files/hex-tubes/pentagon-organic-preview.png',dpi=110)
