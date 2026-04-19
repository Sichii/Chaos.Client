# Font Modernization — Findings

Documentation of the font-replacement track run in April 2026. Captures what was learned so the work can be picked up cleanly later. Not a plan — a set of findings, technical constraints, and open items.

## Context

**Legacy state:** Dark Ages ships with Gulimche (굴림체), a Korean system font whose Latin side is an afterthought. Pre-rasterized into `eng00.fnt` (bitmap, 8×12 glyphs, fixed 6-px advance). No accented-character support, no serif aesthetic, no thematic connection to the Celtic setting.

**Motivation:** Replace Gulimche with a font that (a) has serifs or clearly distinguished `I`/`l`, (b) supports Latin-1 Supplement + Latin Extended-A accents for player/place/item names (`Síofra`, `Órla`, `Brónach`, etc.), and (c) fits the Dark Ages aesthetic.

## Core finding: `GLYPH_HEIGHT = 12` is the real bottleneck

Every UI layout in the client was designed around a 12-pixel line-height constant ([TextRenderer.cs:16–23](../Chaos.Client.Rendering/TextRenderer.cs#L16-L23), [FontAtlas.cs](../Chaos.Client.Rendering/FontAtlas.cs)):

```csharp
public const int CHAR_WIDTH = 6;
public const int CHAR_HEIGHT = 12;
private const int ENGLISH_GLYPH_WIDTH = 8;
private const int ENGLISH_ADVANCE = ENGLISH_GLYPH_WIDTH - 2;
```

Modern typography fonts (Timeless, Fira Code, JetBrains Mono, Inter, etc.) are designed at 14–18px. Forcing them into a 12px slot either clips them or renders them at sub-native sizes where stroke widths become non-integer and glyphs look mushy. **Font modernization is inseparable from layout modernization** — you can't meaningfully improve typography without lifting the 12-pixel constraint.

This is flagged as "path B" in the UI modernization direction doc ([docs/ui-modernization-direction.md](ui-modernization-direction.md)) and must happen before fonts can be picked purely on aesthetic merit rather than "what fits in 12px."

## Rendering pipeline built during this track

The rendering code now supports a 3-tier loading hierarchy for English glyphs:

1. **BMFont (preferred)** — pre-baked atlas + `.fnt` metadata pair. Handles both binary and text BMFont formats, and both PNG and TGA page atlases. Supports per-glyph `xadvance` (proportional rendering) and per-glyph `xoffset`/`yoffset`. Files in [Chaos.Client.Rendering/BmFont.cs](../Chaos.Client.Rendering/BmFont.cs), [TgaLoader.cs](../Chaos.Client.Rendering/TgaLoader.cs), [FontAtlas.cs](../Chaos.Client.Rendering/FontAtlas.cs).
2. **Runtime-rasterized TTF** — SkiaSharp renders glyph-by-glyph into the atlas at startup. Works but produces fuzzy results because runtime vector-to-bitmap rasterization doesn't snap cleanly to the 12px slot. Useful as a quick-try path before committing a font to BMFont baking.
3. **Legacy DALib `.fnt`** — unchanged fallback. When no modern font is present, everything works as before.

**Auto-discovery:** `FontAtlas.Initialize` scans `Content/Fonts/*.fnt` and uses the first match found. Swap fonts by dropping a new BMFont pair in — no code changes required.

**Codepoint range:** Expanded English glyph lookup from `0x21..0x7E` (ASCII printable only) to `0x21..0x17F` (+ Latin-1 Supplement + Latin Extended-A). `IsKorean` threshold raised from `c > 127` to `c > 0x17F` so accented characters are properly classified as English-side. All three `TextRenderer` draw/measure paths updated for per-glyph advance.

**Premultiplication:** Both TGA and PNG atlas loading premultiply alpha to match SpriteBatch defaults. PNG path also synthesizes alpha from RGB intensity (BMFont PNG output puts glyphs in RGB channels on an opaque black background, unlike TGA which uses the alpha channel directly).

## Font candidates evaluated

| Font | Native size | Outcome | Notes |
|---|---|---|---|
| Microserif | 8px | Too small, stroke thickness inconsistent | Free-for-personal-use license probably not AGPL-compatible |
| Timeless | 12px-ish | Proportional — looked weird at fixed 6px advance | Led to the proportional-advance rewrite |
| MonoSpatial | Unclear (likely 8) | Non-integer scaling, fuzzy | Mono, but renders off-native |
| Fira Code | 14–18 | Too tall, glyphs clipped, `m` loses its middle stroke | Wrong size target for 12px slot |
| Pixellari | 12px native | Legible, ships, 318-char coverage (full Latin Extended-A) | **Currently shipping.** "Not great but legible" |

**Key insight:** Pixel fonts designed natively at 10–12px are the only TTFs that produce clean renders in the 12px slot. Everything else either fuzzes or clips.

## Currently shipping

- **Font:** Pixellari (BMFont-baked) at `Content/Fonts/pixellari.fnt` + `pixellari_0.png`.
- **Coverage:** ASCII printable + Latin-1 Supplement + Latin Extended-A.
- **Aesthetic:** acceptable, not beautiful. Clear upgrade over Gulimche for accent coverage and `I`/`l` distinction. Retains pixel aesthetic appropriate to the Dark Ages visual style.
- **Rendering:** proportional per-glyph advance, crisp binary pixels (no AA), pre-baked from TTF to atlas offline via BMFont tool.

## Open items

### Accented chars in chat bubbles (verify)

User observed mid-session that accented characters didn't render in chat bubbles. Observation predates the BMFont rebuild + Pixellari swap. Pipeline analysis suggests this *should* work now:

- Pixellari `.fnt` contains Latin Extended-A chars.
- `ChatBubble.WordWrap` doesn't filter by charset.
- `TextRenderer.DrawText`/`DrawTextClipped` (via `UIElement.DrawTextClipped` wrapper) handles `0x21..0x17F`.

Verify with a test message containing `à`, `é`, `ñ`. If still broken, debug. Likely a 10-minute fix; possibly already fixed by the range expansion + BMFont load.

### Decouple `GLYPH_HEIGHT` from layout (blocked on UI modernization)

The real font work is blocked on lifting the 12-pixel constraint. When the UI modernization track executes, this is the unlock:

- `GLYPH_HEIGHT`, `CHAR_WIDTH`, `ENGLISH_GLYPH_WIDTH`, `ENGLISH_ADVANCE` become font-derived, not hardcoded.
- Every UI layout that reserves space per "line" in pixels must transition to a "line count × font-derived line-height" model.
- Unblocks picking fonts at their native design sizes (14–18px for modern typography fonts, 16–20px for display serifs).

Font candidates queued for re-evaluation once the slot constraint is lifted:

- **Alagard** (16px fantasy pixel serif, SIL OFL) — excellent thematic fit for Dark Ages Celtic aesthetic. Blocked only by size.
- **Compass Pro** — delicate pixel serif body text companion.
- **JetBrains Mono** / **Fira Code** — for any dev-leaning UI (debug overlays, console). Render beautifully at 14+.
- **IBM Plex Mono** / **Courier Prime** — serif mono alternatives for body text.

### Korean font path untouched

Modernization focused on English only. Korean still loads via legacy DALib `.fnt` via EUC-KR (codepage 949). If Hybrasyl ever targets Korean-language deployments, this is a separate modernization track. Kept running as-is.

### BMFont baking workflow

BMFont ([angelcode.com/products/bmfont](http://www.angelcode.com/products/bmfont/)) is the tool used. Recipe that produced the shipping Pixellari:

- Font size: 12 (native)
- AA: off (crisp binary pixels for pixel font)
- Output: binary `.fnt` + PNG atlas
- Charset: ASCII + Latin-1 Supplement + Latin Extended-A (codepoints `32–383`)
- Texture size: 256×256 sufficed; 512×512 for larger fonts

For smooth TTFs (non-pixel), enable AA. Anti-aliased edges render correctly through the premultiplied pipeline.

## Code artifacts produced

New:
- [Chaos.Client.Rendering/BmFont.cs](../Chaos.Client.Rendering/BmFont.cs) — binary + text BMFont parser
- [Chaos.Client.Rendering/TgaLoader.cs](../Chaos.Client.Rendering/TgaLoader.cs) — 8-bit grayscale + 32-bit BGRA TGA decoder

Modified:
- [Chaos.Client.Rendering/FontAtlas.cs](../Chaos.Client.Rendering/FontAtlas.cs) — 3-tier loading, auto-discovery, `GlyphInfo` record with per-glyph advance/offsets
- [Chaos.Client.Rendering/TextRenderer.cs](../Chaos.Client.Rendering/TextRenderer.cs) — per-glyph advance, `0x21..0x17F` range, Korean threshold raised
- [Chaos.Client/Chaos.Client.csproj](../Chaos.Client/Chaos.Client.csproj) — `Content/**` copy-to-output

## Status

**Parked.** Shipping Pixellari via BMFont as the interim font. Real font work resumes as part of UI modernization — specifically after `GLYPH_HEIGHT` is decoupled from layout. All pipeline infrastructure (BMFont loader, proportional rendering, extended codepoint range, auto-discovery) is in place and will carry forward unchanged.
