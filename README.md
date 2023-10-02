# Dalamud-OBS

A Dalamud plugin that communicates with OBS by websocket.

## Requirements

You need to install two plugins to your OBS:

### OBS 28

You still need to use OBS-websocket plugin 4.9.1-compat instead of the built-in one because the api 5 is not backward compatible and it lacks some of the APIs in api 4.

May use the built-in websocket plugin after OBS 30.

- [OBS-websocket 4.9.1-compat](https://github.com/obsproject/obs-websocket/releases/tag/4.9.1-compat): for communication with this plugin.
- [OBS Composite Blur](https://github.com/FiniteSingularity/obs-composite-blur): for the blur function.

### OBS 27

Please upgrade to OBS 28+

## Installation

You can enable the testing plugins in Dalamud settings.
