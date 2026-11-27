# Game audio (lane 4)

`make_audio.py <Kenney Audio folder> OprGodot/sounds` rebuilds sounds/music, ambience, cues and dice. The Kenney
packs (CC0) come from a public mirror (kenney.nl itself is blocked from the cloud sandbox):

    git clone --depth 1 --filter=blob:none --no-checkout https://github.com/eturner58/game-assets ga
    cd ga && git sparse-checkout set "kenney/Audio/Casino Audio" "kenney/Audio/Impact Sounds" \
      "kenney/Audio/Interface Sounds" "kenney/Audio/Music Jingles" "kenney/Audio/Music Loops" "kenney/Audio/Sci-Fi Sounds" && git checkout
    python3 tools/audio/make_audio.py ga/kenney/Audio OprGodot/sounds

`check_audio.py OprGodot/sounds` prints loudness (LUFS) and true peak for every file and flags clipping / very loud /
silent files.
