# Changelog

## 1.2.0
- Fix: loading screens are no longer slowed down while the game is minimized — background throttling now pauses during loads, and the initial boot to the main menu is never throttled.

## 1.1.0
- Fix: the in-game **Frame Limiter** setting could get permanently stuck at a low value (the mod no longer touches that setting; it throttles via the main loop only).

## 1.0.0
- Initial release: caps the framerate while the game window is in the background (alt-tabbed/minimized).
