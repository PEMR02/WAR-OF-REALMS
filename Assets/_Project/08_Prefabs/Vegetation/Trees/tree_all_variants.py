"""
WAR OF REALMS — Generador de 5 Variantes de Árbol con LOD
==========================================================
Ejecutar completo en Blender vía execute_blender_code (MCP).
Genera 15 objetos: 5 variantes × 3 LODs (LOD0 / LOD1 / LOD2)

Layout en escena:
  Eje X → variantes (0, 15, 30, 45, 60 m)
  Eje Y → LOD       (0, 18, 36 m)

Variantes:
  Tree_TallForest   — alto y estrecho, copa densa arriba
  Tree_WideOak      — copa ancha, tronco grueso
  Tree_YoungDense   — compacto, alta densidad de hojas
  Tree_WindBent     — tronco curvado, copa asimétrica
  Tree_SparseNatural— ramas visibles, densidad media

LOD factor:
  LOD0 = 1.0  (100% hojas — LOD de máxima calidad)
  LOD1 = 0.6  (60%  hojas — rango medio ~20-40 m)
  LOD2 = 0.3  (30%  hojas — distancia >40 m)

Polígonos aproximados por árbol (LOD0):
  TallForest    ~111,180   ~13,673 hojas
  WideOak       ~158,823   ~19,598 hojas
  YoungDense    ~162,445   ~20,019 hojas
  WindBent      ~125,184   ~15,411 hojas
  SparseNatural  ~67,550    ~8,255 hojas
"""

# ============================================================
#  CONFIGURACIONES (editar aquí para ajustar variantes)
# ============================================================
TREES = [
    {
        'tag': 'TallForest',
        'H': 9.5, 'R': 0.22, 'cz': 5.5, 'cr': 2.8,
        'seed': 101, 'ls': 0.34,
        'leaves_range': (36, 50),
        'mid': {'n': 5, 'elev': (42, 58), 'z': (0.55, 0.80), 'l_f': 0.44, 'r_f': 0.40},
        'top': {'n': 10, 'elev': (8, 22),  'z': (0.72, 0.97), 'l': (2.2, 3.5), 'r_f': 0.30},
    },
    {
        'tag': 'WideOak',
        'H': 7.5, 'R': 0.36, 'cz': 3.8, 'cr': 4.8,
        'seed': 202, 'ls': 0.43,
        'leaves_range': (44, 62),
        'mid': {'n': 8, 'elev': (50, 72), 'z': (0.40, 0.76), 'l_f': 0.55, 'r_f': 0.50},
        'top': {'n': 8, 'elev': (16, 38), 'z': (0.65, 0.92), 'l': (3.0, 4.5), 'r_f': 0.38},
    },
    {
        'tag': 'YoungDense',
        'H': 6.5, 'R': 0.20, 'cz': 2.8, 'cr': 2.6,
        'seed': 303, 'ls': 0.31,
        'leaves_range': (40, 55),
        'mid': {'n': 9, 'elev': (35, 58), 'z': (0.35, 0.76), 'l_f': 0.48, 'r_f': 0.38},
        'top': {'n': 10, 'elev': (10, 28), 'z': (0.62, 0.95), 'l': (1.8, 3.0), 'r_f': 0.28},
    },
    {
        'tag': 'WindBent',
        'H': 8.0, 'R': 0.27, 'cz': 4.2, 'cr': 3.6,
        'seed': 404, 'ls': 0.38,
        'leaves_range': (38, 52),
        'lean_x': 1.4,   # metros de inclinación en X
        'mid': {'n': 7, 'elev': (38, 58), 'z': (0.44, 0.78), 'l_f': 0.50, 'r_f': 0.44},
        'top': {'n': 8, 'elev': (12, 30), 'z': (0.68, 0.94), 'l': (2.5, 4.0), 'r_f': 0.34},
    },
    {
        'tag': 'SparseNatural',
        'H': 8.8, 'R': 0.24, 'cz': 4.8, 'cr': 3.4,
        'seed': 505, 'ls': 0.36,
        'leaves_range': (26, 38),   # deliberadamente pocas hojas por cluster
        'mid': {'n': 6, 'elev': (40, 60), 'z': (0.46, 0.80), 'l_f': 0.47, 'r_f': 0.40},
        'top': {'n': 7, 'elev': (15, 32), 'z': (0.68, 0.96), 'l': (2.6, 3.8), 'r_f': 0.32},
    },
]

