"""
WAR OF REALMS — Tree Generator (Blender MCP)
=============================================
Árbol base aprobado: Oak Realista Estilizado
Estilo: RTS Anno/Genshin inspired — stylized realism
Escala real: ~8.5 m de altura
Triángulos LOD0: ~137,511 polys / ~16,950 hojas

PRINCIPIO FUNDAMENTAL:
    Cada cluster de hojas está anclado EXCLUSIVAMENTE en la punta
    de una rama. No existen clusters flotantes sin rama asociada.

Uso:
    1. Copiar y pegar el código de la sección MAIN en Blender MCP
       usando la herramienta execute_blender_code.
    2. Ajustar los PARÁMETROS DE CONFIGURACIÓN según la variante deseada.

Variantes planeadas (misma función, distintos parámetros):
    - Oak (este archivo)       → referencia base aprobada
    - Tall Forest Tree         → H=9.5, corona estrecha
    - Young Dense              → H=6.5, copa compacta
    - Wind Bent                → trunk cx/cy, copa asimétrica
    - Sparse Natural           → menos hojas, ramas visibles
"""

# ============================================================
#  PARÁMETROS DE CONFIGURACIÓN — editar aquí para variantes
# ============================================================
CONFIG = {
    # Árbol
    "name":        "Tree_RealismOak",
    "seed":        2024,

    # Tronco
    "trunk_H":     8.5,          # altura del tronco (m)
    "trunk_R":     0.28,         # radio base del tronco (m)
    "trunk_rings": 23,           # secciones verticales
    "trunk_segs":  10,           # segmentos de circunferencia
    "trunk_taper": 0.65,         # exponente de conicidad (mayor = más lineal)
    "trunk_flare": 0.28,         # factor de ensanchamiento en la base (0 = ninguno)
    "trunk_flare_h": 0.08,       # proporción de altura con raíz expandida

    # Corona
    "crown_z":     4.5,          # altura donde inicia la copa
    "crown_r":     3.8,          # radio de la copa

    # Ramas medias (zona baja-media)
    "mid_branches":   7,         # cantidad de ramas medias
    "mid_elev_min":   38,        # ángulo mínimo desde eje Z (grados)
    "mid_elev_max":   56,        # ángulo máximo desde eje Z (grados)
    "mid_z_min":      0.46,      # fracción del crown_z donde nacen (min)
    "mid_z_max":      0.78,      # fracción del crown_z donde nacen (max)
    "mid_len_factor": 0.50,      # longitud como fracción del crown_r
    "mid_r_factor":   0.45,      # radio base como fracción del trunk_R

    # Ramas altas (zona alta del tronco, apuntan a la copa superior)
    "top_branches":   9,         # cantidad de ramas altas
    "top_elev_min":   12,        # ángulo mínimo desde eje Z (muy vertical)
    "top_elev_max":   30,        # ángulo máximo desde eje Z
    "top_z_min":      0.68,      # fracción del trunk_H donde nacen
    "top_z_max":      0.95,
    "top_len_min":    2.8,       # longitud mínima (m)
    "top_len_max":    4.2,       # longitud máxima (m)
    "top_r_factor":   0.37,      # radio base como fracción del trunk_R
    "top_leaf_scale": 1.06,      # multiplicador de tamaño de hoja (ligeramente mayor)

    # Sistema de ramas recursivo
    "branch_children_l1": (3, 5),   # (min, max) hijos para ramas nivel 1
    "branch_children_l2": (3, 4),   # hijos para ramas nivel 2
    "gravity_l1":  0.14,            # caída gravitacional nivel 1
    "gravity_l2":  0.08,            # caída gravitacional nivel 2
    "gravity_l3":  0.03,            # caída gravitacional ramitas

    # Hojas por cluster (en punta de ramita)
    "leaves_per_cluster": (42, 58),  # (min, max)
    "cluster_radius":     (0.20, 0.30),
    "leaf_size":          0.39,

    # Materiales — colores
    "bark_color1":  (0.25, 0.13, 0.05, 1),   # corteza oscura
    "bark_color2":  (0.55, 0.38, 0.20, 1),   # corteza clara
    "bark_roughness": 0.88,
    "leaf_color":   (0.08, 0.35, 0.06, 1),   # verde saturado
    "leaf_roughness": 0.72,
    "leaf_subsurface": 0.08,
}

