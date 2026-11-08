#!/usr/bin/env bash
# Records and encodes the BITSHIFT trailer. Run from the repo root (Git Bash).
# Needs: Unity 6000.4.3f1, ffmpeg on PATH. A game window opens for ~1 minute while it records.
set -euo pipefail
UNITY="${UNITY:-/c/Program Files/Unity/Hub/Editor/6000.4.3f1/Editor/Unity.exe}"
OUT="Builds/Trailer"
FRAMES="$OUT/frames"

cp tools/solutions.txt Assets/Resources/solutions.txt
"$UNITY" -batchmode -nographics -quit -projectPath . \
  -executeMethod BitShift.EditorTools.BuildScript.BuildWindows -logFile Logs/win.log

mkdir -p "$FRAMES"
FRAMES_WIN="$(cygpath -w "$PWD/$FRAMES")"
Builds/Windows/BITSHIFT.exe -trailer "$FRAMES_WIN" -screen-width 1280 -screen-height 720 \
  -screen-fullscreen 0 -logFile "$(cygpath -w "$PWD/Logs/trailer.log")"

N=$(ls "$FRAMES"/*.jpg | wc -l)
D=$(python -c "print($N/30)")
ffmpeg -v error -y -framerate 30 -i "$FRAMES/f%05d.jpg" -i "$FRAMES/audio.wav" \
  -filter_complex "[0:v]scale=1920:1080:flags=lanczos,fade=t=in:st=0:d=0.4,fade=t=out:st=$(python -c "print($D-1.0)"):d=1.0,format=yuv420p[v];[1:a]loudnorm=I=-16:TP=-1.5,afade=t=out:st=$(python -c "print($D-1.5)"):d=1.5[a]" \
  -map "[v]" -map "[a]" -c:v libx264 -preset slow -crf 18 -r 30 -c:a aac -b:a 192k -ar 48000 \
  -movflags +faststart -shortest "$OUT/BITSHIFT-trailer.mp4"
echo "Trailer: $OUT/BITSHIFT-trailer.mp4 ($D s)"
