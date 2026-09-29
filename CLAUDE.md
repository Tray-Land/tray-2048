@AGENTS.md

## Tray app conventions

This is a tray-only WinUI 3 app built from the `winui-tray-app` skill scaffold on top of `winapp new`. Load that skill before changing app lifetime, the flyout, the tray icon, polling, settings, or secrets.

- Launch with `winapp run` (or `dotnet run`); never register the package by hand. Use `winapp run --clean` to test first-run behavior.
- `App.xaml.cs` owns the tray icon, single instance, and the only exit path (the Exit menu). There is no main window.
- The flyout (`Views/TrayFlyoutWindow`) is hidden on dismiss and closed after a minute hidden. Anything it starts, it must stop in `FlyoutPage.OnHidden` / `Dispose`.
- Network work runs only while visible, through `Services/ForegroundPoller`. The one exception is background work the tray icon itself displays, and that needs a documented reason.
- App API keys go in `OAuth.resw` (gitignored) and are read through `Services/Secrets`. User tokens go in `Services/CredentialStore`. Nothing secret goes in `SettingsService`.
- Put pure decision logic (parsing, policies, formatting) in classes with no WinRT dependency, so a plain `net10.0` test project can cover it.

## Tray 2048

- `Game/GameEngine.cs` holds all the rules and has no WinRT dependency; `tests/Tray2048.Tests` links it directly (`dotnet test tests/Tray2048.Tests`).
- `Views/GameBoard` renders and animates moves (slide → pop/spawn); `Views/TileView` maps each value to a visual state; colors live in `Themes/Game.xaml`.
- The game is saved to `SettingsService.SavedGame` after every move. Nothing runs while the flyout is hidden.
- Debug builds only: Ctrl+Shift+W / Ctrl+Shift+L load a board one move from winning / losing, for checking the overlays.