# ============================================================
#  MAIN — pegar en execute_blender_code
# ============================================================
BLENDER_SCRIPT = '''
import bpy, bmesh, math, random
from mathutils import Vector

# --- Limpiar árbol anterior si existe ---
for obj in list(bpy.data.objects):
    if obj.type == "MESH" and obj.name.startswith("{name}"):
        bpy.data.objects.remove(obj, do_unlink=True)
for m in list(bpy.data.meshes):
    if m.name.startswith("{name}"):
        bpy.data.meshes.remove(m)

rng = random.Random({seed})
H        = {trunk_H}
R_BASE   = {trunk_R}
CROWN_Z  = {crown_z}
CROWN_R  = {crown_r}
LEAF_SIZE = {leaf_size}

bm = bmesh.new()

# ────────────────────────────────────────────
# FUNCIONES UTILITARIAS
# ────────────────────────────────────────────

def cyl(bm, p0, p1, r0, r1, segs, mat):
    d = p1 - p0; L = d.length
    if L < 0.001: return
    z = d / L
    ref = Vector((0,1,0)) if abs(z.z) > 0.9 else Vector((0,0,1))
    x = z.cross(ref).normalized()
    y = z.cross(x).normalized()
    sv, ev = [], []
    for i in range(segs):
        a = 2*math.pi*i/segs; c, s = math.cos(a), math.sin(a)
        sv.append(bm.verts.new(p0 + (x*c + y*s)*r0))
        ev.append(bm.verts.new(p1 + (x*c + y*s)*r1))
    for i in range(segs):
        ni = (i+1) % segs
        try:
            f = bm.faces.new([sv[i], sv[ni], ev[ni], ev[i]])
            f.material_index = mat
        except: pass

def leaf_at(bm, pos, up, rot, scale):
    u = up.normalized()
    ref = Vector((1,0,0)) if abs(u.z) > 0.9 else Vector((0,0,1))
    r = u.cross(ref).normalized()
    fv = r.cross(u).normalized()
    cr, sr = math.cos(rot), math.sin(rot)
    r2 = r*cr + fv*sr; f2 = -r*sr + fv*cr
    def T(v): return pos + r2*v.x + f2*v.y + u*v.z
    s = scale
    lv = [Vector(p)*s for p in [
        (0,0,0),(-0.13,0,0.12),(-0.155,0.022,0.26),(-0.09,0.042,0.40),
        (0,0.054,0.48),(0.09,0.042,0.40),(0.155,0.022,0.26),(0.13,0,0.12),(0,0.026,0.23)]]
    verts = [bm.verts.new(T(v)) for v in lv]
    for fi in [(0,8,1),(0,7,8),(1,8,2),(7,6,8),(2,8,3),(6,5,8),(3,4,8),(4,5,8)]:
        try:
            f = bm.faces.new([verts[i] for i in fi])
            f.material_index = 1
        except: pass

def cluster_at_tip(bm, tip, direction, rng, leaf_size, n_leaves=50, cluster_r=0.25):
    """Cluster compacto anclado en la punta de una rama terminal."""
    for i in range(n_leaves):
        theta = math.acos(rng.uniform(-0.95, 0.95))
        phi   = rng.uniform(0, 2*math.pi)
        r     = cluster_r * math.pow(rng.uniform(0.30, 1.0), 0.28)
        lx = r*math.sin(theta)*math.cos(phi)
        ly = r*math.sin(theta)*math.sin(phi)
        lz = r*math.cos(theta)
        lpos = tip + Vector((lx, ly, lz))
        ow = Vector((lx, ly, max(lz, 0)+0.04))
        ow = ow.normalized() if ow.length > 0.01 else Vector((0,0,1))
        ld = (ow*0.6 + Vector((0,0,1))*0.4 +
              Vector((rng.uniform(-0.14,0.14), rng.uniform(-0.14,0.14), 0))).normalized()
        leaf_at(bm, lpos, ld, rng.uniform(0, 2*math.pi), rng.uniform(0.82, 1.18)*leaf_size)
    # Sub-cluster lateral para efecto puff natural
    off = Vector((rng.uniform(-1,1), rng.uniform(-1,1), rng.uniform(-0.2, 0.8))).normalized()
    off *= rng.uniform(0.14, 0.24)
    n2 = int(n_leaves * 0.55)
    for i in range(n2):
        theta = math.acos(rng.uniform(-0.95, 0.95))
        phi   = rng.uniform(0, 2*math.pi)
        r     = cluster_r * 0.80 * math.pow(rng.uniform(0.30, 1.0), 0.28)
        lx = r*math.sin(theta)*math.cos(phi)
        ly = r*math.sin(theta)*math.sin(phi)
        lz = r*math.cos(theta)
        lpos = tip + off + Vector((lx, ly, lz))
        ow = Vector((lx, ly, max(lz, 0)+0.04))
        ow = ow.normalized() if ow.length > 0.01 else Vector((0,0,1))
        ld = (ow*0.6 + Vector((0,0,1))*0.4 +
              Vector((rng.uniform(-0.14,0.14), rng.uniform(-0.14,0.14), 0))).normalized()
        leaf_at(bm, lpos, ld, rng.uniform(0, 2*math.pi), rng.uniform(0.82, 1.18)*leaf_size)

def grow_branch(bm, p0, direction, length, radius, level, rng, ls):
    """Crece una rama recursivamente. Las hojas SOLO en puntas de nivel 3."""
    p1 = p0 + direction * length
    gravity = {{1: {gravity_l1}, 2: {gravity_l2}, 3: {gravity_l3}}}.get(level, 0.05)
    p1.z -= length * gravity
    r_end = radius * 0.22
    segs = {{1:8, 2:7, 3:5}}.get(level, 5)
    cyl(bm, p0, p1, radius, r_end, segs, 0)
    if level == 3:
        n_l  = rng.randint({leaves_min}, {leaves_max})
        cr   = rng.uniform({cluster_r_min}, {cluster_r_max})
        cluster_at_tip(bm, p1, direction, rng, ls, n_l, cr)
        return
    n_children = rng.randint(*{{1:{cl1_min},{cl1_max}, 2:{cl2_min},{cl2_max}}}.get(level, (3,4)).values()
    # Simplificado directo:
    n_ch = rng.randint({cl1_min}, {cl1_max}) if level==1 else rng.randint({cl2_min}, {cl2_max})
    for i in range(n_ch):
        ts  = (i+1)/(n_ch+1)
        s0  = p0 + direction * length * (0.25 + ts*0.55)
        perp = direction.cross(Vector((0,0,1)))
        if perp.length < 0.01: perp = direction.cross(Vector((1,0,0)))
        perp = perp.normalized()
        sa  = rng.uniform(0, 2*math.pi)
        ss  = rng.uniform(0.42, 0.70)
        new_dir = (direction*0.60 + perp*math.cos(sa)*ss +
                   direction.cross(perp)*math.sin(sa)*ss).normalized()
        child_len = length * rng.uniform(0.34, 0.52)
        child_r   = r_end  * rng.uniform(1.3, 1.9)
        grow_branch(bm, s0, new_dir, child_len, child_r, level+1, rng, ls)

# ────────────────────────────────────────────
# TRONCO
# ────────────────────────────────────────────
rv = []
for ri in range({trunk_rings}):
    t = ri / ({trunk_rings}-1); z = H*0.62*t
    r = R_BASE * math.pow(1-t, {trunk_taper}) + R_BASE*0.08
    if t < {trunk_flare_h}:
        r *= 1 + ({trunk_flare_h}-t)/{trunk_flare_h} * {trunk_flare}
    row = []
    for si in range({trunk_segs}):
        a  = 2*math.pi*si/{trunk_segs}
        nr = r*(1 + rng.uniform(-0.03, 0.03))
        row.append(bm.verts.new((math.cos(a)*nr, math.sin(a)*nr, z)))
    rv.append(row)
for ri in range({trunk_rings}-1):
    for si in range({trunk_segs}):
        ns = (si+1) % {trunk_segs}
        try:
            f = bm.faces.new([rv[ri][si], rv[ri][ns], rv[ri+1][ns], rv[ri+1][si]])
            f.material_index = 0
        except: pass
cb = bm.verts.new((0,0,-0.02))
for si in range({trunk_segs}):
    try:
        f = bm.faces.new([cb, rv[0][(si+1)%{trunk_segs}], rv[0][si]])
        f.material_index = 0
    except: pass

# ────────────────────────────────────────────
# RAMAS MEDIAS
# ────────────────────────────────────────────
for pi in range({mid_branches}):
    tp   = pi/({mid_branches}-1)
    z0   = CROWN_Z * ({mid_z_min} + tp*({mid_z_max}-{mid_z_min}))
    azi  = 2*math.pi*pi/{mid_branches} + rng.uniform(-0.20, 0.20)
    elev = math.radians(rng.uniform({mid_elev_min}, {mid_elev_max}))
    pd   = Vector((math.cos(azi)*math.sin(elev),
                   math.sin(azi)*math.sin(elev),
                   math.cos(elev))).normalized()
    pl   = CROWN_R * rng.uniform({mid_len_factor}-0.08, {mid_len_factor}+0.08)
    pr0  = R_BASE  * rng.uniform({mid_r_factor}-0.07, {mid_r_factor}+0.07)
    grow_branch(bm, Vector((0,0,z0)), pd, pl, pr0, 1, rng, LEAF_SIZE)

# ────────────────────────────────────────────
# RAMAS ALTAS
# ────────────────────────────────────────────
for pi in range({top_branches}):
    azi  = 2*math.pi*pi/{top_branches} + rng.uniform(-0.22, 0.22)
    z0   = H * rng.uniform({top_z_min}, {top_z_max})
    elev = math.radians(rng.uniform({top_elev_min}, {top_elev_max}))
    pd   = Vector((math.cos(azi)*math.sin(elev),
                   math.sin(azi)*math.sin(elev),
                   math.cos(elev))).normalized()
    pl   = rng.uniform({top_len_min}, {top_len_max})
    pr0  = R_BASE * rng.uniform({top_r_factor}-0.07, {top_r_factor}+0.07)
    grow_branch(bm, Vector((0,0,z0)), pd, pl, pr0, 1, rng, LEAF_SIZE*{top_leaf_scale})

# ────────────────────────────────────────────
# CONSTRUIR OBJETO Y MATERIALES
# ────────────────────────────────────────────
bm.verts.ensure_lookup_table()
bm.faces.ensure_lookup_table()

me = bpy.data.meshes.new("{name}_M")
bm.to_mesh(me); bm.free()
for p in me.polygons: p.use_smooth = True

obj = bpy.data.objects.new("{name}", me)
bpy.context.scene.collection.objects.link(obj)

for mname in ("MyBark","MyLeaf"):
    if mname in bpy.data.materials:
        bpy.data.materials.remove(bpy.data.materials[mname])

bark = bpy.data.materials.new("MyBark")
bark.use_nodes = True; bn = bark.node_tree; bn.nodes.clear()
bsdf = bn.nodes.new("ShaderNodeBsdfPrincipled"); bsdf.location=(400,0)
bsdf.inputs["Roughness"].default_value = {bark_roughness}
bout = bn.nodes.new("ShaderNodeOutputMaterial"); bout.location=(700,0)
bn.links.new(bsdf.outputs["BSDF"], bout.inputs["Surface"])
noise = bn.nodes.new("ShaderNodeTexNoise"); noise.location=(0,100)
noise.inputs["Scale"].default_value=14; noise.inputs["Detail"].default_value=8
cr = bn.nodes.new("ShaderNodeValToRGB"); cr.location=(200,100)
cr.color_ramp.elements[0].color={bark_color1}
cr.color_ramp.elements[1].color={bark_color2}
bn.links.new(noise.outputs["Fac"], cr.inputs["Fac"])
bn.links.new(cr.outputs["Color"], bsdf.inputs["Base Color"])

leaf = bpy.data.materials.new("MyLeaf")
leaf.use_nodes = True; ln = leaf.node_tree; ln.nodes.clear()
lbsdf = ln.nodes.new("ShaderNodeBsdfPrincipled"); lbsdf.location=(400,0)
lbsdf.inputs["Base Color"].default_value  = {leaf_color}
lbsdf.inputs["Roughness"].default_value   = {leaf_roughness}
try: lbsdf.inputs["Subsurface Weight"].default_value = {leaf_subsurface}
except: pass
lout = ln.nodes.new("ShaderNodeOutputMaterial"); lout.location=(700,0)
ln.links.new(lbsdf.outputs["BSDF"], lout.inputs["Surface"])
leaf.use_backface_culling = False

me.materials.append(bark); me.materials.append(leaf)

bpy.context.view_layer.objects.active = obj
bpy.context.scene.cursor.location = (0,0,0)
bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

total = len(me.polygons)
bark_p = sum(1 for f in me.polygons if f.material_index==0)
leaf_p = sum(1 for f in me.polygons if f.material_index==1)
print(f"✓ {{obj.name}} generado | Bark:{bark_p} | Hojas:{leaf_p} (~{{leaf_p//8}}) | Total:{total}")
'''

