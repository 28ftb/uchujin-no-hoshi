"""宇宙人の星：主人公アバターの部品キット（キャラクターメイク用）。

形・寸法・部品は NOKA（Documents/ChatGPT/NOKA）の人物モデル `Tools/create-gameplay-assets.py` の
chibi() / face_parts() / townsperson() キット（_hair・_hat・_outfit・_species）をそのまま元にしている。
NOKA では1人ずつ1つの FBX にしていたものを、キャラメイクで組み合わせられるよう部品ごとの FBX に分けた。
外部メッシュ・画像は使わない（すべてこのスクリプトで生成）。

実行（リポジトリのルートで）:
  blender --background --factory-startup --python Tools/Avatar/build_avatar_kit.py -- [部品名 ...]
  blender --background --factory-startup --python Tools/Avatar/build_avatar_kit.py -- --preview
出力:
  Assets/_Project/Resources/Avatar/<部品>.fbx   Unity が Resources.Load("Avatar/<部品>") で読む
  ArtSource/AvatarKit/preview_*.png            確認用レンダー（--preview）

座標は Unity 空間（メートル、+Y 上）で書き、B() で Blender 空間に直す（NOKA と同じ）。モデルは NOKA の住民と同じく
−Z を向く。ゲーム側（AvatarKit.cs）で 180° 回して +Z 向きにする。
部品は NOKA の rig_person と同じ骨組みの空オブジェクトの下に入れる：
  Rig_Body（胴・肩・頭の親）/ Rig_Head / Rig_Hip_P,N（脚・靴）/ Rig_Shoulder_P,N（腕・手）。P/N = Unity の +x / −x 側。
体型ファイル（Body_*）だけが Rig_Body の下に Fit_<x>_<y>_<z>（百分率）を持ち、服の胴まわりの部品はゲーム側でその下に入れて体型に合わせる。
材質名に色の役目を書く：AvTint_<役目>（ゲームがキャラメイクの色で塗る）、AvArt_RRGGBB（固定色）、
AvGlow_RRGGBB（自発光）、AvGlass_RRGGBB（半透明）。
"""
import bpy, bmesh, math, os, sys
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Resources', 'Avatar')
PREVIEW_OUT = os.path.join(ROOT, 'ArtSource', 'AvatarKit')
PARTS = {}
HERO = (40, 26)  # 髪と頭の境目がなめらかになるよう NOKA (28, 18) より細かく

# NOKA の配色（ArtSource/StyleGuide.md「ふっくら・パステル・ちいさな発見」）
CREAM, BUTTER, PEACH, CORAL = 'fbf3e0', 'f6dc8f', 'f8c0a4', 'ee8e7c'
MINT, SAGE, LEAF = 'a8e6cf', '9cc9a0', '7cc08a'
SKY, LAVENDER, LILAC = '9fd3f0', 'c6b5ee', 'e6b8e0'
COCOA, TEAL, INK = '8a6450', '3f6f73', '3a3042'
GLOW_MINT, GLOW_PEACH, GLOW_STAR, SPARK = 'b8f5e0', 'ffd9a8', 'fff4c2', 'fffaf0'
WHITE, SUIT, PANEL = 'fffdf8', 'c9d2de', '3d4660'
CHEEK = 'f6a6a0'

# 役目つきの色（'@役目'）。確認用レンダーでだけ使う色。ゲームではキャラメイクの色に置きかわる。
SLOTS = {
    'skin': 'f8d8bc', 'hair': '8a6450', 'eye': '4a3a5a', 'pupil': '231a2a', 'shirt': SKY, 'sleeve': SKY, 'fluff': 'd8eef8',
    'top': SKY, 'topDark': '7fb0cc', 'legs': '5a6a80', 'bottom': '5a6a80', 'shoes': CORAL, 'shoesDark': 'c86a5a',
    'acc': BUTTER, 'accDark': 'c8a860', 'trait': 'f8d8bc', 'traitDark': 'd8b090',
}
PREVIEW_MODE = False


def part(fn):
    PARTS[fn.__name__] = fn
    return fn


# ---------------------------------------------------------------- helpers（NOKA と同じ）
def B(p):
    return Vector((-p[0], -p[2], p[1]))


def _linear(c):
    return c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4


def mat(color, glow=False):
    """color: 'RRGGBB'、'@役目'（ゲームで塗る）、'glass:RRGGBB'（半透明）。"""
    alpha = 1.0
    if color.startswith('@'):
        slot = color[1:]
        color = SLOTS[slot]
        name = 'AvTint_' + slot + (('_' + color) if PREVIEW_MODE else '')
    elif color.startswith('glass:'):
        color = color[6:]
        name = 'AvGlass_' + color
        alpha = .3
    else:
        name = ('AvGlow_' if glow else 'AvArt_') + color
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    rgb = tuple(_linear(int(color[i:i + 2], 16) / 255) for i in (0, 2, 4))
    m.diffuse_color = rgb + (alpha,)
    try:
        bsdf = m.node_tree.nodes.get('Principled BSDF') if m.node_tree else None
        if bsdf:
            bsdf.inputs['Base Color'].default_value = rgb + (1,)
            bsdf.inputs['Roughness'].default_value = .6
            if alpha < 1:
                bsdf.inputs['Alpha'].default_value = alpha
                m.blend_method = 'BLEND'
            if glow:
                bsdf.inputs['Emission Color'].default_value = rgb + (1,)
                bsdf.inputs['Emission Strength'].default_value = .45
    except Exception:
        pass
    return m


def _finish(o, name, color, parent=None, rot=None, glow=False, smooth=False, bevel=0.0, subdiv=0, segs=3):
    o.name = name
    o.data.name = name
    if rot:
        o.rotation_euler = (math.radians(rot[0]), math.radians(rot[2]), math.radians(-rot[1]))
    o.data.materials.clear()
    o.data.materials.append(mat(color, glow))
    for f in o.data.polygons:
        f.use_smooth = smooth and (abs(f.normal.z) < .92 or subdiv > 0 or len(f.vertices) == 3)
    if bevel > 0:
        m = o.modifiers.new('Bevel', 'BEVEL')
        m.width = bevel
        m.segments = segs
        m.limit_method = 'ANGLE'
        for f in o.data.polygons:
            f.use_smooth = True
    if subdiv:
        m = o.modifiers.new('Subdivision', 'SUBSURF')
        m.levels = subdiv
        m.render_levels = subdiv
    if parent:
        attach(o, parent)
    return o


def _bake_scale(o, scale):
    o.scale = scale
    bpy.context.view_layer.objects.active = o
    for s in bpy.context.view_layer.objects.selected:
        s.select_set(False)
    o.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)


