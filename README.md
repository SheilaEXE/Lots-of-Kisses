# Lots of Kisses 💕

A mod for Stardew Valley that expands romantic kiss interactions with your spouse or dating partner.

---

## Features

- **Bump Kiss** — share a quick kiss when you run toward your partner; small chance of knocking an item out of their hands
- **Multi-Kiss Sequences** — hold your position near your partner for continuous kiss cycles
- **Public Reactions** — nearby NPCs with line of sight will notice and react with embarrassed emotes and dialogue
- **Partner Dialogue** — your partner embarrassed comments during kiss sequences you're in public
- **Blush Smoke Styles** — choose between two blush smoke animation styles in the config menu
- **Polyamory Support** — compatible with polyamory mods; all romantic partners are recognized
- **Content Pack Support** — add dialogue for a NPC custom ones from other mods

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

### Note

NPCs without a content pack still work fully — kisses happen normally, just without custom dialogue.

---

## Compatibility

- ✅ Works with spouses and dating partners
- ✅ Compatible with polyamory mods (all recognized romantic partners are supported)
- ✅ Compatible with any romanceable NPC custom via content pack
- ✅ Cross-mod compatibility with [Outfit Reactions](https://github.com/NatrollEXE) via `modData` flags

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
