# Dalamud-OBS

A Dalamud plugin that communicates with OBS by websocket.

## Requirements

You need to install two plugins to your OBS:

### OBS 28

You still need to use OBS-websocket plugin 4.9.1-compat instead of the built-in one because the api 5 is not backward compatible and it lacks some of the APIs in api 4.

- [OBS-websocket 4.9.1-compat](https://github.com/obsproject/obs-websocket/releases/tag/4.9.1-compat): for communication with this plugin.
- [StreamFX 0.12.0 alpha](https://github.com/Xaymar/obs-StreamFX/releases/tag/0.12.0a117): for the blur function.

### OBS 27

- [OBS-websocket 4.9.1](https://github.com/obsproject/obs-websocket/releases/tag/4.9.1): for communication with this plugin.
- [StreamFX 0.11.1](https://github.com/Xaymar/obs-StreamFX/releases/tag/0.11.1): for the blur function.

## Installation

You can enable the testing plugins in Dalamud settings.
