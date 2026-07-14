# Lots of Kisses 💕

A mod for Stardew Valley that expands romantic kiss interactions with your spouse or dating partner.

---

## Features

- **Bump Kiss** — share a quick kiss when you run toward your partner; small chance of knocking an item out of their hands
- **Multi-Kiss Sequences** — hold your position near your partner for continuous kiss cycles
- **Public Reactions** — nearby NPCs with line of sight will notice and react with embarrassed emotes and dialogue
- **Partner Dialogue** — your partner embarrassed comments during kiss sequences you're in public
- **Polyamory Support** — compatible with polyamory mods; all romantic partners are recognized

---

## Requirements

- [Stardew Valley](https://www.stardewvalley.net/) 1.6+
- [SMAPI](https://smapi.io/) 4.0.0+
- *(Optional)* [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) — for in-game config options

---

## Installation

1. Install [SMAPI](https://smapi.io/)
2. Download the latest release and extract the `Lots of Kisses` folder into your `Stardew Valley/Mods/` directory
3. Launch the game through SMAPI

---

## Content Packs

Lots of Kisses supports content packs for adding kiss dialogue for any NPC.

### Structure

```
[LoK] Your Pack Name/
├── manifest.json
├── en.json
└── pt.json        ← optional, add other locales as needed
```

### manifest.json

```json
{
  "Name": "Lots of Kisses - Your Pack Name",
  "Author": "YourName",
  "Version": "1.0.0",
  "Description": "Kiss dialogue for ...",
  "UniqueID": "YourName.LotsOfKisses.YourPackName",
  "ContentPackFor": {
    "UniqueID": "NatrollEXE.LotsOfKisses"
  },
  "MinimumApiVersion": "4.0.0",
  "UpdateKeys": []
}
```

### Dialogue keys

Every dialogue key ends with a positive number. There is no fixed maximum, and the numbers do not need to be consecutive, so a pack may use keys such as `.1`, `.25`, and `.250` without filling the gaps.

Use the NPC's exact internal name in NPC-specific keys. The main supported patterns are:

- `approachKiss.<InternalName>.<number>`
- `OutdoorKisses.<InternalName>.<number>`
- `IndoorKisses.<InternalName>.<number>`
- `kissReaction.<InternalName>.<number>`
- `PublicKissReaction.<InternalName>.<number>`
- `Reaction<InternalName>.<number>` for a bystander's personalized reaction
- `CrowdReaction.<number>` for generic adult reactions
- `CrowdReaction.Child.<number>` for child reactions

Partner dialogue may also be gender-specific by adding `.male` or `.female` before the number, for example `OutdoorKisses.MyNPC.female.1`. Locale files work like translations: Lots of Kisses loads the current language when available and falls back to `en.json`.

### Note

NPCs without a content pack still work fully — kisses happen normally, just without custom dialogue.

---

## Compatibility

- ✅ Works with spouses and dating partners
- ✅ Compatible with polyamory mods (all recognized romantic partners are supported)
- ✅ Compatible with any romanceable NPC custom via content pack

---

## Ignoring Specific Vision Tiles

Some counters, fences, decorations, and custom-map tiles are marked as impassable by the game even though an NPC should still be able to see a kiss across them. You can make the Lots of Kisses line-of-sight checks ignore those tiles with the optional **Tile Marker** framework or through `VisionIgnoredTiles` in `config.json`.

### Using Tile Marker (recommended)

1. Install both Lots of Kisses and Tile Marker.
2. Load your save and go to the location containing the tiles you want to change.
3. Press Tile Marker's configured editor key (`\` / `|` by default).
4. Choose **Kiss reaction vision exceptions**. If this is the only registered category, the editor opens directly.
5. Click one tile to mark or unmark it. Click and drag to add or remove a rectangle.
6. Press `Esc` while not dragging, or press the editor key again, to close and save.

Blue tiles are already marked, green is an add preview, and red is a remove preview. Tile Marker stores selections separately for each save, and Lots of Kisses reads the new selection as soon as the editor saves it.

Tile Marker is optional. If it isn't installed, Lots of Kisses continues working normally with the manual configuration below.

### Manual config.json setup

1. Launch the game with Lots of Kisses installed at least once so SMAPI creates `config.json`.
2. Close the game.
3. Open `Mods/Lots of Kisses/config.json` in a text editor.
4. Find `VisionIgnoredTiles` and add the map name and tile coordinates you want to ignore.
5. Save the file and restart the game so the configuration is loaded.

These are **map tile coordinates**, not screen or pixel coordinates. You can read them with a map editor or any Stardew debugging/modding tool that displays the tile under the cursor.

### Accepted coordinate formats

Each entry uses the format `x,y`. Both axes accept either one coordinate or an inclusive range:

- `"6,12"` ignores only tile X 6, Y 12.
- `"6-8,12"` ignores tiles X 6 through 8 on row Y 12.
- `"6,12-15"` ignores tiles Y 12 through 15 in column X 6.
- `"1-8,15-18"` ignores the entire rectangle from X 1-8 and Y 15-18.

Ranges are inclusive, may be written in reverse (for example, `"8-1,18-15"`), and may contain spaces. Invalid entries are ignored.

Pierre's full counter area in the Seed Shop occupies the rectangle from X 1-8 and Y 15-18:

```json
"VisionIgnoredTiles": {
  "SeedShop": [ "1-8,15-18" ]
}
```

### Multiple areas

You can list multiple groups of tiles in the same location and configure vanilla or custom indoor locations separately:

```json
"VisionIgnoredTiles": {
  "SeedShop": [ "6-8,12", "10,4-6" ],
  "Town": [ "20-24,55", "31,40-43" ],
  "CustomLocationName": [ "4-7,9-11" ]
}
```

Use the location's internal map name, such as `SeedShop` or `Town`. Custom maps must use their own internal location name. If a location has a unique instance name, the mod checks that first and then falls back to the base location name.

Outdoor locations already use open sightlines and don't consult `VisionIgnoredTiles`. In indoor locations, these entries affect the Lots of Kisses visibility checks used for public bystander reactions and for deciding whether a kiss moment is private or public.

They do **not** change collision, movement, pathfinding, placement rules, or the map itself. Only add tiles that visually make sense to see across; ignoring walls or large solid objects can make NPCs react through them.

Tile Marker selections and manual `VisionIgnoredTiles` entries are additive: a tile is ignored if either source contains it. Removing a tile in Tile Marker does not remove the same tile from `config.json`; manually configured entries must still be removed from that file by hand.

---

## Building from Source

```bash
git clone https://github.com/NatrollEXE/LotsOfKisses.git
cd LotsOfKisses
dotnet build
```

Requires the Stardew Valley game path to be set correctly in `Lots_of_Kisses.csproj`.

---

## License

[MIT](LICENSE) — feel free to learn from, fork, or contribute to this project.
