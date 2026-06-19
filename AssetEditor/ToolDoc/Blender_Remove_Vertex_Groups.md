# Blender Remove Vertex Groups Helper

Copies a small Blender Python helper script to the clipboard. Paste it into Blender's scripting workspace and run it to remove all vertex groups from selected mesh objects.

```python
import bpy
selection = bpy.context.selected_objects
for ob in selection:
    if ob.type == 'MESH':
        for group in list(ob.vertex_groups):
            ob.vertex_groups.remove(group)
```

This is useful for cleaning helper meshes before re-rigging or transferring weights.
