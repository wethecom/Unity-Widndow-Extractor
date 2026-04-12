import bpy
import bmesh
import os
import sys
import subprocess
import traceback
from datetime import datetime
from collections import deque

# --- USER CONFIGURATION ---
INPUT_FOLDER  = r"C:\Users\Administrator\Desktop\Meshes"
OUTPUT_FOLDER = r"C:\Users\Administrator\Desktop\MeshesWindows"
TARGET_STRING = "glass"
SCRIPT_PATH   = r"C:\Users\Administrator\Desktop\fbx_glass_hole_cutter.py"
# --------------------------

LOG_FILE    = os.path.join(OUTPUT_FOLDER, "processing_log.txt")
_log_handle = None


# ── Logging ─────────────────────────────────────────────────────

def log(msg):
    timestamp = datetime.now().strftime("%H:%M:%S")
    line = f"[{timestamp}] {msg}"
    print(line)
    sys.stdout.flush()
    if _log_handle:
        _log_handle.write(line + "\n")
        _log_handle.flush()


def open_log():
    global _log_handle
    os.makedirs(OUTPUT_FOLDER, exist_ok=True)
    _log_handle = open(LOG_FILE, "w", encoding="utf-8")
    log("=" * 60)
    log("FBX Glass Hole Cutter  —  Started")
    log(f"Input  : {INPUT_FOLDER}")
    log(f"Output : {OUTPUT_FOLDER}")
    log("=" * 60)


def close_log(succeeded, failed_list):
    log("=" * 60)
    log(f"FINISHED  —  {succeeded} succeeded,  {len(failed_list)} failed")
    if failed_list:
        log("Failed files:")
        for name, reason in failed_list:
            log(f"  x {name}  --  {reason}")
    else:
        log("All files processed without errors.")
    log("=" * 60)
    if _log_handle:
        _log_handle.close()
    try:
        os.startfile(LOG_FILE)
    except Exception:
        pass


# ── Mesh helpers (used inside each single-file subprocess) ──────

def get_glass_material_indices(obj):
    return {
        i for i, slot in enumerate(obj.material_slots)
        if slot.material and TARGET_STRING.lower() in slot.material.name.lower()
    }


def get_glass_islands(glass_faces):
    glass_set = set(glass_faces)
    visited   = set()
    islands   = []
    for start in glass_faces:
        if start in visited:
            continue
        island = []
        queue  = deque([start])
        while queue:
            face = queue.popleft()
            if face in visited:
                continue
            visited.add(face)
            island.append(face)
            for edge in face.edges:
                for linked in edge.link_faces:
                    if linked not in visited and linked in glass_set:
                        queue.append(linked)
        islands.append(island)
    return islands


def faces_to_new_object(faces, transform, name):
    bm_new   = bmesh.new()
    vert_map = {}
    for face in faces:
        new_verts = []
        for v in face.verts:
            if v.index not in vert_map:
                vert_map[v.index] = bm_new.verts.new(v.co)
            new_verts.append(vert_map[v.index])
        try:
            bm_new.faces.new(new_verts)
        except ValueError:
            pass
    mesh                 = bpy.data.meshes.new(name + "_mesh")
    bm_new.to_mesh(mesh)
    bm_new.free()
    new_obj              = bpy.data.objects.new(name, mesh)
    new_obj.matrix_world = transform
    bpy.context.scene.collection.objects.link(new_obj)
    return new_obj


def process_object(obj, window_counter):
    glass_indices = get_glass_material_indices(obj)
    if not glass_indices:
        return window_counter

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()

    glass_faces = [f for f in bm.faces if f.material_index in glass_indices]
    if not glass_faces:
        bm.free()
        return window_counter

    for island in get_glass_islands(glass_faces):
        name = f"Window_{window_counter:03d}"
        faces_to_new_object(island, obj.matrix_world.copy(), name)
        print(f"          + {name}  ({len(island)} faces)")
        sys.stdout.flush()
        window_counter += 1

    bmesh.ops.delete(bm, geom=glass_faces, context='FACES')
    bm.to_mesh(obj.data)
    obj.data.update()
    bm.free()
    return window_counter


