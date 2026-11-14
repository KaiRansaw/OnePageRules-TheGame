"""
Copies the terrain pieces the game uses out of the free (CC0) packs into OprGodot/terrain/, so they ship with the game.
Kai's call (2026-11-14): "both games have free packs that are bundled into the official release".

Packs (all CC0 1.0 - free to ship, no credit required; credited anyway in OprGodot/CREDITS.md):
  kenney_nature-kit, kenney_castle-kit, kenney_fantasy-town-kit_2.0, kenney_city-kit-industrial_2.0,
  kenney_space-kit, kenney_survival-kit (kenney.nl), and Quaternius' Medieval Village MegaKit [Standard]
  (quaternius.com).

Usage:  python3 tools/terrain/bundle_terrain.py <folder holding the unzipped packs>
        (each pack unzipped into its own sub-folder named after its zip; brackets and spaces turned into _)

Only the files named in PICKS are copied - the packs are 260 MB together, the game needs a few MB of them. The
Medieval Village textures are shrunk to 512 px (they ship at 2048; a 6" cottage on the table never needs more).
Re-running replaces OprGodot/terrain/ with exactly this list. Afterwards run Godot's import once
(godot --headless --path OprGodot --import) and commit the .import files with the models.
"""
import glob, json, os, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(REPO, 'OprGodot', 'terrain')

# pack folder -> (where its models are, files to copy, target sub-folder). Names must stay in step with
# FdgApp/Terrain/TerrainLooks.cs, which names each file as "<target>/<file>".
NATURE = ['tree_oak', 'tree_default', 'tree_detailed', 'tree_fat', 'tree_tall', 'tree_simple',
          'tree_oak_dark', 'tree_default_dark', 'tree_detailed_dark', 'tree_fat_darkh', 'tree_blocks_dark', 'tree_plateau_dark',
          'tree_oak_fall', 'tree_default_fall', 'tree_detailed_fall', 'tree_fat_fall',
          'tree_pineTallA', 'tree_pineTallB', 'tree_pineTallC', 'tree_pineRoundA', 'tree_pineRoundB', 'tree_cone',
          'plant_bush', 'plant_bushLarge', 'plant_bushDetailed', 'plant_flatTall', 'grass_large', 'grass_leafsLarge',
          'flower_redA', 'flower_yellowA', 'flower_purpleA', 'hanging_moss', 'log_large', 'stump_round', 'mushroom_red',
          'rock_largeA', 'rock_largeB', 'rock_largeC', 'rock_largeD', 'rock_tallA', 'rock_tallB', 'rock_tallC', 'rock_tallE',
          'rock_tallG', 'rock_smallA', 'rock_smallB', 'rock_smallC', 'rock_smallFlatA', 'stone_largeA', 'stone_largeB',
          'stone_tallA', 'stone_tallB', 'stone_smallA', 'stone_smallB', 'stone_smallC', 'statue_columnDamaged', 'statue_block',
          'crops_wheatStageA', 'crops_wheatStageB', 'crops_cornStageC', 'crops_cornStageD', 'crops_leafsStageB', 'lily_large']
CASTLE = ['wall', 'wall-half', 'wall-corner', 'wall-corner-half', 'wall-narrow', 'wall-narrow-wood', 'wall-narrow-wood-fence',
          'wall-pillar', 'tower-square-base', 'tower-square-mid', 'tower-square-mid-windows', 'tower-square-roof',
          'tower-square-top-roof', 'rocks-large', 'rocks-small', 'siege-tower-demolished', 'siege-catapult-demolished',
          'siege-ram-demolished']
TOWN = ['fence', 'fence-broken', 'hedge', 'hedge-large', 'wall-broken', 'wall-wood-broken', 'cart', 'cart-high', 'planks',
        'rock-large', 'rock-wide', 'lantern', 'poles']
INDUSTRIAL = ['building-' + c for c in 'abcdefghijklmnopqrst'] + ['detail-tank', 'detail-tank-large', 'shipping-container-a',
              'shipping-container-b', 'shipping-container-c', 'water-tower', 'chimney-large']
SPACE = ['barrel', 'barrels', 'barrels_rail', 'crater', 'craterLarge', 'meteor', 'meteor_detailed', 'rock', 'rock_crystals',
         'rock_crystalsLargeA', 'rock_crystalsLargeB', 'rock_largeA', 'rock_largeB', 'rocks_smallA', 'rocks_smallB',
         'structure', 'structure_detailed', 'supports_high', 'supports_low', 'machine_generator', 'machine_generatorLarge',
         'machine_barrel', 'machine_barrelLarge', 'pipe_straight', 'pipe_corner', 'hangar_smallA', 'hangar_smallB',
         'hangar_largeA', 'hangar_roundA', 'craft_cargoA', 'craft_cargoB', 'craft_miner', 'rover', 'turret_single',
         'satelliteDish', 'bones']
