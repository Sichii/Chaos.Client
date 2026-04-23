# Character Creator Revamp — Scoping Notes

Starting-point scoping for `feature/character-creator`. Captures the current flow, gaps vs. retail parity, and candidate directions for a revamp. **Nothing here is committed design** — this branch is a scratch space. Revisit and prune as exploration lands.

## Current Flow

```
LobbyLoginScreen
  └── "Create" button
        └── CharacterCreationControl.Show()
              └── OK clicked
                    └── client-side validate (name non-empty, password match)
                          └── CreateCharInitial(name, password)          ─── server ───▶
                                                                        ◀─── Confirm ───
                                └── CreateCharFinalize(hairStyle, gender, hairColor) ─── server ───▶
                                                                        ◀─── Confirm ───
                                      └── success popup → return to lobby
```

### File / line pointers

| Area | Location |
|---|---|
| Entry from Create button | [Chaos.Client/Screens/LobbyLoginScreen.cs:216-222](../Chaos.Client/Screens/LobbyLoginScreen.cs#L216-L222) |
| Client-side validation + `CreateCharInitial` send | [Chaos.Client/Screens/LobbyLoginScreen.cs:224-250](../Chaos.Client/Screens/LobbyLoginScreen.cs#L224-L250) |
| Server response handling + `CreateCharFinalize` send | [Chaos.Client/Screens/LobbyLoginScreen.cs:470-522](../Chaos.Client/Screens/LobbyLoginScreen.cs#L470-L522) |
| UI, preview, hair/color/direction cycling | [Chaos.Client/Controls/LobbyLogin/CharacterCreationControl.cs](../Chaos.Client/Controls/LobbyLogin/CharacterCreationControl.cs) |
| 5-frame walk preview renderer | [Chaos.Client.Rendering/AislingRenderer.cs:631-663](../Chaos.Client.Rendering/AislingRenderer.cs#L631-L663) |
| Packet dispatch (`CreateCharInitial`, `CreateCharFinalizeArgs`) | [Chaos.Client.Networking/ConnectionManager.cs:203-231](../Chaos.Client.Networking/ConnectionManager.cs#L203-L231) |
| Control prefab | `_ncreate.txt` (loaded via `DataContext.UserControls.Get("_ncreate")`) |

### UI surface the player sees

- **Text**: Name, Password, Password confirm
- **Gender**: Male / Female toggle (binary; `Gender` enum also defines `None`/`Unisex` — unused here)
- **Hair style**: cycled 1–18 (male) or 1–17 (female) via HairLeft/HairRight
- **Hair color**: 14-swatch grid (Teal, Green, Olive, Yellow, Pumpkin, Apple, Violet, Default/Lavender, Navy, Blue, Gray, Carrot, Brown, Black)
- **Direction rotation**: 4-way preview-only via AngleLeft/AngleRight
- **Preview**: live 5-frame walk cycle, 350 ms frame pacing, hardcoded `BODY_ID = 1`, no equipment layers

### Wire protocol (Chaos.Networking)

| Packet | Fields |
|---|---|
| `CreateCharInitialArgs` | `string Name`, `string Password` |
| `CreateCharFinalizeArgs` | `byte HairStyle`, `Gender Gender`, `DisplayColor HairColor` |

Server responses routed through `LoginMessageArgs` with `LoginMessageType` values `Confirm` / `ClearNameMessage` / `ClearPswdMessage` / generic errors.

## Gaps vs. Retail Parity

- No class / path selection at creation (original DA offered Rogue / Mage / Priest / Warrior with starting ability implications)
- No body type or overcoat customization — hardcoded `BODY_ID = 1`
- No equipment / dye preview — player sees only hair + body, never the armor they'll be wearing
- Hair color swatch has no labels
- No client-side name validation beyond non-empty (length, reserved chars, forbidden punctuation all come back as generic server rejects)
- No password requirements display (length, strength)
- Generic server error strings on reject

## Revamp Candidates (Unordered Punch List)

1. **Full-appearance preview** — render equipment, overcoat, weapon silhouette, dye layers on the preview aisling (not just hair + body)
2. **Class / path selection** — path chooser with starting-ability preview panel
3. **Body sprite / overcoat customization** — surface body IDs beyond 1; optional overcoat variant
4. **Client-side name validation** — live feedback for length, reserved chars, character-class rules before the server round-trip
5. **Dye preview for worn gear** — full-body swatches, not hair-only
6. **Password requirements** — strength indicator or min-length hint
7. **Swatch improvements** — labeled color names, larger / more readable picker
8. **Server schema extension** — any new fields (class, body, dye) would need a new `CreateCharFinalize` shape. Candidate for a custom opcode starting at `0xFF` per the modernization convention, negotiated via capability handshake.

## Strategic Direction

**Additive modernization, not refactor.** Build `CharacterCreationControlV2` alongside the existing `CharacterCreationControl` and gate which one shows via capability handshake with the server. Legacy `CreateCharInitial` / `CreateCharFinalize` opcodes stay untouched and keep working for retail-compatible servers. Any new fields ride a new opcode.

## Out of Scope (For This Branch)

- Implementation decisions (what to actually build first)
- Server changes (opcode allocation, protocol extensions)
- DALib changes
- Asset pipeline changes (new sprite packs, body variants, etc.)

This branch is sketching only. When we converge on a specific target, a follow-up plan with phase-level review gates takes over.
