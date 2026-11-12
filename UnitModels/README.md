# UnitModels

3D models for units, opened in the Godot build's **Asset Lab** (main menu -> Asset Lab).

Put them in the same shape as `UnitImages`:

    UnitModels/<Faction>/<Unit Name>.glb

`.glb`, `.gltf` and `.obj` all open. An `.obj` takes its skin from its `.mtl`, or from a picture with the same
name beside it (`Assault Grunt.obj` + `Assault Grunt.png`) - the way the Tabletop Simulator mod's models come.
You can also drag files straight onto the Asset Lab window; they are copied into `_Dropped/`.

In the lab you set how a model stands - which way is up, which way it faces, how tall it is in mm and its
base - and **Save settings** writes those numbers beside the file as `<file>.model.json`. The table reads
them when real models go in, so each model only has to be set up once.

Everything here except `_Samples/` and this file is ignored by git (models are big and they are yours).
