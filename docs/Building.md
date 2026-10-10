# Building

`./build.sh` builds the mod and installs it into the game; `./build.sh check` only compiles; `./build.sh dist`
builds into `GameData/` here without touching the game. It needs the .NET SDK, a copy of the game (`KSP_DIR=/path`
if it is not where Steam puts it), `Keystone.dll` from [Keystone](https://github.com/IshiakiZ/ksp-keystone) and
`VolumetricExplosions.dll` from [Volumetric Explosions](https://github.com/IshiakiZ/ksp-volumetric-explosions):
the copies installed in the game, or built copies of those repositories beside this one.

This repository is made from a workspace that holds several mods side by side; changes arrive here from there.
