# Character Creator Revamp — Scoping Notes

Starting-point scoping for `feature/character-creator`. Captures the current flow, gaps vs. retail parity, and candidate directions for a revamp. **Nothing here is committed design** — this branch is a scratch space. Revisit and prune as exploration lands.

## Current Flow

```text
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
| --- | --- |
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
| --- | --- |
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

## Scope Expansion

This started as a character-creator-only revamp. It grew to cover the full onboarding flow (lobby → account login → character select → create → world) because several creator features — server-gated species lists, server-provided clan names — require account identity before the server can filter. Splitting them across branches would mean retrofitting the creator against account context later; cheaper to treat onboarding as one coherent modernization.

The branch name stays `feature/character-creator` as a shorthand; the doc covers the full surface.

## Revamp Candidates (Unordered Punch List)

1. **Account login + character select** — replace per-character login with an account → character list → select-or-create flow. Prerequisite for anything server-gated per account. Additive: legacy per-character login stays alongside for retail-compat servers.
2. **Capability handshake** — one-time negotiation so the client knows which modernization features the server supports (species list, clan lookup, new create-char schema, etc.). Amortizes across everything downstream.
3. **Species selection** — gate on server capability. Fallback to a hardcoded starter five when server doesn't advertise it; server eventually provides an account-filtered list (players earn non-starter species outside the initial five via account manager). Rendering approach for the first non-human species (dwarves) is scoped in [species-rendering-dwarves.md](species-rendering-dwarves.md).
4. **Clan names** — client-set at creation initially; server-pulled from account manager once that system exists.
5. **Full-appearance preview** — render equipment, overcoat, weapon silhouette, dye layers on the preview aisling (not just hair + body).
6. **Class / path selection** — path chooser with starting-ability preview panel. Confirm how server currently handles starting loadout before scoping client work.
7. **Body sprite / overcoat customization** — surface body IDs beyond 1; optional overcoat variant.
8. **Client-side name validation** — live feedback for length, reserved chars, character-class rules before the server round-trip.
9. **Dye preview for worn gear** — full-body swatches, not hair-only.
10. **Password requirements** — strength indicator or min-length hint.
11. **Swatch improvements** — labeled color names, larger / more readable picker.
12. **Server schema extension** — new fields (species, clan, class, body, dye) need a new `CreateCharFinalize` shape. Candidate for a custom opcode starting at `0xFF` per the modernization convention, negotiated via the capability handshake.

## Proposed Order

Coarse → fine. Earlier items unblock later ones.

1. **Account login + character select screen** — introduces account context; legacy per-character login coexists.
2. **Capability handshake** — amortizes across every modernization feature below.
3. **Species** — client-local starter list first, then swap to server-provided filtered list once the server exposes it.
4. **Clan names** — client-set first; server-pulled follow-up once the account manager can serve them.
5. **Class / path selection** — after species, since class may constrain by species.
6. **Full-appearance preview** — equipment, overcoat, weapon silhouette, dye layers.
7. **Polish** — name validation UX, password hints, swatch labels, body / overcoat customization.

## Strategic Direction

**Additive modernization, not refactor.** Build `CharacterCreationControlV2` (and an account-login + character-select path) alongside the existing controls. Gate via the capability handshake. Legacy `CreateCharInitial` / `CreateCharFinalize` opcodes stay untouched and keep working for retail-compatible servers. Any new fields ride a new opcode starting at `0xFF`.

## Out of Scope (For This Branch)

- Server-side implementation of account manager, species catalog, clan registry, new opcodes. Client-side integration against planned server protocols is in scope; the server work lands in the Hybrasyl repo separately.
- DALib changes.
- Asset pipeline changes (new sprite packs, body variants, etc.) beyond what's needed to render new appearance layers the server already supports.

This doc is sketching. Each item above — once we commit to implementing it — gets its own plan with phase-level review gates per the repo's review policy.
