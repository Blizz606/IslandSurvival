The full pack: Weather Overlays has 32 kinds of weather in 44 loops: heavy rain, a storm, hail,
splashes and rain on the window, lightning bolts and flashes, wind, a tornado, heavy snow, a
blizzard, frost, a rainbow, thick fog, smoke, god rays, an aurora, stars and shooting stars,
petals, butterflies, ash, embers, fireflies, a sandstorm and more. Same sheets, same player.
https://heyheythere.itch.io/weather-overlays

sheets/256/    every weather as a 256x256 tile
sheets/128/    the same at half size
sheets/pixel/  pixel-art versions, drawn at 64 px (scale them up with nearest filtering)
               the fogs, smoke, cloud_shadows, clouds, lightning_flash, god_rays, aurora,
               shooting_stars, caustics and dappled_light are soft or sparse: draw their tiles twice as big (512 px on screen for the 256 set); their
               pixel set is drawn at 128 px
previews/      an animated GIF of each
sheets/effects.json  for each sheet: frame size per set, frame count, columns, fps, "tile",
               the on-screen tile size, where it isn't 256, and "single" for lightning,
               tornado and rainbow

Each sheet is a grid of frames, left to right then top to bottom, 8 to a row, on a transparent
background (straight alpha). Every frame is a seamless tile and every sheet loops: repeat the
current frame across the screen, and step through the frames at the fps. cloud_shadows, clouds,
rain_splashes and rain_ripples are seen from above, bubbles and caustics are for under water.

lightning is the one that isn't a tile: each frame (twice as tall as wide) is a bolt from the
top edge to the bottom, three strikes a loop with dark between. Draw it once, as tall as the
sky, where it should strike, and add lightning_flash over the screen for the sky lighting up.
The tornado (a frame twice as tall as wide) and the rainbow (twice as wide as tall) aren't tiles
either: draw each once, where it stands.

Godot 4.3+
    Copy addons/weather_overlays/ into your project and sheets/ into addons/weather_overlays/, then
        var rain := WeatherOverlay.add(self, "rain_heavy")   # over the whole screen
        WeatherOverlay.add(self, "fog", "pixel")               # stack as many as you like
        rain.scroll = camera.global_position                   # keep the rain in the world
        rain.stop(2.0)                                         # fade out and free
        WeatherOverlay.add(self, "lightning").rect = Rect2(700, 0, 300, 480)   # a bolt there
    tile_size is how many screen pixels a tile spans (256 by default, whichever set), speed its
    playback rate, rect the area it covers if not the screen. WeatherOverlay.default_set =
    "pixel" (or "128") switches every overlay made after it; set WeatherOverlay.sheets_dir if
    sheets/ is elsewhere. Open addons/weather_overlays/demo/demo.tscn to try them all.

Unity
    Import a sheet, set Wrap Mode to Repeat (and for the pixel set Filter Mode Point, no
    compression). Slice it as a grid with the frame size from effects.json, or keep it whole and
    step a material's tiling and offset through the frames. For the overlay, a Sprite Renderer
    in Tiled draw mode (Mesh Type Full Rect) over the camera, or a RawImage whose UV Rect width
    and height are the screen size divided by the tile size.

GameMaker
    Create Sprite > Import > the sheet, then Convert to Frames with the frame size, 8 frames per
    row and the frame count from effects.json. Set the sprite's speed to the fps and draw it with
    draw_sprite_tiled(spr, image_index, x, y) in a Draw GUI event, or put it on a background
    layer with horizontal and vertical tiling on.

Anything else
    Any engine or framework that repeats a texture: cut the frame from the sheet with the frame
    size from effects.json and draw it wrapped across the area.