def box(name, pos, size, color, parent=None, rot=None, bevel=None, glow=False, segs=3):
    bpy.ops.mesh.primitive_cube_add(size=1, location=B(pos))
    o = bpy.context.object
    _bake_scale(o, (size[0], size[2], size[1]))
    if bevel is None:
        bevel = min(.12, min(size) * .3)
    return _finish(o, name, color, parent, rot, glow, False, min(bevel, min(size) * .45), segs=segs)


def ball(name, pos, size, color, parent=None, rot=None, glow=False, segments=20, rings=12, base=False, subdiv=0):
    if subdiv:
        segments, rings, subdiv = max(segments, HERO[0]), max(rings, HERO[1]), 0
    elif segments >= 10:
        rings = max(rings, 8)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=.5, location=B(pos))
    o = bpy.context.object
    _bake_scale(o, (size[0], size[2], size[1]))
    if base:
        for v in o.data.vertices:
            v.co.z += size[1] / 2
    return _finish(o, name, color, parent, rot, glow, True, 0, subdiv)


def cyl(name, pos, radius, height, color, parent=None, rot=None, top=None, verts=24, glow=False, bevel=None, smooth=True):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=radius, radius2=radius if top is None else top,
                                    depth=height, location=B(pos))
    if bevel is None:
        bevel = min(.06, height * .3, radius * .3) if verts > 5 else 0
    return _finish(bpy.context.object, name, color, parent, rot, glow, smooth and verts > 6, bevel)


def torus(name, pos, major, minor, color, parent=None, rot=None, glow=False, segments=40, half=False):
    bpy.ops.mesh.primitive_torus_add(major_segments=segments, minor_segments=10, major_radius=major,
                                     minor_radius=minor, location=B(pos))
    o = _finish(bpy.context.object, name, color, parent, rot, glow, True)
    if half:
        bpy.context.view_layer.update()
        mw = o.matrix_world
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if (mw @ v.co).z < B(pos).z - minor], context='VERTS')
        bm.to_mesh(o.data)
        bm.free()
    return o


def limb(name, a, b, radius, color, parent=None, tip=None, verts=12, glow=False):
    A, Bv = B(a), B(b)
    d = Bv - A
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=radius, radius2=radius if tip is None else tip,
                                    depth=d.length, location=(A + Bv) / 2)
    o = bpy.context.object
    o.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
    o = _finish(o, name, color, parent, None, glow, True)
    for end, r in ((a, radius), (b, radius if tip is None else tip)):
        if r > .012:
            ball(name + '_Cap', end, (r * 2, r * 2, r * 2), color, parent, glow=glow, segments=10, rings=6)
    return o