LODs   = [(1.0, 'LOD0'), (0.6, 'LOD1'), (0.3, 'LOD2')]
X_SP   = 15.0   # separación entre variantes
Y_SP   = 18.0   # separación entre LODs

# ============================================================
#  SCRIPT BLENDER  (pegar completo en execute_blender_code)
# ============================================================
BLENDER_SCRIPT = '''
import bpy, bmesh, math, random
from mathutils import Vector

# --- LIMPIAR ---
for o in list(bpy.data.objects):  bpy.data.objects.remove(o, do_unlink=True)
for m in list(bpy.data.meshes):   bpy.data.meshes.remove(m)
for m in list(bpy.data.materials): bpy.data.materials.remove(m)

# --- MATERIALES COMPARTIDOS ---
bark_mat = bpy.data.materials.new("MAT_Bark")
bark_mat.use_nodes = True
bn = bark_mat.node_tree; bn.nodes.clear()
b_bsdf = bn.nodes.new("ShaderNodeBsdfPrincipled"); b_bsdf.location=(400,0)
b_bsdf.inputs["Roughness"].default_value = 0.88
b_out  = bn.nodes.new("ShaderNodeOutputMaterial"); b_out.location=(700,0)
bn.links.new(b_bsdf.outputs["BSDF"], b_out.inputs["Surface"])
b_noise = bn.nodes.new("ShaderNodeTexNoise"); b_noise.location=(0,100)
b_noise.inputs["Scale"].default_value = 14
b_noise.inputs["Detail"].default_value = 8
b_cr = bn.nodes.new("ShaderNodeValToRGB"); b_cr.location=(200,100)
b_cr.color_ramp.elements[0].color = (0.25, 0.13, 0.05, 1)
b_cr.color_ramp.elements[1].color = (0.55, 0.38, 0.20, 1)
bn.links.new(b_noise.outputs["Fac"], b_cr.inputs["Fac"])
bn.links.new(b_cr.outputs["Color"], b_bsdf.inputs["Base Color"])

leaf_mat = bpy.data.materials.new("MAT_Leaf")
leaf_mat.use_nodes = True
ln = leaf_mat.node_tree; ln.nodes.clear()
l_bsdf = ln.nodes.new("ShaderNodeBsdfPrincipled"); l_bsdf.location=(400,0)
l_bsdf.inputs["Base Color"].default_value = (0.08, 0.35, 0.06, 1)
l_bsdf.inputs["Roughness"].default_value  = 0.72
try: l_bsdf.inputs["Subsurface Weight"].default_value = 0.08
except: pass
l_out = ln.nodes.new("ShaderNodeOutputMaterial"); l_out.location=(700,0)
ln.links.new(l_bsdf.outputs["BSDF"], l_out.inputs["Surface"])
leaf_mat.use_backface_culling = False

# --- MAKE_TREE ---
def make_tree(cfg, lf, pos, bmat, lmat):
    tag   = cfg["tag"]; H=cfg["H"]; R=cfg["R"]; cz=cfg["cz"]; cr=cfg["cr"]
    seed  = cfg["seed"]; ls=cfg["ls"]
    lmin, lmax = cfg.get("leaves_range", (42, 58))
    lean_x = cfg.get("lean_x", 0.0)
    mid = cfg["mid"]; top = cfg["top"]
    rng = random.Random(seed)
    bm_ = bmesh.new()

    def _cyl(p0,p1,r0,r1,segs,mi):
        d=p1-p0; L=d.length
        if L<0.001: return
        z=d/L; ref=Vector((0,1,0)) if abs(z.z)>0.9 else Vector((0,0,1))
        x=z.cross(ref).normalized(); y=z.cross(x).normalized()
        sv,ev=[],[]
        for i in range(segs):
            a=2*math.pi*i/segs; c,s=math.cos(a),math.sin(a)
            sv.append(bm_.verts.new(p0+(x*c+y*s)*r0))
            ev.append(bm_.verts.new(p1+(x*c+y*s)*r1))
        for i in range(segs):
            ni=(i+1)%segs
            try: f=bm_.faces.new([sv[i],sv[ni],ev[ni],ev[i]]); f.material_index=mi
            except: pass

    def _leaf(pos_,up,rot,scale):
        u=up.normalized(); ref=Vector((1,0,0)) if abs(u.z)>0.9 else Vector((0,0,1))
        r_=u.cross(ref).normalized(); fv=r_.cross(u).normalized()
        cr_,sr=math.cos(rot),math.sin(rot); r2=r_*cr_+fv*sr; f2=-r_*sr+fv*cr_
        def T(v): return pos_+r2*v.x+f2*v.y+u*v.z
        lv=[Vector(p)*scale for p in [(0,0,0),(-0.13,0,0.12),(-0.155,0.022,0.26),
            (-0.09,0.042,0.40),(0,0.054,0.48),(0.09,0.042,0.40),
            (0.155,0.022,0.26),(0.13,0,0.12),(0,0.026,0.23)]]
        verts=[bm_.verts.new(T(v)) for v in lv]
        for fi in [(0,8,1),(0,7,8),(1,8,2),(7,6,8),(2,8,3),(6,5,8),(3,4,8),(4,5,8)]:
            try: f=bm_.faces.new([verts[i] for i in fi]); f.material_index=1
            except: pass

    def _cluster(tip,n,cr_):
        for _ in range(n):
            th=math.acos(rng.uniform(-0.95,0.95)); ph=rng.uniform(0,2*math.pi)
            r=cr_*math.pow(rng.uniform(0.30,1.0),0.28)
            lx=r*math.sin(th)*math.cos(ph); ly=r*math.sin(th)*math.sin(ph); lz=r*math.cos(th)
            lpos=tip+Vector((lx,ly,lz))
            ow=Vector((lx,ly,max(lz,0)+0.04)); ow=ow.normalized() if ow.length>0.01 else Vector((0,0,1))
            ld=(ow*0.6+Vector((0,0,1))*0.4+Vector((rng.uniform(-0.14,0.14),rng.uniform(-0.14,0.14),0))).normalized()
            _leaf(lpos,ld,rng.uniform(0,2*math.pi),rng.uniform(0.82,1.18)*ls)
        off=Vector((rng.uniform(-1,1),rng.uniform(-1,1),rng.uniform(-0.2,0.8))).normalized()*rng.uniform(0.14,0.24)
        for _ in range(int(n*0.55)):
            th=math.acos(rng.uniform(-0.95,0.95)); ph=rng.uniform(0,2*math.pi)
            r=cr_*0.80*math.pow(rng.uniform(0.30,1.0),0.28)
            lx=r*math.sin(th)*math.cos(ph); ly=r*math.sin(th)*math.sin(ph); lz=r*math.cos(th)
            lpos=tip+off+Vector((lx,ly,lz))
            ow=Vector((lx,ly,max(lz,0)+0.04)); ow=ow.normalized() if ow.length>0.01 else Vector((0,0,1))
            ld=(ow*0.6+Vector((0,0,1))*0.4+Vector((rng.uniform(-0.14,0.14),rng.uniform(-0.14,0.14),0))).normalized()
            _leaf(lpos,ld,rng.uniform(0,2*math.pi),rng.uniform(0.82,1.18)*ls)

    def _branch(p0,d_,length,radius,level):
        p1=p0+d_*length
        grav=[0,0.14,0.08,0.03][min(level,3)]; p1.z-=length*grav
        r_end=radius*0.22; segs=[0,8,7,5][min(level,3)]
        _cyl(p0,p1,radius,r_end,segs,0)
        if level==3:
            nl=max(5,int(rng.randint(lmin,lmax)*lf)); cr__=rng.uniform(0.20,0.30)
            _cluster(p1,nl,cr__); return
        n_ch=rng.randint(3,5) if level==1 else rng.randint(3,4)
        for i in range(n_ch):
            ts=(i+1)/(n_ch+1); s0=p0+d_*length*(0.25+ts*0.55)
            perp=d_.cross(Vector((0,0,1)))
            if perp.length<0.01: perp=d_.cross(Vector((1,0,0)))
            perp=perp.normalized()
            sa=rng.uniform(0,2*math.pi); ss=rng.uniform(0.42,0.70)
            nd=(d_*0.60+perp*math.cos(sa)*ss+d_.cross(perp)*math.sin(sa)*ss).normalized()
            _branch(s0,nd,length*rng.uniform(0.34,0.52),r_end*rng.uniform(1.3,1.9),level+1)

    # Tronco
    RINGS=23; SEGS=10; rv=[]
    for ri in range(RINGS):
        t=ri/(RINGS-1); z_=H*0.62*t
        r_=R*math.pow(1-t,0.65)+R*0.08
        if t<0.08: r_*=1+(0.08-t)/0.08*0.28
        ox=lean_x*t*t*1.2; row=[]
        for si in range(SEGS):
            a=2*math.pi*si/SEGS; nr=r_*(1+rng.uniform(-0.03,0.03))
            row.append(bm_.verts.new((math.cos(a)*nr+ox, math.sin(a)*nr, z_)))
        rv.append(row)
    for ri in range(RINGS-1):
        for si in range(SEGS):
            ns=(si+1)%SEGS
            try: f=bm_.faces.new([rv[ri][si],rv[ri][ns],rv[ri+1][ns],rv[ri+1][si]]); f.material_index=0
            except: pass
    cb=bm_.verts.new((0,0,-0.02))
    for si in range(SEGS):
        try: f=bm_.faces.new([cb,rv[0][(si+1)%SEGS],rv[0][si]]); f.material_index=0
        except: pass

    # Ramas medias
    mn=mid["n"]
    for pi in range(mn):
        tp=pi/max(mn-1,1); z0=cz*(mid["z"][0]+tp*(mid["z"][1]-mid["z"][0]))
        azi=2*math.pi*pi/mn+rng.uniform(-0.20,0.20)
        if lean_x!=0: azi+=lean_x*0.35
        elev=math.radians(rng.uniform(*mid["elev"]))
        pd=Vector((math.cos(azi)*math.sin(elev),math.sin(azi)*math.sin(elev),math.cos(elev))).normalized()
        tf_=min(z0/(H*0.62),1.0); ox=lean_x*tf_*tf_*1.2
        _branch(Vector((ox,0,z0)),pd,cr*rng.uniform(mid["l_f"]-0.08,mid["l_f"]+0.08),
                R*rng.uniform(mid["r_f"]-0.07,mid["r_f"]+0.07),1)

    # Ramas altas
    tn=top["n"]
    for pi in range(tn):
        azi=2*math.pi*pi/tn+rng.uniform(-0.22,0.22)
        z0=H*rng.uniform(*top["z"])
        elev=math.radians(rng.uniform(*top["elev"]))
        pd=Vector((math.cos(azi)*math.sin(elev),math.sin(azi)*math.sin(elev),math.cos(elev))).normalized()
        tf_=min(z0/(H*0.62),1.0); ox=lean_x*tf_*tf_*1.2
        _branch(Vector((ox,0,z0)),pd,rng.uniform(*top["l"]),
                R*rng.uniform(top["r_f"]-0.07,top["r_f"]+0.07),1)

    # Mesh
    bm_.verts.ensure_lookup_table(); bm_.faces.ensure_lookup_table()
    me=bpy.data.meshes.new(tag+"_M"); bm_.to_mesh(me); bm_.free()
    for p in me.polygons: p.use_smooth=True
    me.materials.append(bmat); me.materials.append(lmat)
    obj=bpy.data.objects.new(tag,me)
    bpy.context.scene.collection.objects.link(obj)
    obj.location=pos
    lp=sum(1 for f in me.polygons if f.material_index==1)
    print(f"  {tag:<32} | lf={lf:.1f} | {len(me.polygons):>7,} polys | ~{lp//8:>5,} hojas")
    return obj

# --- GENERAR ---
TREES = ''' + repr(TREES) + '''

LODs = [(1.0,"LOD0"),(0.6,"LOD1"),(0.3,"LOD2")]
print("Generando 15 arboles...")
for ti, cfg in enumerate(TREES):
    for li, (lf, ltag) in enumerate(LODs):
        full_cfg = {**cfg, "tag": f"Tree_{cfg[\\'tag\\']}_{ltag}"}
        make_tree(full_cfg, lf, (ti*15.0, li*18.0, 0), bark_mat, leaf_mat)
print("=== COMPLETADO: 15 objetos listos ===")
'''