# ============================================================
#  HELPER — genera el script final reemplazando CONFIG
# ============================================================
def build_script(cfg=None):
    c = {**CONFIG, **(cfg or {})}
    return BLENDER_SCRIPT.format(
        name          = c["name"],
        seed          = c["seed"],
        trunk_H       = c["trunk_H"],
        trunk_R       = c["trunk_R"],
        trunk_rings   = c["trunk_rings"],
        trunk_segs    = c["trunk_segs"],
        trunk_taper   = c["trunk_taper"],
        trunk_flare   = c["trunk_flare"],
        trunk_flare_h = c["trunk_flare_h"],
        crown_z       = c["crown_z"],
        crown_r       = c["crown_r"],
        mid_branches  = c["mid_branches"],
        mid_elev_min  = c["mid_elev_min"],
        mid_elev_max  = c["mid_elev_max"],
        mid_z_min     = c["mid_z_min"],
        mid_z_max     = c["mid_z_max"],
        mid_len_factor= c["mid_len_factor"],
        mid_r_factor  = c["mid_r_factor"],
        top_branches  = c["top_branches"],
        top_elev_min  = c["top_elev_min"],
        top_elev_max  = c["top_elev_max"],
        top_z_min     = c["top_z_min"],
        top_z_max     = c["top_z_max"],
        top_len_min   = c["top_len_min"],
        top_len_max   = c["top_len_max"],
        top_r_factor  = c["top_r_factor"],
        top_leaf_scale= c["top_leaf_scale"],
        gravity_l1    = c["gravity_l1"],
        gravity_l2    = c["gravity_l2"],
        gravity_l3    = c["gravity_l3"],
        leaves_min    = c["leaves_per_cluster"][0],
        leaves_max    = c["leaves_per_cluster"][1],
        cluster_r_min = c["cluster_radius"][0],
        cluster_r_max = c["cluster_radius"][1],
        leaf_size     = c["leaf_size"],
        cl1_min       = c["branch_children_l1"][0],
        cl1_max       = c["branch_children_l1"][1],
        cl2_min       = c["branch_children_l2"][0],
        cl2_max       = c["branch_children_l2"][1],
        bark_color1   = c["bark_color1"],
        bark_color2   = c["bark_color2"],
        bark_roughness= c["bark_roughness"],
        leaf_color    = c["leaf_color"],
        leaf_roughness= c["leaf_roughness"],
        leaf_subsurface=c["leaf_subsurface"],
    )


if __name__ == "__main__":
    print(build_script())