def mesh(name, pos, verts, faces, color, parent=None, rot=None, glow=False, smooth=False, bevel=0.0):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    vs = [bm.verts.new(B(v)) for v in verts]
    for f in faces:
        bm.faces.new([vs[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.location = B(pos)
    return _finish(o, name, color, parent, rot, glow, smooth, bevel, segs=2)


def star(name, pos, outer, color, parent=None, rot=None, glow=True, depth=.04, points=5):
    inner = outer * .5
    ring = []
    for k in range(points * 2):
        a = math.pi / 2 + k * math.pi / points
        r = outer if k % 2 == 0 else inner
        ring.append((math.cos(a) * r, math.sin(a) * r))
    v = [(x, y, -depth / 2) for x, y in ring] + [(x, y, depth / 2) for x, y in ring] + [(0, 0, -depth), (0, 0, depth)]
    n = len(ring)
    f = []
    for k in range(n):
        j = (k + 1) % n
        f.append((k, j, j + n, k + n))
        f.append((2 * n, j, k))
        f.append((2 * n + 1, k + n, j + n))
    return mesh(name, pos, v, f, color, parent, rot, glow, smooth=False, bevel=depth * .3)


def empty(name, pos=(0, 0, 0), parent=None):
    o = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(o)
    o.location = B(pos)
    o.empty_display_size = .1
    if parent:
        attach(o, parent)
    return o


def attach(child, parent):
    bpy.context.view_layer.update()
    world = child.matrix_world.copy()
    child.parent = parent
    child.matrix_world = world


def arc(name, pos, major, minor, color, down=False, segments=20):
    """顔の弧：∩（にっこり閉じ目）、down=True で ∪。−Z（顔の前）向き。"""
    o = torus(name, pos, major, minor, color, rot=(90, 0, 0), half=True, segments=segments)
    if down:
        o.rotation_euler[1] += math.pi
    return o


def eyes(cx, cy, cz, spread, height, face, prefix='Eye', sparkle=True):
    """NOKA の大きなつや目（ゲームで目の色に塗る）＋瞳＋2つの光。face=-1 で −Z 向き。"""
    for s in (-1, 1):
        x = cx + s * spread
        one_eye(x, cy, cz, height, face, prefix, sparkle)


def one_eye(x, cy, cz, height, face, prefix='Eye', sparkle=True):
    ball(prefix, (x, cy, cz), (height * .62, height, height * .32), '@eye', segments=16, rings=10)
    ball(prefix + '_Pupil', (x, cy - height * .06, cz + face * height * .08), (height * .4, height * .62, height * .22), '@pupil',
         segments=12, rings=8)
    if sparkle:
        ball(prefix + '_Sparkle', (x - height * .1, cy + height * .2, cz + face * height * .16),
             (height * .3, height * .3, height * .1), SPARK, glow=True, segments=8, rings=6)
        ball(prefix + '_Sparkle', (x + height * .12, cy - height * .2, cz + face * height * .16),
             (height * .14, height * .14, height * .06), SPARK, glow=True, segments=6, rings=4)


# ---------------------------------------------------------------- 骨組みと仕分け
HEAD_Y = 1.02           # NOKA chibi の頭の中心
F = -1                  # 顔の向き（−Z）
BODIES = {
    # 体型: wide = 腕と胴の横幅, body = 胴の楕円の直径, fit = 服の胴まわりの部品の拡大率（x, y, z）
    'Normal': dict(wide=1.0, body=(.48, .5, .42), body_y=.45, fit=(1, 1, 1)),
    'Slim': dict(wide=.86, body=(.41, .5, .36), body_y=.45, fit=(.86, 1, .86)),
    'Sturdy': dict(wide=1.18, body=(.57, .52, .5), body_y=.46, fit=(1.18, 1.04, 1.19)),
    'Round': dict(wide=1.3, body=(.66, .6, .6), body_y=.47, fit=(1.36, 1.14, 1.42)),
    'Fluffy': dict(wide=1.06, body=(.5, .5, .44), body_y=.45, fit=(1.1, 1.02, 1.1), fluff=True),
}
HIP_PARTS = ('Shoe', 'Leg', 'Foot', 'Sole', 'Boot', 'Sandal', 'Shorts', 'Knee', 'Moon')
ARM_PARTS = ('Arm', 'Hand', 'Sleeve', 'Glove', 'Cuff')
HEAD_DOWN = ('Ponytail', 'Twin_Tail', 'Side_Braid', 'Long_Hair', 'Hair_Tie', 'Fin_Ear', 'Long_Ear')
NOT_HEAD = ('Helmet_Collar', 'Scarf', 'Hood', 'Turtleneck', 'Collar', 'Neck_Ring', 'Cape', 'Tail')


def make_rig(spec):
    w = spec['wide']
    body = empty('Rig_Body', (0, .3, 0))
    pivots = {'body': body, 'head': empty('Rig_Head', (0, .74, 0), body)}
    for s in (-1, 1):
        tag = 'P' if s > 0 else 'N'
        pivots['hip' + tag] = empty('Rig_Hip_' + tag, (s * .1, .3, 0))
        pivots['shoulder' + tag] = empty('Rig_Shoulder_' + tag, (s * .2 * w, .58, 0), body)
    return pivots


def unity_centre(o):
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    p = sum(pts, Vector()) / 8
    return Vector((-p.x, p.z, -p.y))


def classify(o):
    base = o.name.split('.')[0]
    c = unity_centre(o)
    side = 'P' if c.x >= 0 else 'N'
    if base.startswith(HIP_PARTS):
        return 'hip' + side
    if base.startswith(ARM_PARTS):
        return 'shoulder' + side
    if base.startswith(NOT_HEAD):
        return 'body'
    if base.startswith(HEAD_DOWN) or c.y > .74:
        return 'head'
    return 'body'


def rig(objects, pivots):
    for o in objects:
        if o.parent is None and o.type == 'MESH':
            attach(o, pivots[classify(o)])


# ---------------------------------------------------------------- 体型（Body_*）
def body_base(name):
    spec = BODIES[name]
    w = spec['wide']
    pivots = make_rig(spec)
    for s in (-1, 1):
        cyl('Leg', (s * .1, .2, 0), .065, .18, '@legs', verts=12)
    ball('Body', (0, spec['body_y'], 0), spec['body'], '@shirt', subdiv=1)
    for s in (-1, 1):
        limb('Arm', (s * .2 * w, .58, 0), (s * .29 * w, .38, F * .05), .07, '@sleeve', verts=10)
        ball('Hand', (s * .31 * w, .34, F * .06), (.14, .14, .14), '@skin', segments=12, rings=8)
    ball('Head', (0, HEAD_Y, 0), (.8, .74, .72), '@skin', subdiv=1)
    if spec.get('fluff'):
        for k in range(10):
            a = k * math.tau / 10
            ball('Fluff', (math.sin(a) * .24, .3 + (k % 2) * .05, math.cos(a) * .21), (.17, .14, .17), '@fluff', segments=12, rings=8)
        for k in range(6):
            a = k * math.tau / 6 + .3
            ball('Fluff', (math.sin(a) * .2, .66, math.cos(a) * .17), (.14, .1, .14), '@fluff', segments=10, rings=6)
    fx, fy, fz = spec['fit']
    empty('Fit_%d_%d_%d' % (round(fx * 100), round(fy * 100), round(fz * 100)), (0, .3, 0), pivots['body'])
    return pivots


for _n in BODIES:
    PARTS['Body_' + _n] = (lambda n: (lambda: body_base(n)))(_n)


# ---------------------------------------------------------------- 目（Eyes_*）
ey = HEAD_Y - .05


@part
def Eyes_Round():
    eyes(0, ey, F * .325, .14, .16, F)


@part
def Eyes_Droopy():
    eyes(0, ey - .01, F * .325, .14, .15, F)
    for s in (-1, 1):
        limb('Eye_Lid', (s * .08, ey + .1, F * .33), (s * .21, ey + .055, F * .3), .014, INK, verts=6)


@part
def Eyes_Sharp():
    eyes(0, ey, F * .33, .14, .13, F)
    for s in (-1, 1):
        limb('Eye_Lid', (s * .08, ey + .055, F * .335), (s * .21, ey + .1, F * .3), .015, INK, verts=6)


@part
def Eyes_Sparkly():
    eyes(0, ey, F * .325, .145, .18, F)
    for s in (-1, 1):
        star('Eye_Sparkle', (s * .145 + .03, ey - .02, F * .372), .028, SPARK, rot=(0, 0, 0), depth=.012)


@part
def Eyes_Happy():
    for s in (-1, 1):
        arc('Eye', (s * .14, ey - .03, F * .338), .05, .016, INK)


@part
def Eyes_Dots():
    for s in (-1, 1):
        ball('Eye', (s * .13, ey, F * .34), (.06, .075, .03), '@pupil', segments=10, rings=8)


@part
def Eyes_Big():
    eyes(0, ey + .01, F * .32, .145, .21, F)


@part
def Eyes_One():
    one_eye(0, ey + .02, F * .315, .26, F)
    limb('Eye_Lid', (-.1, ey + .17, F * .32), (.1, ey + .17, F * .32), .014, INK, verts=6)


@part
def Eyes_Three():
    eyes(0, ey - .01, F * .33, .15, .14, F)
    one_eye(0, ey + .15, F * .33, .1, F, sparkle=False)
    ball('Eye_Sparkle', (-.012, ey + .17, F * .345), (.03, .03, .01), SPARK, glow=True, segments=6, rings=4)


@part
def Eyes_Sleepy():
    for s in (-1, 1):
        arc('Eye', (s * .14, ey + .02, F * .338), .05, .016, INK, down=True)


# ---------------------------------------------------------------- 顔（Face_*：口・ほっぺ・眉・もよう）
my = HEAD_Y - .17


def cheeks(color=CHEEK, size=(.12, .06, .04)):
    for s in (-1, 1):
        ball('Cheek', (s * .23, HEAD_Y - .14, F * .29), size, color, segments=10, rings=6)


@part
def Face_Smile():
    arc('Mouth', (0, my + .028, F * .33), .045, .012, 'a05a5a', down=True)
    cheeks()


@part
def Face_Open():
    ball('Mouth', (0, my, F * .335), (.09, .07, .03), '8a3f45', segments=12, rings=8)
    ball('Mouth_Tongue', (0, my - .016, F * .345), (.05, .026, .02), 'f08a8a', segments=10, rings=6)
    cheeks()


@part
def Face_Calm():
    ball('Mouth', (0, my, F * .345), (.07, .028, .02), 'a05a5a', segments=10, rings=6)


@part
def Face_Cat():
    for s in (-1, 1):
        arc('Mouth', (s * .022, my + .016, F * .332), .022, .01, 'a05a5a', down=True, segments=14)
    cheeks()


@part
def Face_Smirk():
    o = arc('Mouth', (.03, my + .026, F * .33), .04, .012, 'a05a5a', down=True)
    o.rotation_euler[1] += math.radians(14)
    ball('Mouth_Dimple', (.08, my + .03, F * .322), (.02, .02, .01), 'c98a8a', segments=6, rings=4)


@part
def Face_Rosy():
    ball('Mouth', (0, my, F * .345), (.06, .026, .02), 'a05a5a', segments=10, rings=6)
    cheeks('f48a9a', (.15, .08, .04))


@part
def Face_Freckles():
    arc('Mouth', (0, my + .028, F * .33), .045, .012, 'a05a5a', down=True)
    for s in (-1, 1):
        for k in range(3):
            ball('Freckle', (s * (.19 + k * .035), HEAD_Y - .1 - (k % 2) * .025, F * .305), (.022, .022, .012), 'c98a6a', segments=6, rings=4)


@part
def Face_Brows():
    ball('Mouth', (0, my, F * .345), (.07, .028, .02), 'a05a5a', segments=10, rings=6)
    for s in (-1, 1):
        limb('Brow', (s * .07, ey + .15, F * .335), (s * .21, ey + .17, F * .3), .019, '@hair', verts=6)


@part
def Face_Tiny():
    ball('Mouth', (0, my, F * .34), (.045, .05, .02), '8a3f45', segments=10, rings=6)
    cheeks()


@part
def Face_Stars():
    arc('Mouth', (0, my + .028, F * .33), .045, .012, 'a05a5a', down=True)
    for s in (-1, 1):
        star('Cheek_Star', (s * .24, HEAD_Y - .12, F * .3), .045, GLOW_STAR, rot=(0, s * 30, 0), depth=.02)


# ---------------------------------------------------------------- 髪（Hair_*）NOKA の _hair
y = HEAD_Y
H = '@hair'


def hair_cap(bangs=True):
    ball('Hair', (0, y + .12, .05), (.84, .6, .74), H, subdiv=1)
    if bangs:
        for k in range(4):
            ball('Hair_Bang', (-.21 + k * .14, y + .2, -.28), (.22, .16, .12), H, segments=12, rings=8)


@part
def Hair_Short():
    hair_cap()


@part
def Hair_Bob():
    ball('Bob_Hair', (0, y + .04, .05), (.9, .66, .8), H, subdiv=1)
    for k in range(5):
        ball('Bob_Bang', (-.24 + k * .12, y + .2, -.29), (.16, .12, .1), H, segments=10, rings=6)


@part
def Hair_Long():
    hair_cap()
    ball('Long_Hair', (0, y - .14, .2), (.82, .78, .42), H, segments=18, rings=10)
    for s in (-1, 1):
        ball('Long_Hair_Side', (s * .38, y - .12, -.02), (.16, .5, .22), H, segments=12, rings=8)


@part
def Hair_Ponytail():
    hair_cap()
    for k in range(4):
        ball('Ponytail', (0, y + .12 - k * .14, .38 + k * .03), (.2 - k * .02, .18, .18), H, segments=12, rings=8)
    ball('Hair_Tie', (0, y + .2, .36), (.12, .08, .08), '@acc', segments=10, rings=6)


@part
def Hair_Topbun():
    hair_cap()
    ball('Hair_Topbun', (0, y + .42, .12), (.3, .28, .28), H, segments=14, rings=8)


@part
def Hair_Buns():
    hair_cap()
    for s in (-1, 1):
        ball('Hair_Bun', (s * .36, y + .24, .04), (.28, .28, .28), H, segments=14, rings=8)


@part
def Hair_Braids():
    hair_cap()
    for s in (-1, 1):
        for k in range(4):
            ball('Side_Braid', (s * .34, y - .1 - k * .12, -.04), (.13 - k * .01, .13, .12), H, segments=10, rings=6)
        ball('Hair_Tie', (s * .34, y - .53, -.04), (.08, .05, .08), '@acc', segments=8, rings=4)


@part
def Hair_Spiky():
    hair_cap()
    for k in range(7):
        a = -60 + k * 20
        cyl('Hair_Spike', (math.sin(math.radians(a)) * .28, y + .32, .04 + math.cos(math.radians(a)) * .1), .09, .22, H,
            top=.01, verts=8, rot=(10, 0, -a * .6))


@part
def Hair_Afro():
    ball('Hair', (0, y + .2, .13), (1.04, .9, .86), H, subdiv=1)


@part
def Hair_Curls():
    for k in range(8):
        a = k * math.tau / 8
        ball('Hair_Curl', (math.sin(a) * .34, y + .08 + (k % 2) * .08, math.cos(a) * .3 + .04), (.18, .18, .18), H, segments=10, rings=6)
    ball('Hair_Curl', (0, y + .34, .04), (.3, .2, .3), H, segments=12, rings=6)
    ball('Hair_Curl', (0, y + .18, .1), (.66, .4, .56), H, segments=16, rings=8)


@part
def Hair_Sidepart():
    ball('Hair', (0, y + .12, .05), (.84, .6, .74), H, subdiv=1)
    ball('Hair_Swoop', (-.14, y + .2, -.28), (.42, .16, .14), H, rot=(0, 0, -12), segments=14, rings=8)
    for k in range(2):
        ball('Hair_Bang', (.12 + k * .12, y + .2, -.28), (.16, .13, .1), H, segments=10, rings=6)


@part
def Hair_Mohawk():
    hair_cap()
    for k in range(5):
        ball('Hair_Crest', (0, y + .4 - abs(k - 2) * .03, -.16 + k * .1), (.1, .2, .12), H, segments=10, rings=6)


@part
def Hair_Twintails():
    hair_cap()
    for s in (-1, 1):
        ball('Hair_Tie', (s * .4, y + .16, .08), (.1, .1, .1), '@acc', segments=10, rings=6)
        for k in range(4):
            ball('Twin_Tail', (s * (.46 + k * .02), y + .06 - k * .13, .1 + k * .02), (.18 - k * .02, .17, .17), H, segments=12, rings=8)


@part
def Hair_Feelers():
    hair_cap()
    for s in (-1, 1):
        limb('Hair_Feeler', (s * .2, y + .3, -.22), (s * .3, y + .02, -.36), .03, H, tip=.02, verts=8)
        ball('Hair_Feeler_Tip', (s * .31, y - .03, -.36), (.08, .08, .08), H, segments=10, rings=6)


@part
def Hair_Sides():
    for s in (-1, 1):
        ball('Hair_Tuft', (s * .36, y + .02, .06), (.16, .22, .26), H, segments=12, rings=8)


# ---------------------------------------------------------------- 上着（Top_*）
T, TD = '@top', '@topDark'


def short_sleeves(color='@shirt'):
    for s in (-1, 1):
        ball('Sleeve', (s * .22, .54, -.005), (.17, .16, .17), color, segments=12, rings=8)


@part
def Top_Tshirt():
    short_sleeves()
    torus('Collar', (0, .69, 0), .14, .022, TD, segments=24)


@part
def Top_Hoodie():
    ball('Hood', (0, .67, .2), (.5, .22, .26), TD, segments=16, rings=8)
    for s in (-1, 1):
        limb('Hood_String', (s * .05, .66, -.21), (s * .06, .52, -.225), .012, WHITE, verts=6)
    box('Pocket', (0, .36, -.2), (.24, .1, .04), TD, bevel=.02)


@part
def Top_Jacket():
    box('Jacket_Inner', (0, .5, -.205), (.1, .3, .02), WHITE, bevel=.01)
    for s in (-1, 1):
        ball('Collar_Lapel', (s * .09, .6, -.2), (.11, .18, .04), TD, rot=(0, 0, s * 22), segments=8, rings=6)
    for k in range(2):
        ball('Jacket_Button', (.07, .44 - k * .09, -.215), (.03, .03, .015), BUTTER, segments=6, rings=4)


@part
def Top_Spacesuit():
    torus('Neck_Ring', (0, .69, 0), .17, .035, SUIT, segments=28)
    box('Suit_Panel', (0, .5, -.2), (.2, .12, .04), PANEL, bevel=.015)
    ball('Suit_Led', (-.05, .52, -.225), (.04, .04, .02), GLOW_MINT, glow=True, segments=8, rings=4)
    ball('Suit_Led', (.05, .52, -.225), (.04, .04, .02), GLOW_PEACH, glow=True, segments=8, rings=4)
    torus('Suit_Belt', (0, .32, 0), .25, .025, SUIT, segments=28)
    for s in (-1, 1):
        ball('Glove', (s * .31, .34, F * .06), (.16, .16, .16), WHITE, segments=12, rings=8)
        torus('Cuff', (s * .3, .4, -.045), .065, .016, SUIT, rot=(0, 0, s * 18), segments=16)


@part
def Top_Dress():
    short_sleeves()
    cyl('Dress_Skirt', (0, .25, 0), .36, .22, T, top=.2, verts=28)
    torus('Collar', (0, .69, 0), .14, .022, WHITE, segments=24)


@part
def Top_Sweater():
    torus('Turtleneck', (0, .68, 0), .15, .05, TD, segments=24)
    torus('Sweater_Hem', (0, .26, 0), .2, .03, TD, segments=24)
    for k in range(3):
        box('Sweater_Stripe', (0, .44 + k * .06, -.205), (.26 - abs(k - 1) * .02, .018, .02), TD, bevel=.006)


@part
def Top_Overalls():
    box('Overall_Bib', (0, .5, -.19), (.24, .2, .05), T, bevel=.02)
    for s in (-1, 1):
        limb('Overall_Strap', (s * .1, .58, -.2), (s * .13, .68, .02), .022, T, verts=6)
        ball('Overall_Button', (s * .09, .58, -.22), (.045, .045, .02), BUTTER, segments=8, rings=4)
    ball('Overall_Seat', (0, .3, 0), (.5, .2, .44), T, segments=20, rings=10)


@part
def Top_Cape():
    ball('Cape', (0, .5, .2), (.6, .62, .2), T, segments=16, rings=10)
    ball('Cape_Clasp', (0, .66, -.2), (.08, .08, .04), BUTTER, segments=8, rings=4)
    torus('Collar', (0, .69, 0), .15, .03, T, segments=24)


@part
def Top_Vest():
    ball('Vest', (0, .47, 0), (.52, .46, .46), T, subdiv=1)
    for k in range(3):
        ball('Vest_Button', (0, .38 + k * .08, -.235), (.03, .03, .015), BUTTER, segments=6, rings=4)


@part
def Top_Poncho():
    cyl('Poncho', (0, .52, 0), .38, .36, T, top=.14, verts=12)
    torus('Poncho_Trim', (0, .35, 0), .36, .025, TD, segments=12)


@part
def Top_Labcoat():
    ball('Coat', (0, .42, 0), (.54, .48, .48), WHITE, subdiv=1)
    cyl('Coat_Tail', (0, .25, 0), .3, .14, WHITE, top=.25, verts=24)
    for s in (-1, 1):
        ball('Collar_Lapel', (s * .07, .6, -.21), (.1, .16, .03), T, rot=(0, 0, s * 20), segments=8, rings=6)
        ball('Sleeve', (s * .24, .5, -.02), (.16, .2, .16), WHITE, segments=12, rings=8)


@part
def Top_Apron():
    ball('Apron', (0, .42, -.17), (.38, .4, .12), T, segments=16, rings=8)
    box('Apron_Pocket', (0, .36, -.235), (.14, .06, .02), TD, bevel=.008)
    torus('Apron_Tie', (0, .5, 0), .24, .016, T, segments=24)


# ---------------------------------------------------------------- 下衣（Bottom_*）
BT = '@bottom'


def pants_seat():
    ball('Pants_Seat', (0, .3, 0), (.5, .2, .44), BT, segments=20, rings=10)


@part
def Bottom_Trousers():
    pants_seat()


@part
def Bottom_Shorts():
    pants_seat()
    for s in (-1, 1):
        cyl('Shorts_Leg', (s * .1, .24, 0), .085, .1, BT, verts=14)


@part
def Bottom_Skirt():
    cyl('Skirt', (0, .26, 0), .32, .16, BT, top=.22, verts=28)


@part
def Bottom_Puffy():
    ball('Puffy_Skirt', (0, .29, 0), (.66, .26, .6), BT, segments=24, rings=12)
    torus('Puffy_Frill', (0, .2, 0), .29, .022, WHITE, segments=32)


@part
def Bottom_Spacepants():
    pants_seat()
    for s in (-1, 1):
        torus('Knee_Ring', (s * .1, .18, 0), .072, .018, SUIT, segments=16)


@part
def Bottom_Leggings():
    torus('Legging_Band', (0, .28, 0), .21, .02, BT, segments=24)


# ---------------------------------------------------------------- 靴（Shoes_*）
SH = '@shoes'


@part
def Shoes_Sneakers():
    for s in (-1, 1):
        ball('Shoe', (s * .1, .07, F * .03), (.19, .14, .25), SH, segments=14, rings=8)
        ball('Sole', (s * .1, .02, F * .03), (.2, .04, .26), WHITE, segments=14, rings=6)


@part
def Shoes_Boots():
    for s in (-1, 1):
        ball('Shoe', (s * .1, .07, F * .03), (.2, .15, .26), SH, segments=14, rings=8)
        cyl('Boot_Shaft', (s * .1, .17, 0), .08, .14, SH, verts=14)
        torus('Boot_Rim', (s * .1, .24, 0), .078, .014, '@shoesDark', segments=16)


@part
def Shoes_Sandals():
    for s in (-1, 1):
        ball('Foot', (s * .1, .06, F * .03), (.16, .1, .22), '@skin', segments=12, rings=8)
        box('Sandal_Sole', (s * .1, .015, F * .03), (.18, .03, .26), SH, bevel=.01)
        torus('Sandal_Strap', (s * .1, .07, F * .06), .07, .014, SH, rot=(0, 0, 90), segments=14)


@part
def Shoes_Moonboots():
    for s in (-1, 1):
        ball('Moon_Boot', (s * .1, .09, F * .02), (.24, .2, .3), SH, segments=16, rings=10)
        torus('Moon_Boot_Ring', (s * .1, .17, 0), .085, .02, WHITE, segments=18)


@part
def Shoes_Barefoot():
    for s in (-1, 1):
        ball('Foot', (s * .1, .06, F * .03), (.16, .1, .22), '@skin', segments=12, rings=8)


# ---------------------------------------------------------------- こもの（Acc_*）NOKA の _hat ほか
A, AD = '@acc', '@accDark'


@part
def Acc_Cap():
    cyl('Cap', (0, y + .34, 0), .37, .16, A, top=.33, verts=26)
    ball('Cap_Visor', (0, y + .28, -.33), (.38, .05, .2), AD, segments=14, rings=6)


@part
def Acc_Goggles():
    torus('Goggle_Band', (0, y + .2, .04), .385, .024, COCOA, segments=32)
    for s in (-1, 1):
        torus('Goggle_Rim', (s * .14, y + .25, -.35), .085, .03, A, rot=(80, 0, 0), segments=20)
        cyl('Goggle_Lens', (s * .14, y + .25, -.35), .08, .02, GLOW_MINT, rot=(80, 0, 0), glow=True)


@part
def Acc_Ribbon():
    for s in (-1, 1):
        ball('Ribbon', (.24 + s * .09, y + .34, -.04), (.16, .12, .06), A, rot=(0, 0, s * 18), segments=12, rings=8)
    ball('Ribbon_Knot', (.24, y + .34, -.06), (.06, .06, .05), AD, segments=8, rings=6)


@part
def Acc_Beanie():
    cyl('Beanie', (0, y + .3, 0), .4, .22, A, top=.3, verts=26)
    torus('Beanie_Rim', (0, y + .21, 0), .36, .05, CREAM, segments=26)
    ball('Beanie_Pom', (0, y + .45, 0), (.16, .16, .16), CREAM, segments=10, rings=6)


@part
def Acc_Glasses():
    for s in (-1, 1):
        torus('Glasses_Rim', (s * .14, y - .05, -.355), .08, .013, A, rot=(90, 0, 0), segments=20)
        cyl('Glasses_Lens', (s * .14, y - .05, -.352), .072, .006, 'glass:d8f0ff', rot=(90, 0, 0), verts=16)
    limb('Glasses_Bridge', (-.06, y - .04, -.36), (.06, y - .04, -.36), .01, A, verts=6)


@part
def Acc_Headphones():
    torus('Headset_Band', (0, y + .02, 0), .41, .03, AD, rot=(0, 0, 90), half=True, segments=24)
    for s in (-1, 1):
        ball('Headset_Ear', (s * .4, y - .02, 0), (.12, .18, .18), A, segments=12, rings=8)


@part
def Acc_Flowers():
    for k in range(9):
        a = math.radians(-160 + k * 40)
        ball('Flower_Crown', (math.sin(a) * .36, y + .27, math.cos(a) * .32), (.11, .11, .11), [A, BUTTER, LILAC, WHITE][k % 4],
             segments=10, rings=6)


@part
def Acc_Helmet():
    ball('Helmet_Dome', (0, y + .04, 0), (1.06, 1.0, 1.0), 'glass:cfefff', segments=28, rings=16)
    torus('Helmet_Collar', (0, .7, 0), .26, .05, A, segments=28)
    ball('Helmet_Light', (.3, y + .36, -.2), (.06, .06, .06), GLOW_MINT, glow=True, segments=8, rings=6)


@part
def Acc_CatEars():
    torus('CatEar_Band', (0, y + .1, -.02), .4, .02, AD, rot=(0, 0, 90), half=True, segments=24)
    for s in (-1, 1):
        cyl('CatEar', (s * .24, y + .38, -.02), .1, .16, A, top=.01, verts=4, rot=(0, 45, s * -18), bevel=.01)
        cyl('CatEar_Inner', (s * .24, y + .37, -.06), .055, .1, 'f6a6b0', top=.01, verts=4, rot=(0, 45, s * -18), bevel=.005)


@part
def Acc_Crown():
    cyl('Crown', (0, y + .36, .02), .22, .1, A, top=.24, verts=20)
    for k in range(5):
        a = k * math.tau / 5
        cyl('Crown_Point', (math.sin(a) * .22, y + .45, .02 + math.cos(a) * .22), .04, .1, A, top=.005, verts=8)
    ball('Crown_Gem', (0, y + .36, -.21), (.07, .07, .04), 'ff7aa8', glow=True, segments=8, rings=6)


@part
def Acc_Scarf():
    ball('Scarf', (0, .66, 0), (.44, .12, .4), A, segments=16, rings=6)
    ball('Scarf_Tail', (.1, .54, -.2), (.08, .2, .04), A, rot=(0, 0, 12), segments=8, rings=4)


@part
def Acc_StarPin():
    star('Star_Pin', (.25, y + .2, -.3), .07, A, rot=(0, -30, 0), glow=False, depth=.03)
    ball('Star_Pin_Glint', (.25, y + .21, -.33), (.03, .03, .01), SPARK, glow=True, segments=6, rings=4)


@part
def Acc_Beret():
    ball('Beret', (.04, y + .36, .02), (.66, .2, .6), A, rot=(0, 0, -10), segments=20, rings=10)
    ball('Beret_Nub', (.06, y + .47, .02), (.06, .06, .06), AD, segments=8, rings=4)


@part
def Acc_Straw():
    cyl('Straw_Brim', (0, y + .29, 0), .56, .04, 'f2d48a', verts=32)
    cyl('Straw_Crown', (0, y + .39, 0), .3, .18, 'f2d48a', top=.26, verts=26)
    torus('Straw_Band', (0, y + .33, 0), .295, .028, A, segments=28)


@part
def Acc_Tophat():
    cyl('Top_Hat_Brim', (0, y + .3, 0), .46, .04, A, verts=28)
    cyl('Top_Hat', (0, y + .5, 0), .28, .38, A, top=.3, verts=26)
    torus('Top_Hat_Band', (0, y + .36, 0), .285, .03, AD, segments=26)
    star('Top_Hat_Star', (.12, y + .44, -.27), .06, GLOW_STAR)


@part
def Acc_Bucket():
    cyl('Bucket_Hat', (0, y + .36, 0), .34, .2, A, top=.3, verts=28)
    cyl('Bucket_Brim', (0, y + .26, 0), .5, .05, A, top=.4, verts=32)


@part
def Acc_Turban():
    ball('Turban', (0, y + .3, .02), (.82, .42, .76), A, segments=20, rings=10)
    torus('Turban_Wrap', (0, y + .24, .02), .38, .06, AD, segments=26)
    ball('Turban_Gem', (0, y + .3, -.37), (.08, .08, .04), GLOW_PEACH, glow=True, segments=8, rings=4)


@part
def Acc_Sailor():
    cyl('Sailor_Hat', (0, y + .38, 0), .36, .12, WHITE, top=.4, verts=26)
    torus('Sailor_Band', (0, y + .33, 0), .36, .03, A, segments=26)
    for s in (-1, 1):
        ball('Sailor_Ribbon', (s * .06, y + .26, .38), (.06, .16, .02), A, rot=(0, 0, s * 15), segments=8, rings=4)


@part
def Acc_Chef():
    cyl('Chef_Hat_Band', (0, y + .3, 0), .34, .12, WHITE, verts=26)
    for k in range(5):
        a = k * math.tau / 5
        ball('Chef_Hat_Puff', (math.sin(a) * .16, y + .5, math.cos(a) * .14), (.3, .26, .28), WHITE, segments=12, rings=8)
    torus('Chef_Hat_Line', (0, y + .3, 0), .345, .012, A, segments=26)


@part
def Acc_Newsboy():
    ball('Newsboy_Cap', (0, y + .32, -.02), (.8, .26, .78), A, segments=20, rings=10)
    ball('Newsboy_Bill', (0, y + .26, -.36), (.34, .04, .16), AD, segments=12, rings=6)
    ball('Newsboy_Button', (0, y + .45, -.02), (.06, .04, .06), AD, segments=8, rings=4)


@part
def Acc_Bandana():
    ball('Bandana', (0, y + .14, .05), (.86, .56, .78), A, subdiv=1)
    ball('Bandana_Knot', (0, y + .12, .42), (.14, .12, .1), AD, segments=10, rings=6)


# ---------------------------------------------------------------- 宇宙人のしるし（Trait_*）NOKA の _species ほか
TR, TRD = '@trait', '@traitDark'


@part
def Trait_Fins():
    for s in (-1, 1):
        ball('Fin_Ear', (s * .42, y + .02, .04), (.06, .26, .2), TRD, rot=(0, 0, s * -20), segments=12, rings=8)
        ball('Fin_Ear_Edge', (s * .45, y + .1, .08), (.04, .12, .1), GLOW_MINT, glow=True, rot=(0, 0, s * -20), segments=8, rings=6)


@part
def Trait_LongEars():
    for s in (-1, 1):
        limb('Long_Ear', (s * .36, y + .02, .02), (s * .64, y + .2, .08), .06, TR, tip=.03, verts=10)


@part
def Trait_Antennae():
    for s in (-1, 1):
        limb('Antenna', (s * .12, y + .36, 0), (s * .22, y + .6, -.04), .014, TRD, verts=6)
        ball('Antenna_Glow', (s * .23, y + .63, -.04), (.09, .09, .09), GLOW_STAR, glow=True, segments=10, rings=6)


@part
def Trait_Horns():
    for s in (-1, 1):
        cyl('Horn', (s * .2, y + .38, .02), .06, .14, TRD, top=.02, verts=10, rot=(0, 0, s * -18))


@part
def Trait_Tail():
    limb('Tail', (0, .3, .2), (0, .24, .42), .05, TR, tip=.035, verts=10)
    limb('Tail_Tip_Stem', (0, .24, .42), (0, .34, .54), .035, TR, verts=8)
    star('Tail_Star', (0, .38, .58), .07, GLOW_STAR, rot=(0, 90, 0), depth=.03)


@part
def Trait_Glowdots():
    for k, (dx, dy) in enumerate(((0, .25), (-.07, .2), (.07, .2))):
        ball('Glow_Dot', (dx, y + dy, F * (.33 - abs(dx) * .3)), (.045, .045, .02), GLOW_MINT, glow=True, segments=8, rings=6)
    for s in (-1, 1):
        ball('Glow_Dot', (s * .3, y - .08, F * .25), (.035, .035, .02), GLOW_MINT, glow=True, segments=8, rings=6)


@part
def Trait_RoundEars():
    for s in (-1, 1):
        ball('Round_Ear', (s * .3, y + .32, .04), (.2, .2, .1), TR, rot=(0, 0, s * -20), segments=14, rings=8)
        ball('Round_Ear_Inner', (s * .3, y + .31, -.01), (.11, .11, .04), 'f6a6b0', rot=(0, 0, s * -20), segments=10, rings=6)


# ---------------------------------------------------------------- 書き出し
def reset():
    bpy.ops.wm.read_homefile(use_empty=True)


def build(name):
    reset()
    fn = PARTS[name]
    if name.startswith('Body_'):
        before = set(bpy.data.objects)
        pivots = fn()
        rig([o for o in bpy.data.objects if o not in before], pivots)
    else:
        pivots = make_rig(BODIES['Normal'])
        before = set(bpy.data.objects)
        fn()
        rig([o for o in bpy.data.objects if o not in before], pivots)
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + '.fbx'), use_selection=False,
                             object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                             add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    tris = 0
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            m = o.evaluated_get(depsgraph).to_mesh()
            m.calc_loop_triangles()
            tris += len(m.loop_triangles)
            o.evaluated_get(depsgraph).to_mesh_clear()
    print('AVATAR_PART', name, 'tris', tris)


# ---------------------------------------------------------------- 確認用レンダー（--preview）
SAMPLES = [
    # (体型, 目, 顔, 髪, 上着, 下衣, 靴, こもの, しるし, 色)
    ('Normal', 'Round', 'Smile', 'Short', 'Spacesuit', 'Spacepants', 'Moonboots', None, 'Antennae',
     dict(skin='f8d8bc', hair='8a6450', top=WHITE, topDark='d8dde6', bottom='8c95a3', legs='8c95a3', shoes=CORAL)),
    ('Slim', 'Big', 'Freckles', 'Twintails', 'Dress', 'Leggings', 'Sneakers', 'Ribbon', 'Fins',
     dict(skin='a8e6cf', trait='a8e6cf', traitDark='6cc0a8', hair='c6b5ee', top=CORAL, topDark='d06a5a', bottom=LAVENDER,
          legs=LAVENDER, acc=BUTTER, shoes=SKY, eye='3f6f9f')),
    ('Round', 'Happy', 'Rosy', 'Afro', 'Sweater', 'Trousers', 'Boots', 'Beanie', None,
     dict(skin='c98f6c', hair='3a2e2a', top=LEAF, topDark='5aa06a', bottom='5a4030', legs='5a4030', acc=CORAL, shoes=COCOA)),
    ('Sturdy', 'Sharp', 'Brows', 'Mohawk', 'Jacket', 'Trousers', 'Boots', None, 'Horns',
     dict(skin='9fb0f0', trait='9fb0f0', traitDark='6c7ac8', hair='ff9a3a', top='3d4660', topDark='2a3048', bottom='2f3a5a',
          legs='2f3a5a', shoes='3a3042')),
    ('Fluffy', 'Three', 'Cat', 'Topbun', 'Cape', 'Puffy', 'Sandals', 'Crown', 'Tail',
     dict(skin='ffd0e0', trait='ffd0e0', traitDark='e09ab0', hair=WHITE, top=LILAC, topDark='c090c0', fluff='f6e8f6',
          bottom='fff4c2', acc=BUTTER, shoes=LILAC, eye='a05ab0')),
    ('Normal', 'One', 'Open', 'Feelers', 'Overalls', 'Shorts', 'Barefoot', 'Goggles', 'Glowdots',
     dict(skin='b8f0a0', trait='b8f0a0', hair='3f6f73', top=SKY, topDark='7fb0cc', shirt=CREAM, sleeve=CREAM, bottom=SKY, legs='b8f0a0',
          acc=CORAL, shoes=COCOA)),
]


def preview():
    global PREVIEW_MODE, SLOTS
    PREVIEW_MODE = True
    base_slots = dict(SLOTS)
    for view, direction in (('front', (0, .25, -1)), ('three_quarter', (-.8, .35, -1)), ('back', (.3, .3, 1))):
        reset()
        for i, sample in enumerate(SAMPLES):
            body, eye, face, hair, top, bottom, shoes, acc, trait, colors = sample
            SLOTS = dict(base_slots)
            SLOTS.update(colors)
            SLOTS.setdefault('shirt', colors.get('top', SKY))
            if 'shirt' not in colors:
                SLOTS['shirt'] = SLOTS['top']
            if 'sleeve' not in colors:
                SLOTS['sleeve'] = SLOTS['skin'] if top in ('Tshirt', 'Dress') else SLOTS['shirt']
            if 'legs' not in colors:
                SLOTS['legs'] = SLOTS['bottom']
            SLOTS.setdefault('fluff', SLOTS['shirt'])
            SLOTS.setdefault('trait', SLOTS['skin'])
            before = set(bpy.data.objects)
            pivots = body_base(body)
            fit_scale = BODIES[body]['fit']
            fit = empty('PreviewFit', (0, .3, 0))
            fit.scale = (fit_scale[0], fit_scale[2], fit_scale[1])
            names = ['Eyes_' + eye, 'Face_' + face, 'Hair_' + hair, 'Top_' + top, 'Bottom_' + bottom, 'Shoes_' + shoes]
            if acc:
                names.append('Acc_' + acc)
            if trait:
                names.append('Trait_' + trait)
            for n in names:
                made = set(bpy.data.objects)
                PARTS[n]()
                for o in [o for o in bpy.data.objects if o not in made]:
                    if o.parent is None and o.type == 'MESH' and n.startswith(('Top_', 'Bottom_', 'Acc_', 'Trait_')) and classify(o) == 'body':
                        # ゲームと同じく、胴まわりの服は Rig_Body の高さを中心に体型の拡大率で広げる
                        bpy.context.view_layer.update()
                        world = o.matrix_world.copy()
                        o.parent = fit
                        o.matrix_parent_inverse.identity()
                        o.matrix_basis = Matrix.Translation(-fit.location) @ world
            bpy.context.view_layer.update()
            for o in [o for o in bpy.data.objects if o not in before and o.parent is None]:
                o.location.x -= i * 1.3
        bpy.context.view_layer.update()
        render(os.path.join(PREVIEW_OUT, 'preview_%s.png' % view), direction)


def render(path, direction):
    scene = bpy.context.scene
    objs = [o for o in scene.objects if o.type == 'MESH']
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre, size = (lo + hi) / 2, (hi - lo)
    cam_data = bpy.data.cameras.new('PreviewCam')
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = max(size.x, size.z) * 1.12
    cam = bpy.data.objects.new('PreviewCam', cam_data)
    scene.collection.objects.link(cam)
    d = B(direction).normalized()
    cam.location = centre + d * (size.length * 2 + 5)
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cam_data.clip_end = size.length * 6 + 50
    scene.camera = cam
    for engine in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT', 'BLENDER_WORKBENCH'):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.view_settings.view_transform = 'Standard'
    world = bpy.data.worlds.new('PreviewWorld')
    scene.world = world
    try:
        bg = world.node_tree.nodes['Background']
        bg.inputs['Color'].default_value = (.2, .22, .32, 1)
        bg.inputs['Strength'].default_value = 1.0
    except Exception:
        world.color = (.2, .22, .32)
    sun = bpy.data.objects.new('PreviewSun', bpy.data.lights.new('PreviewSun', 'SUN'))
    sun.data.energy = 2.6
    sun.data.angle = .35
    sun.rotation_euler = (math.radians(40), math.radians(-25), math.radians(200 if direction[2] > 0 else 30))
    scene.collection.objects.link(sun)
    scene.render.resolution_x, scene.render.resolution_y = 1800, 560
    scene.render.film_transparent = False
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print('AVATAR_PREVIEW', path)


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    if '--preview' in argv:
        preview()
        return
    names = [a for a in argv if not a.startswith('--')] or list(PARTS)
    for n in names:
        build(n)
    print('AVATAR_DONE', len(names))


main()
