import sys
import bpy

argv = sys.argv
argv = argv[argv.index("--") + 1:]  # get all args after "--"
exportPath = argv[0]
objToExportName = argv[1]

# Deselect all objects
obj = bpy.context.active_object
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action="DESELECT")

# Get the desired objects from all available collections
match_objects = []
for obj in bpy.data.objects:
    if objToExportName == obj.name:
        match_objects.append(obj)
        print( "Found " + obj.name)
        
if len(match_objects) > 1:
    print("WARNING: Found more than 1 " + objToExportName)
    
bpy.context.view_layer.objects.active = match_objects[0]
bpy.data.objects[objToExportName].select_set(True)
bpy.ops.object.select_grouped(extend=True, type='CHILDREN_RECURSIVE')

# Export the selection
bpy.ops.export_scene.fbx(
   filepath = argv[0], 
   path_mode = 'RELATIVE', 
   use_custom_props = True,
   use_selection=True,
   apply_scale_options = 'FBX_SCALE_UNITS',
   object_types = {'EMPTY','MESH'})    