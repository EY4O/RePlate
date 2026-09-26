<p align="center"><img src="images/icon.png" width="96" alt="RePlate icon"></p>

<h1 align="center">RePlate</h1>

<p align="center"><strong>Save and restore your adventurer plate.</strong></p>

Keep copies of your adventurer plate and your gear sets' portraits, and put any of them back, after a Fantasia or
just a change of mind.

## Features

- Save everything in one click: Grab the full look (pose, expression, lighting, background, frames, accents, and base layout) without manually writing down settings.
- Smarter restoring: RePlate opens the in-game editor, dials in your saved setup, and pauses so you can double-check the final result before saving. It skips parts that already match, calls out anything you haven't unlocked yet, and can even hit Save automatically if you turn that setting on.
- Gear set gallery: Store your favorite looks in a central gallery and apply one portrait across multiple jobs. If a job can't use a pose or anything else in it, that gear set keeps what it already had.
- Test before committing: Drop a saved preset directly into Edit Portrait or Edit Plate Design to tweak the details by hand before saving.
- Easy sharing: Swap plates using simple text codes. If someone shares a preset with items you don't own yet, your current style stays untouched, and the import stops right before saving so you can review it first.
- HaselTweaks support: Copy or import HaselTweaks codes, and migrate your entire existing library from Portrait Helper (preview images included). (Thanks mate.)
- Custom thumbnails: Snap an in-game screenshot or drop in a PNG so you can instantly recognize which plate is which.
- One-file backups: Export your entire collection (portraits, plates, and thumbnails) into a single file to move between characters or transfer to a new PC.

## How it works

- RePlate only uses the game's own windows. It opens your plate, picks Edit Portrait or Edit Plate Design, and fills them in the same way you would by hand.
- Nothing is saved unless you say so. By default it stops before Save so you can check the result, and shared plates and gear set portraits always do.
- Anything your character hasn't unlocked is checked with the game's own unlock check and left as it was.
- No network, no hooks, and nothing about other players is kept. Your plates and pictures stay in your Dalamud settings folder.

## Installation

1. In game, open `/xlsettings`.
2. Under **Experimental > Custom Plugin Repositories**, add this URL and save:

   ```text
   https://ey4o.github.io/XIV-Plugins/repo.json
   ```

3. Open `/xlplugins`, then find and install **RePlate**.

## Commands

| Command | What it does |
| --- | --- |
| `/replate` | Open RePlate |
| `/replate settings` | Open the settings |
| `/replate welcome` | Open the welcome guide |
| `/replate stop` | Stop a restore |

## Licence

[MIT](LICENSE).