def process_file(input_filepath, output_filepath):
    print(f"    Importing {os.path.basename(input_filepath)}")
    sys.stdout.flush()

    bpy.ops.import_scene.fbx(filepath=input_filepath)
    print("    Import OK")
    sys.stdout.flush()

    for obj in [o for o in list(bpy.data.objects) if o.type in ('LIGHT', 'CAMERA')]:
        bpy.data.objects.remove(obj, do_unlink=True)

    targets = [
        obj for obj in bpy.data.objects
        if obj.type == 'MESH'
        and any(
            slot.material and TARGET_STRING.lower() in slot.material.name.lower()
            for slot in obj.material_slots
        )
    ]

    if not targets:
        print("    No glass material found — exporting unchanged.")
        sys.stdout.flush()
    else:
        print(f"    {len(targets)} object(s) with glass:")
        sys.stdout.flush()
        window_counter = 1
        for obj in targets:
            window_counter = process_object(obj, window_counter)
        print(f"    {window_counter - 1} window island(s) created.")
        sys.stdout.flush()

    os.makedirs(os.path.dirname(output_filepath), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=output_filepath, use_selection=False)
    print(f"    Saved  ->  {os.path.basename(output_filepath)}")
    print("[SUBPROCESS_OK]")
    sys.stdout.flush()


# ── Entry points ─────────────────────────────────────────────────

def run_single_file_mode(input_path, output_path):
    try:
        process_file(input_path, output_path)
    except Exception as e:
        print(f"[SUBPROCESS_FAIL] {e}")
        traceback.print_exc()
        sys.stdout.flush()


def get_script_path():
    # Method 1: command line --python argument
    for i, a in enumerate(sys.argv):
        if a == '--python' and i + 1 < len(sys.argv):
            return sys.argv[i + 1]
    # Method 2: __file__ (set when Blender loads the script from disk)
    try:
        path = os.path.abspath(__file__)
        if os.path.isfile(path):
            return path
    except NameError:
        pass
    # Method 3: manual SCRIPT_PATH from user config
    if SCRIPT_PATH and os.path.isfile(SCRIPT_PATH):
        return SCRIPT_PATH
    return None


def run_batch_mode():
    open_log()

    if not os.path.isdir(INPUT_FOLDER):
        log(f"ERROR: Input folder not found: {INPUT_FOLDER}")
        close_log(0, [("—", "Input folder missing")])
        return

    fbx_files = [f for f in os.listdir(INPUT_FOLDER) if f.lower().endswith('.fbx')]
    if not fbx_files:
        log("No FBX files found in input folder.")
        close_log(0, [])
        return

    script_path = get_script_path()
    if not script_path:
        log("ERROR: Cannot detect script path.")
        log("Set the SCRIPT_PATH variable at the top of the script to the full path of this file.")
        close_log(0, [])
        return

    blender_exe = bpy.app.binary_path
    log(f"Blender : {blender_exe}")
    log(f"Script  : {script_path}")
    log(f"{len(fbx_files)} FBX file(s) found — one fresh Blender process per file.\n")

    os.makedirs(OUTPUT_FOLDER, exist_ok=True)
    succeeded   = 0
    failed_list = []

    for i, filename in enumerate(fbx_files, 1):
        input_path  = os.path.join(INPUT_FOLDER, filename)
        output_path = os.path.join(OUTPUT_FOLDER, filename)
        log(f"  [{i}/{len(fbx_files)}]  {filename}")

        cmd = [
            blender_exe, "--background",
            "--python", script_path,
            "--", input_path, output_path
        ]
        try:
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=600)

            for line in result.stdout.splitlines():
                if line.strip():
                    log(f"    {line}")

            if "[SUBPROCESS_OK]" in result.stdout:
                log(f"  OK  {filename}\n")
                succeeded += 1
            else:
                reason = "subprocess did not complete cleanly"
                err_lines = [l for l in result.stderr.splitlines() if l.strip()]
                if err_lines:
                    reason = err_lines[-1]
                log(f"  x FAILED: {filename}  --  {reason}")
                failed_list.append((filename, reason))

        except subprocess.TimeoutExpired:
            log(f"  x TIMEOUT: {filename}  (over 10 minutes — skipping)")
            failed_list.append((filename, "timed out"))
        except Exception as e:
            log(f"  x ERROR: {filename}  --  {e}")
            failed_list.append((filename, str(e)))

    close_log(succeeded, failed_list)


def main():
    if "--" in sys.argv:
        extra = sys.argv[sys.argv.index("--") + 1:]
        if len(extra) >= 2:
            run_single_file_mode(extra[0], extra[1])
            return

    run_batch_mode()


main()