SURVIVAL = ['fence-fortified', 'metal-panel', 'metal-panel-screws', 'metal-panel-screws-half', 'structure-metal-wall',
            'box', 'box-large', 'barrel', 'rock-a', 'rock-b', 'rock-c', 'rock-flat', 'tree-log', 'resource-stone-large']
VILLAGE = ['Wall_Plaster_Straight', 'Wall_Plaster_Window_Wide_Round', 'Wall_Plaster_Door_Round', 'Wall_Plaster_WoodGrid',
           'Wall_UnevenBrick_Straight', 'Wall_UnevenBrick_Window_Wide_Round', 'Wall_UnevenBrick_Door_Round',
           'Corner_Exterior_Brick', 'Corner_Exterior_Wood', 'Prop_Chimney', 'Prop_Crate', 'Prop_Wagon',
           'Prop_WoodenFence_Single', 'Prop_Brick1', 'Prop_Brick2'] + [
           'Roof_RoundTiles_' + s for s in ('4x4', '4x6', '4x8', '6x4', '6x6', '6x8', '6x10', '6x12', '6x14', '8x8', '8x10', '8x12', '8x14')]

KENNEY = [
    ('kenney_nature-kit', 'Models/GLTF format', NATURE, 'nature'),
    ('kenney_castle-kit', 'Models/GLB format', CASTLE, 'castle'),
    ('kenney_fantasy-town-kit_2.0', 'Models/GLB format', TOWN, 'town'),
    ('kenney_city-kit-industrial_2.0', 'Models/GLB format', INDUSTRIAL, 'industrial'),
    ('kenney_space-kit', 'Models/GLTF format', SPACE, 'space'),
    ('kenney_survival-kit', 'Models/GLB format', SURVIVAL, 'survival'),
]


def glb_images(path):
    import struct
    b = open(path, 'rb').read()
    n = struct.unpack('<I', b[12:16])[0]
    return [i['uri'] for i in json.loads(b[20:20 + n]).get('images', []) if 'uri' in i]


def main(packs):
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    missing = []
    for pack, sub, names, target in KENNEY:
        src = os.path.join(packs, pack, sub)
        dst = os.path.join(OUT, target)
        os.makedirs(dst, exist_ok=True)
        for n in names:
            f = os.path.join(src, n + '.glb')
            if not os.path.exists(f):
                missing.append(f'{pack}/{n}')
                continue
            shutil.copy2(f, dst)
            for uri in glb_images(f):  # "Textures/colormap.png", shared by a whole pack
                t = os.path.join(src, uri)
                os.makedirs(os.path.dirname(os.path.join(dst, uri)), exist_ok=True)
                if os.path.exists(t) and not os.path.exists(os.path.join(dst, uri)):
                    shutil.copy2(t, os.path.join(dst, uri))
        shutil.copy2(os.path.join(packs, pack, 'License.txt'), os.path.join(dst, 'License.txt'))

    from PIL import Image
    village = glob.glob(os.path.join(packs, 'Medieval*'))[0]
    root = glob.glob(os.path.join(village, '*'))[0] if not os.path.isdir(os.path.join(village, 'glTF')) else village
    dst = os.path.join(OUT, 'village')
    os.makedirs(dst, exist_ok=True)
    textures = set()
    for n in VILLAGE:
        g = os.path.join(root, 'glTF', n + '.gltf')
        if not os.path.exists(g):
            missing.append(f'village/{n}')
            continue
        j = json.load(open(g, encoding='utf-8'))
        for b in j.get('buffers', []):
            shutil.copy2(os.path.join(root, 'glTF', b['uri']), dst)
        for i in j.get('images', []):
            textures.add(i['uri'])
        shutil.copy2(g, dst)
    for t in sorted(textures):
        src = os.path.join(root, 'Textures', t)
        if not os.path.exists(src):
            missing.append(f'village texture {t}')
            continue
        im = Image.open(src)
        im.thumbnail((512, 512))
        im.save(os.path.join(dst, t), optimize=True)
    shutil.copy2(glob.glob(os.path.join(root, 'License*.txt'))[0] if glob.glob(os.path.join(root, 'License*.txt')) else glob.glob(os.path.join(village, '**', 'License*.txt'), recursive=True)[0], os.path.join(dst, 'License.txt'))

    size = sum(os.path.getsize(os.path.join(d, f)) for d, _, fs in os.walk(OUT) for f in fs)
    print(f'copied into {OUT}: {size / 1e6:.1f} MB')
    for m in missing:
        print('  missing:', m)
    return 1 if missing else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1]))
