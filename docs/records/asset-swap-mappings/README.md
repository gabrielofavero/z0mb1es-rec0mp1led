# Version 1 visual mappings

These JSON files contain metadata only. `i-maed-360.json`, `i-maed-steam.json`, and `time-viking-360.json` record one source's possible phone elements. `endless-360.json` selects matching candidates in Xbox 360 I MAED, Steam I MAED, Xbox 360 TIME VIKING, then phone Original order. Xbox 360 I MAED can supply 121 Endless elements, Steam I MAED 113, and TIME VIKING 168; combining Xbox 360 I MAED and TIME VIKING supplies 179.

`candidate` records a source crop, its original and decoded file hashes, its phone fallback, comparison measures, and physical/logical geometry. It is not a runtime approval. `uncertain` and `original` use the phone fallback. Dynamic phone rectangles retain their original expression; expanded possible frames have separate IDs. The [P0 evidence](../asset-swap-p0.md) explains the decisions and reproduction commands.

The eight reviewed Xbox 360 player and monster frames intentionally use clean 360 alpha. Their phone equivalents have baked shadows that reach the atlas cell boundary; this accounts for their low alpha overlap scores and the clipped dark shapes when they fall back to phone art.
