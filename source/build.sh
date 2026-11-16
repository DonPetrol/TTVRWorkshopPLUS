#!/bin/bash
# builds WorkshopPlus.dll from src/*.cs against the Workshop tool's DLLs (refs/Assembly-CSharp.dll is a copy with everything made public: publicize.py)
cd /home/claude/wsplus
B="/mnt/user-data/uploads/Tiny Town VR/BepInEx/core"
R=(); for f in refs/*.dll; do R+=("-r:$f"); done
dotnet /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll -nologo -target:library -optimize+ -unsafe+ -langversion:7.3 -nostdlib -noconfig -nowarn:0618 \
  -out:WorkshopPlus.dll "${R[@]}" -r:"$B/BepInEx.dll" -r:"$B/0Harmony.dll" src/*.cs 2>&1 | grep -v "warning"
ls -la WorkshopPlus.dll
