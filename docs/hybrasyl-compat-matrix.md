# Hybrasyl / Chaos.Networking Server-to-Client Compatibility Matrix

Target: make the Chaos.Client (which consumes `Chaos.Networking 1.11.0-preview`) parse packets emitted by the Hybrasyl private server (`E:\Dark Ages Dev\Repos\server`, branch `main`).

Chaos-Server reference: master @ "Release v1.10" (`E:\Dark Ages Dev\Repos\Chaos-Server\Chaos.Networking\Converters\Server\*.cs`).

---

## 1. Summary

### Refreshed status table (Apr 2026)

Status of each originally-divergent opcode after empirical testing against the Hybrasyl QA server:

| Opcode | Name | Original | Current | Notes |
|---|---|---|---|---|
| 0x04 | Location | DIVERGENT | STILL-DIVERGENT (harmless) | Trailing bytes ignored by Chaos reader; no observable bug |
| 0x05 | UserId | DIVERGENT | VERIFIED-COMPATIBLE | Character identity / gender / paperdoll render correctly |
| 0x07 | DisplayVisibleEntities | DIVERGENT | STILL-DIVERGENT (functionally-working) | Hybrasyl's one-per-packet pattern parses as one-element batch; entities visible |
| 0x0D | DisplayPublicMessage / CastLine | SEMANTIC-DIFF | UNINSPECTED (chant variant) | Chat variant works; CastLine chant-above-head behavior needs testing |
| 0x15 | MapInfo | DIVERGENT | PATCHED | Client synthesizes MapChangePending / MapLoadComplete / MapChangeComplete from 0x15 receipt |
| 0x1A | BodyAnimation | DIVERGENT | VERIFIED-COMPATIBLE | Reader lenient enough that animations play correctly |
| 0x1F | MapChangeComplete | MISSING-HYBRASYL | SYNTHESIZED | Client generates internally after MapInfo |
| 0x29 | Animation | DIVERGENT | UNINSPECTED | Spell/ability visual effects — pending testing |
| 0x2F | DisplayMenu | DIVERGENT | VERIFIED-COMPATIBLE | NPC dialogs and menus interact correctly |
| 0x31 | DisplayBoard | DIVERGENT | VERIFIED-COMPATIBLE | Mail works end-to-end |
| 0x32 | Door / UserMoveResponse | SEMANTIC-DIFF | UNINSPECTED | Door toggle / move rejection — pending testing |
| 0x33 | DisplayAisling | DIVERGENT | STILL-DIVERGENT (unverified) | Monster helmet-vs-headSprite branch unresolved; visual impact uncertain |
| 0x34 | OtherProfile | DIVERGENT | VERIFIED-COMPATIBLE | Other-player profile panels display correctly |
| 0x39 | SelfProfile | DIVERGENT | VERIFIED-COMPATIBLE | Self profile panel displays correctly |
| 0x56 | ServerTableResponse | ALREADY-PATCHED | PATCHED | Zlib decompression workaround in place |
| 0x60 | LoginNotice | ALREADY-PATCHED | PATCHED | Zlib decompression workaround in place |
| 0x63 | DisplayGroupInvite | DIVERGENT | UNINSPECTED | Group invite prompt subtype codes — pending testing |
| 0x67 | MapChangePending | MISSING-HYBRASYL | SYNTHESIZED | Client generates internally after MapInfo |
| 0x68 | SynchronizeTicks | SEMANTIC-DIFF | VERIFIED-COMPATIBLE | Auto-echo keepalive working |

**Net: zero confirmed-broken opcodes as of Apr 2026.** Most of the original divergences are either patched, verified compatible in practice, or functionally-working despite byte-level difference. Four opcodes remain unverified pending feature testing — those are the only live candidates for Phase 2 fixes.

### Original snapshot table (Dec 2025)

| Status | Count | Meaning |
|-------|-------|---------|
| **MATCH**           | 22 | Bytes line up well enough that Chaos.Networking's converter should parse Hybrasyl's output without change. |
| **DIVERGENT**       | 14 | Byte layout differs in a way that will miss-parse or crash. |
| **SEMANTIC-DIFF**   | 6  | Same opcode, genuinely different meaning / packet family. |
| **MISSING-HYBRASYL**| 8  | Chaos opcode that Hybrasyl never emits (or defines a class for but never instantiates). |
| **MISSING-CHAOS**   | 21 | Hybrasyl opcode with no corresponding Chaos.Networking converter. |
| **ALREADY-PATCHED** | 4  | Divergence already worked around in the client. |
| **Total audited**   | 75 unique opcodes |

Refreshed snapshot (Apr 2026, after 5 months of client work):

| Original status | Current status | Count | Notes |
|---|---|---|---|
| DIVERGENT | PATCHED | 1 | MapInfo (0x15) — client synthesizes MapChangePending/MapLoadComplete/MapChangeComplete |
| DIVERGENT | VERIFIED-COMPATIBLE | 1 | BodyAnimation (0x1A) — Chaos.Networking's reader is lenient enough in practice |
| DIVERGENT | STILL-DIVERGENT (but functionally-working) | ~5 | e.g. DisplayVisibleEntities (0x07), Location (0x04) — Chaos's reader ignores trailing bytes; feature works end-to-end despite byte-layout difference |
| DIVERGENT | STILL-DIVERGENT (genuinely broken or silently wrong) | ~6 | Needs P0/P1 triage; see section 3a |
| SEMANTIC-DIFF | VERIFIED-COMPATIBLE | 1 | SynchronizeTicks (0x68) — auto-echo keeps keepalive working |
| SEMANTIC-DIFF | STILL-DIVERGENT | ~5 | Genuine semantic mismatches still unhandled |
| MISSING-HYBRASYL | SYNTHESIZED | 2 | MapChangeComplete (0x1F), MapChangePending (0x67) — client generates these internally |
| ALREADY-PATCHED | PATCHED | 4 | Unchanged. Notably ServerTableResponse (0x56) + LoginNotice (0x60) got their zlib workarounds patched here. |

**Key insight from refresh:** the original matrix categorized divergences by *byte layout*, but some technically-divergent opcodes work fine in practice because Chaos.Networking's readers tolerate trailing bytes or slight shape differences. The actionable divergences are those where field *order*, field *meaning*, or *variant selection* genuinely differ — not those where Hybrasyl just adds extra trailing bytes.

Chaos.Networking defines 58 server converters over 48 unique opcode values; Hybrasyl's `OpCodes` enum enumerates 74 opcode constants. The union is 75 opcodes (0x01 `NewUserCheck` is Hybrasyl-only and is really a *client* opcode repurposed on this enum; it is not a server-to-client packet).

---

## 2. Methodology note

Comparisons are byte-layout first. For each opcode, the Chaos-Server `Serialize(ref SpanWriter, TArgs)` method (under `Chaos.Networking/Converters/Server/`) was read against the matching Hybrasyl emitter — either a `Networking/ServerPackets/*.cs` `Packet()` builder or an inline `new ServerPacket(0xNN)` in `Servers/*.cs`, `Objects/User.cs`, `Objects/Creature.cs`, etc. A Hybrasyl `ServerPackets/*.cs` class was only counted as "sent" if ripgrep found at least one `new <ClassName>(` construction outside the class file itself (or a direct `.Packet()` emission from the class body). Opcodes are the stable identifier because Chaos.Networking's dispatcher is indexed by opcode, not by packet class. Semantics (what the client *does* with the parsed payload) are only noted for the SEMANTIC-DIFF rows where the two systems genuinely disagree on what the opcode *means*.

---

## 3. Main compatibility matrix

| Opcode | Chaos name | Hybrasyl name | Status | Fix location | Notes |
|--------|-----------|---------------|--------|--------------|-------|
| 0x00 | ConnectionInfo | CryptoKey | MATCH | ok | Both: `[00]{crc:u32}{seed:u8}{keyLen:u8}{key}`. Hybrasyl writes length then bytes via `client.EncryptionKey.Length` — same wire shape. |
| 0x01 | — | NewUserCheck | MISSING-CHAOS | n/a | Hybrasyl defines as a client opcode value; no server-to-client use. Ignore. |
| 0x02 | LoginMessage | LoginMessage | MATCH | ok | `{type:u8}{msg:str8}`. Identical. |
| 0x03 | Redirect | Redirect | MATCH | ok | `{ip[4]:reversed}{port:u16}{remaining:u8}{seed:u8}{key:str8}{name:str8}{id:u32}`. Chaos and Hybrasyl (Client.cs:505) emit the same shape. |
| 0x04 | Location | Location | **DIVERGENT** | client parse | Chaos: 4 bytes `X,Y` as u16/u16. Hybrasyl: 8 bytes — appends two hard-coded `0x000B 0x000B` trailing u16s. See §4.1. |
| 0x05 | UserId | UserId | **DIVERGENT** | client parse | Gender shift and unknown-byte drift. See §4.2. |
| 0x06 | — | MapEdit | MISSING-CHAOS | n/a | No server->client usage found in Hybrasyl either. Safe to ignore. |
| 0x07 | DisplayVisibleEntities | AddWorldObject | **DIVERGENT** | client parse | Chaos sends a single packet with N entities keyed by sprite-offset. Hybrasyl emits **one packet per entity** with a hard-coded count of 1 and a different intra-entity layout. See §4.3. |
| 0x08 | Attributes | Attributes | MATCH | ok | Same StatUpdateFlags-keyed sections. Hybrasyl writes 4-byte `uint.MinValue` at the tail of Primary (Chaos also reserves 4 bytes `42 00 88 2E`); Chaos reads/discards them. Compatible. |
| 0x09 | — | Inventory | MISSING-CHAOS | n/a | Client-only opcode on Hybrasyl's enum. Not emitted. |
| 0x0A | ServerMessage | SystemMessage | MATCH | ok | `{type:u8}{msg:str16}`. `SettingsMessage` reuses opcode but writes `{type:u8}{0:u8}{len+1:u8}...` — that's actually written into `SystemMessage`'s u16-length field as `(0 << 8) | (len+1)`, so legitimate SettingsMessage frames will parse as an unusually short ServerMessage on the client. Chaos treats as plain ServerMessage. Acceptable. |
| 0x0B | ClientWalkResponse | UserMove | MATCH | ok | `{dir:u8}{oldX:u16}{oldY:u16}{0x000B:u16}{0x000B:u16}{0x01:u8}`. Chaos reader intentionally skips the trailing 5 bytes. Compatible. |
| 0x0C | CreatureWalk | CreatureMove | MATCH | ok | `{id:u32}{oldX:u16}{oldY:u16}{dir:u8}{0:u8}`. Identical. |
| 0x0D | DisplayPublicMessage | CastLine | **SEMANTIC-DIFF** | client parse / fork | Chaos: chat-above-head style public message `{type:u8}{id:u32}{msg:str8}`. Hybrasyl CastLine: `{chatType:u8}{targetId:u32}{lineLen:u8}{raw text}{00 00 00}`. See §4.4. Also inline Hybrasyl 0x0D at `Objects/User.cs:457` (`{shout:bool}{id:u32}{msg:str8}`) — this one parses cleanly as Chaos 0x0D. |
| 0x0E | RemoveEntity | RemoveWorldObject | MATCH | ok | Both write just `{id:u32}`. |
| 0x0F | AddItemToPane | AddItem | MATCH | ok | `{slot:u8}{sprite+0x8000:u16}{color:u8}{name:str8}{count:i32|u32}{stackable:bool}{maxDur:u32}{curDur:u32}`. Hybrasyl writes an extra `0x00000000:u32` trailing (User.cs:1599) that Chaos's reader ignores. Compatible. |
| 0x10 | RemoveItemFromPane | RemoveItem | MATCH | ok | Chaos reads only `{slot:u8}`. Hybrasyl writes `{slot:u8}{0x0000:u16}{0x00:u8}` — extra trailing bytes are harmless (Chaos reads only slot, then drops the rest). |
| 0x11 | CreatureTurn | CreatureDirection | MATCH | ok | `{id:u32}{dir:u8}`. Identical. |
| 0x12 | — | Guild | MISSING-CHAOS | n/a | Hybrasyl enum constant only; no emission found. |
| 0x13 | HealthBar | HealthBar | MATCH | ok | `{id:u32}{0:u8}{hpPct:u8}{sound?:u8}`. Identical. |
| 0x14 | — | PasswordCheck | MISSING-CHAOS | n/a | Defined on Hybrasyl enum; used as a *client* opcode (password change). No s->c emission. |
| 0x15 | MapInfo | MapInfo | **DIVERGENT** | client parse | Byte field widths diverge. Chaos reads `{id:i16}{w:u8}{h:u8}{flags:u8}{2 unknown}{checksum:u16}{name:str8}`. Hybrasyl writes `{id:u16}{w%256:u8}{h%256:u8}{flags:u8}{w/256:u8}{h/256:u8}{chk%256:u8}{chk/256:u8}{name:str8}` — i.e. the width/height high-order bytes occupy what Chaos reads as "2 unknown bytes". For maps under 256 tiles this is identical in practice, but the flags Hybrasyl emits also differ (it OR-folds `Snow|Rain` for Dark, and sets bit 128 for Snow again). See §4.5. |
| 0x16 | — | PacketMapping | MISSING-CHAOS | n/a | Client-only on Hybrasyl; no emission. |
| 0x17 | AddSpellToPane | AddSpell | MATCH | ok | `{slot:u8}{sprite:u16}{useType:u8}{name:str8}{prompt:str8}{lines:u8}`. Identical. |
| 0x18 | RemoveSpellFromPane | RemoveSpell | MATCH | ok | Chaos reads `{slot:u8}`, Hybrasyl writes `{slot:u8}{0x00:u8}`. Harmless trailing byte. |
| 0x19 | Sound | PlaySound | MATCH | ok | Chaos sniffs for `0xFF` prefix to decide music vs sound. Hybrasyl writes `{0xFF:u8}{track:u8}` for music and `{sound:u8}` for sfx. NOTE: Chaos music branch reads an extra 2-byte trailer that Hybrasyl does not emit — `SpanReader.ReadByte` on an empty tail in Chaos.IO returns `0` without throwing, so this degrades to "two zeros" and still works. Confirm on real traffic. |
| 0x1A | BodyAnimation | PlayerAnimation | **DIVERGENT** | client parse | Field order + signedness. See §4.6. |
| 0x1B | DisplayEditableNotepad | EditablePaper | MATCH | ok | `{slot:u8}{type:u8}{w:u8}{h:u8}{text:str16}`. Identical. |
| 0x1C | — | Icon | MISSING-CHAOS | — | Hybrasyl enum only. Not emitted. |
| 0x1D | — | ChangeShape | MISSING-CHAOS | — | Hybrasyl enum only. Not emitted. |
| 0x1E | — | ChangeDay | MISSING-CHAOS | — | Hybrasyl enum only. Not emitted. |
| 0x1F | MapChangeComplete | MapChangeCompleted | MATCH* | synthesize client-side | Chaos writes `{00 00}`; Hybrasyl never emits (defined on enum but no `new ServerPacket(0x1F)` callsite). Client already synthesizes from 0x15. See §5. |
| 0x20 | LightLevel | ChangeHour | MISSING-HYBRASYL | server change / client default | Defined on Hybrasyl enum as `ChangeHour` but no emission site. Chaos-Server *does* send. Client should assume "full light" if not received. |
| 0x21 | — | SelfSave | MISSING-CHAOS | — | Client opcode only. |
| 0x22 | RefreshResponse | Refresh | MATCH | ok | Chaos writes empty body; Hybrasyl writes `{0x00:u8}`. Trailing byte ignored. |
| 0x24 | — | BattlefieldInfo | MISSING-CHAOS | — | Hybrasyl enum only. No emission. |
| 0x25 | — | DirectMove | MISSING-CHAOS | — | Hybrasyl enum only. No emission. |
| 0x26 | — | ActionChange | MISSING-CHAOS | — | Hybrasyl enum only. No emission. (0x26 is also a client opcode — ChangePassword in Login.) |
| 0x27 | — | GeneralEffect | MISSING-CHAOS | — | Hybrasyl enum only. No emission. |
| 0x28 | — | CloseConnection | MISSING-CHAOS | — | Hybrasyl enum only. No emission. |
| 0x29 | Animation | SpellAnimation | **DIVERGENT** | client parse | Chaos variant selector is `targetId == 0`; Hybrasyl's `SpellAnimation.Packet()` prepends a leading `0x00` byte, and its inline `User.cs` emitters use different length for the "ground" variant. See §4.7. |
| 0x2A | — | AddContainer | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x2B | — | RemoveContainer | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x2C | AddSkillToPane | AddSkill | MATCH | ok | `{slot:u8}{icon:u16}{name:str8}`. Identical. |
| 0x2D | RemoveSkillFromPane | RemoveSkill | MATCH | ok | Chaos reads `{slot:u8}`; Hybrasyl writes `{slot:u8}{0x00:u8}`. Trailing byte ignored. |
| 0x2E | WorldMap | FieldMap | MATCH* | client parse | Both carry world-map payload. Hybrasyl writes `map.GetBytes()` wholesale (opaque blob) — exact internal layout differs from Chaos `WorldMapConverter`. Same high-level purpose but the inner bytes are not guaranteed to agree; treat as DIVERGENT if the client actually depends on Chaos's `WorldMapArgs` shape. Marking MATCH pending empirical test because the legacy DA client format is what matters; assume Hybrasyl mimics it. |
| 0x2F | DisplayMenu | NpcReply | **DIVERGENT** | converter fork | Byte-5 offset, color width differs, and Hybrasyl omits several Chaos menu subtypes entirely (only supports Options / OptionsWithArgument / Input / InputWithArgument / MerchantShopItems / MerchantSkills / MerchantSpells / User{Inventory,Skill,Spell}Book). Leading byte after OpCode is the `MerchantDialogType` which is keyed differently from Chaos's `MenuType`. Large enough that a custom parse path is justified. See §4.8. |
| 0x30 | DisplayDialog | Pursuit | **SEMANTIC-DIFF** / MATCH | ok | Same opcode, both represent "dialog to the user". Hybrasyl's `Dialog.cs:150` writes essentially the same header: `{dialogType:u8}{entType:u8}{id:u32}{0:u8}{sprite:u16}{color:u8}{0:u8}{sprite:u16}{color:u8}{seqId:u16}{idx:u16}{prev:bool}{next:bool}{0:u8}{name:str8}{text:str16}`. The trailing `0:u8` (vs Chaos's `!shouldIllustrate:bool`) is the only divergence — on Hybrasyl it is always zero, which in Chaos is parsed as `ShouldIllustrate = true`. Safe in practice. Close-dialog subtype: Hybrasyl `SendCloseDialog()` writes `{0x0A:u8}{0x00:u8}`, Chaos expects `{(byte)DialogType.CloseDialog:u8}` then stops — dialog-type value 0x0A *is* `CloseDialog` on Chaos's enum, so it parses cleanly. |
| 0x31 | DisplayBoard | Board | **DIVERGENT** | converter fork | Chaos's `BoardOrResponseType` codes (0..5) map loosely to Hybrasyl's `BoardResponseType`, but the sub-layouts diverge: Hybrasyl's DisplayList writes `{0x01:u8}{boards.Count+1:u16}{0:u16}{"Mail":str8}{(id,name)...}` whereas Chaos reads a `count:u16` of `{id:u16}{name:str8}` entries. Hybrasyl's GetMailMessage/GetBoardMessage diverge in the highlight flag and type discriminator. See §4.9. |
| 0x32 | Door | UserMoveResponse | **SEMANTIC-DIFF** | client parse | Chaos: door update packet — `{count:u8}[{x:u8}{y:u8}{closed:bool}{openRight:bool}]*`. Hybrasyl emits **both** purposes on this opcode: `User.cs:3075 SendDoorUpdate` matches the Chaos door format (`{1:u8}{x:u8}{y:u8}{state:bool}{lr:bool}`), *and* `User.cs:2041` sends `{0x00:u8}` as a standalone "move-response" right after every walk. The trailing single-byte variant crashes Chaos's DoorConverter if dispatched normally. See §4.10. |
| 0x33 | DisplayAisling | DisplayUser | MATCH (caveats) | client parse | Same high-level layout: `{x:u16}{y:u16}{dir:u8}{id:u32}{headSprite:u16}{body...}{nameStyle:u8}{name:str8}{groupName:str8}`. Hybrasyl's "DisplayAsMonster" branch writes `{helmet:u16}{monsterSprite:u16}{hairColor:u8}{bootsColor:u8}{six zeros}` — Chaos's `headSprite==0xFFFF` branch decodes the same shape minus the first `helmet:u16`, which Hybrasyl always emits unconditionally before the branch. ACTUAL DIFF: Chaos keys on `headSprite==0xFFFF` for monster, Hybrasyl keys on a `DisplayAsMonster` flag while still writing a real `helmet:u16` first → for Hybrasyl monsters the client will interpret `helmet` as `headSprite` and fall into the humanoid branch. Flag as probable DIVERGENT; empirical testing needed. |
| 0x34 | OtherProfile | Profile | MATCH (caveats) | client parse | Both: `{id:u32}{18×(sprite:u16,color:u8)}{socialStatus:u8}{name:str8}{nation:u8}{title:str8}{grouping:bool}{guildRank:str8}{displayClass:str8}{guildName:str8}{markCount:u8}[...marks]{portraitLen+textLen+4:u16}{portrait:u16data}{profile:str16}{0:u8}`. Hybrasyl (User.cs:964) omits the trailing Chaos-specific bytes (`{0:u8}{display:u16}{0x02:u8}{0:u32}{0:u8}`) and also writes portrait differently. See §4.11. |
| 0x35 | DisplayReadonlyNotepad | ReadonlyPaper | MATCH | ok | `{type:u8}{w:u8}{h:u8}{centered/0:u8/bool}{text:str16}`. Identical bytes (Chaos's 4th byte is "always zero"; Hybrasyl writes the `Centered` boolean — zero in the common case). |
| 0x36 | WorldList | UserList | MATCH | ok | `{worldCount:u16}{countryCount:u16}[{classAndFlags:u8}{color:u8}{socialStatus:u8}{title:str8}{isMaster:bool}{name:str8}]*`. Hybrasyl's (Servers/World.cs:2246) guild-bit packing differs (it writes raw `(byte)user.Class` and then a separate `84|151|255` color byte, bit 0x08 for guilded is not set), so Chaos's guilded-bit extraction will be wrong but content will decode. |
| 0x37 | Equipment | AddEquipment | MATCH | ok | `{slot:u8}{sprite+0x8000:u16}{color:u8}{name:str8}{0:u8}{maxDur:u32}{curDur:u32}`. Identical. |
| 0x38 | DisplayUnequip | RemoveEquipment | MATCH | ok | Chaos: `{slot:u8}`. Hybrasyl `SendRefreshEquipmentSlot` writes `{slot:u8}`. Identical. |
| 0x39 | SelfProfile | SelfProfile | **DIVERGENT** | converter fork | Hybrasyl's `PlayerProfile` (0x39) writes: `{nation:u8}{guildRank:str8}{title:str8}{groupString:str8}{canGroup:bool}{hasRecruit:bool}{…recruit?}{class:u8}{0:u8}{0:u8}{className:str8}{guildName:str8}{legendCount:u8}[...marks]{0:u8}{playerDisplay:u16}{0x02:u8}{0:u32}{0:u8}`. Chaos expects `{nation:u8}{guildRank:str8}{title:str8}{groupString:str8}{canGroup:bool}{hasGroupBox:bool}{baseClass:u8}{enableMasterAbilityMeta:bool}{enableMasterQuestMeta:bool}{displayClass:str8}{guildName:str8}{legendCount:u8}[...marks]`. The two `bool` meta flags after BaseClass land on Hybrasyl's filler bytes (`0x00 0x00`) — likely parses but as `EnableMasterAbilityMetaData=false`. Trailing Hybrasyl-only bytes (portrait-style block) will be garbage in Chaos. See §4.12. |
| 0x3A | Effect | StatusBar | MATCH | ok | `{icon:u16}{color:u8}{0:u8}`. Hybrasyl writes `{icon:u16}{color:u8}` (2 bytes payload vs Chaos's 3). Chaos reads the icon as `u16`, color as `u8`, done — trailing zero is a write-only artifact, not required for parsing. Compatible. |
| 0x3B | HeartBeat | PingA | MATCH | ok | `{a:u8}{b:u8}`. Hybrasyl sends random bytes; Chaos auto-echoes via HandleHeartBeat. Identical. |
| 0x3C | MapData | MapData | MATCH | ok | `{row:u16}{data...}`. Hybrasyl sends one packet per row (width*6 bytes of byte-swapped map data). Chaos's reader takes `row:u16` then reads the rest as `data`. Identical. |
| 0x3D | — | LevelPoint | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x3E | — | UseSkill | MISSING-CHAOS | — | Hybrasyl has a `UseSkill` packet class (ServerPackets/UseSkill.cs) but the class has **no `Packet()` method / `new UseSkill(...)` is never invoked** anywhere in the codebase. MISSING-HYBRASYL in practice. |
| 0x3F | Cooldown | Cooldown | MATCH | ok | `{pane:u8}{slot:u8}{lengthSecs:u32}`. (`pane` is what Chaos calls `IsSkill:bool`; 0=skill, 1=spell on Hybrasyl whereas Chaos treats any nonzero as skill. Close enough but worth a sanity check.) |
| 0x40 | — | SendPatch | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x42 | DisplayExchange | Exchange | MATCH | ok | `{action:u8}{…}`. Hybrasyl's `ExchangeControl.Packet()` and Chaos's `DisplayExchangeConverter` agree on Initiate / QuantityPrompt / ItemUpdate / GoldUpdate / Cancel / Confirm shapes. Hybrasyl uses `uint` for gold where Chaos uses `i32` — positive values < 2^31 are bit-identical. |
| 0x43 | — | ClickObject | MISSING-CHAOS | — | Client-direction opcode on Hybrasyl. |
| 0x44 | — | AddUser | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x45 | — | ItemShop | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x46 | — | GambleStart | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x47 | — | TotalUsers | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x48 | CancelCasting | CancelCast | MATCH | ok | Chaos writes empty; Hybrasyl writes `{0x00:u8}`. Harmless trailing byte. |
| 0x49 | EditableProfileRequest | RequestPortrait | MATCH | ok | Chaos: `{03 00 00 00 00 00}`. Hybrasyl: `{0x00:u8}{0x00:u8}`. DIVERGENCE — Hybrasyl's version is 6 bytes shorter. Chaos-Server does NOT send 0x49 to request portrait; Chaos.Client treats it as a portrait/profile edit prompt. Likely harmless (client ignores the payload) but flag for review. Tentatively MATCH because semantics (request client profile) are equivalent. |
| 0x4B | ForceClientPacket | Bounce | MISSING-HYBRASYL | — | Hybrasyl defines `Bounce` but no `new ServerPacket(0x4B)` / Bounce class found in server emission — unused. Chaos-Server does emit. |
| 0x4C | ExitResponse | Reconnect | MATCH | ok | Chaos: `{confirm:bool}{00 00}`. Hybrasyl (World.cs:2043): `{0x01:u8}{0x00 0x00}`. Identical. |
| 0x4D | — | Emblem | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x4F | — | PlayerShop | MISSING-CHAOS | — | Hybrasyl `PlayerShop` class exists (`ServerPackets/PlayerShop.cs`) and **is not invoked** — no `new PlayerShop(` outside the file itself. MISSING-HYBRASYL in practice. |
| 0x50 | — | Manufacture | MISSING-CHAOS | client parse / ignore | Hybrasyl sends (`Subsystems/Manufacturing/ManufactureState.cs:86,98`). Chaos.Networking has no opcode entry → raw byte discard on receive. Safe to ignore unless crafting UI is wired up. |
| 0x51 | — | BlockInput | MISSING-CHAOS | client parse / ignore | Sent by `ManufactureCursor.Packet()`. Payload `{complete:bool}`. Chaos.Networking has no entry. |
| 0x53 | — | ServerClose | MISSING-CHAOS | — | Hybrasyl enum only, not emitted. |
| 0x54 | — | UnitedSlot | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x56 | ServerTableResponse | MultiServer | MATCH* | already patched | Payload is zlib-wrapped server table. Both emit `{len:u16}{zlibData}`. Hybrasyl uses a ZlibCompression helper whose Adler32 trailer .NET `ZLibStream` rejects; Chaos.Client strips the 2-byte header and 4-byte trailer before inflate. See §5. |
| 0x57 | — | ServerSelect | MISSING-CHAOS | — | Client-direction opcode on Hybrasyl. |
| 0x58 | MapLoadComplete | MapLoadComplete | MATCH* | already patched | Chaos writes `{0:u8}`; Hybrasyl (ServerPackets/MapLoadComplete.cs) writes `{0:u16}`. Functionally equivalent — Chaos client already synthesizes this event off of MapInfo anyway. See §5. |
| 0x5B | — | Advertisement | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x60 | LoginNotice | Notification | MATCH* | already patched | Short form `{false:bool}{crc:u32}`. Full form `{true:bool}{len:u16}{body}`. Hybrasyl's Login.cs:272 and 327 match the Chaos format exactly. The zlib-wrapped notification bytes hit the same Adler32 issue as 0x56. Client already strips header/trailer before inflate. |
| 0x62 | — | WebBoard | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x63 | DisplayGroupInvite | Group | **DIVERGENT** | client parse / fork | Chaos expects `{type:u8}{sourceName:str8}[if ShowGroupBox: name,note,minLv,maxLv,5×(max,current)…]`. Hybrasyl has multiple subtypes: `Ask` (World.cs:2779): `{type:u8}{name:str8}{0:u8}{0:u8}`, and `RecruitInfo` (UserGroup.cs:321): `{type:u8}{recruiter:str8}{name:str8}{note:str8}{min:u8}{max:u8}{5×(wanted,count)}`. The class-count order is **different** from Chaos (Hybrasyl: warrior, wizard, rogue, priest, monk; Chaos serializer: warriors, wizards, rogues, priests, monks — order matches). Subtype-code values may disagree; see §4.13. |
| 0x64 | — | MiniGame | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x66 | LoginControl | Website | MATCH* | client parse | Chaos: `{controlType:u8}{msg:str8}`. Hybrasyl (Login.cs:336) writes `{0x03:u8}{"http://...":str8}`. Byte-identical shape; `LoginControlsType=3` means "homepage URL" on Hybrasyl, distinct from Chaos's own control-type enum. Parses but semantics differ. |
| 0x67 | MapChangePending | MapChangePending | MISSING-HYBRASYL | already patched | Chaos writes `{03 00 00 00 00 00}`; Hybrasyl never emits. Client synthesizes from 0x15. See §5. |
| 0x68 | SynchronizeTicks | PingB | **SEMANTIC-DIFF** | client parse | Chaos: `{ticks:u32}` sent periodically so the server can compute round-trip time (the *client* echoes back). Hybrasyl (Client.cs:169 `SendTickHeartbeat`): same byte shape (`{tickCount:i32}`) but used as a heartbeat that the client must echo within an idle window or be disconnected. Chaos.Client auto-echoes HeartBeat (0x3B) but not 0x68 — needs to echo 0x68 too for Hybrasyl keepalive. See §4.14. |
| 0x6B | — | Screenshot | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x6D | — | LoverName | MISSING-CHAOS | — | Hybrasyl enum only. |
| 0x6F | MetaData | MetaData | MATCH | ok | All-checksums (`{true:bool}{count:u16}[{name:str8}{crc:u32}]`) and by-name (`{false:bool}{name:str8}{crc:u32}{len:u16}{data}`) both match Chaos exactly (World.cs:3841,3859). |
| 0x7E | AcceptConnection | — | MISSING-HYBRASYL | ok | Only sent by Hybrasyl's Lobby server (Server.cs:163 `{0x1B:u8}"CONNECTED SERVER\n"`). Worldserver never sends. Chaos also only expects it in the Lobby flow. |
| 0x8A | — | QuestInfo | MISSING-CHAOS | — | Hybrasyl enum only. |

(\* "ok" / "already patched" means the client will handle it correctly with current code.)

---

## 4. Divergence details

### 4.1 · 0x04 Location

```text
Chaos-Server (LocationConverter.Serialize)          Hybrasyl (ServerPackets/Location.cs + User.cs:1061)
--------------------------------------------        -------------------------------------------------
ushort X   (big-endian, little for some)            ushort X
ushort Y                                            ushort Y
                                                    ushort 0x000B      // hard-coded
                                                    ushort 0x000B      // hard-coded
--------------------------------------------        -------------------------------------------------
total: 4 bytes                                      total: 8 bytes
```
Impact: Chaos.Client's LocationConverter reads 4 bytes then stops, leaving 4 unread bytes in the packet buffer. If the packet dispatcher trims to declared length (`SpanReader` over the exact payload slice) this is harmless; otherwise four stray bytes leak into the next packet. Safe to add "drain remaining" in a custom client handler.

### 4.2 · 0x05 UserId

```text
Chaos-Server                                        Hybrasyl (User.cs:1497)
------------------                                  --------------------------
u32 Id                                              u32 Id
u8  Direction                                       u8  Direction
u8  0x01       // "any value makes guild list work" u8  0x00       // "unknown, clanid?"
u8  BaseClass                                       u8  BaseClass
u8  0                                               u8  0x00       // unknown
u8  0                                               u8  Gender     // 0=male, 1=female
u8  0                                               u8  0
------------------                                  --------------------------
```
Impact: Chaos's reader discards the last 3 bytes so there is no crash. The important semantic leak is that Hybrasyl packs **Gender** into byte 6 (offset 8 in the payload after Id+Dir+unk+Class+unk), which Chaos.Client currently does not read. To render correct aisling sprites for self, the client must read `args.Gender` from offset `5+1+1+1+1+1 = 10` in the Hybrasyl form. Consider patching `UserIdConverter` or adding a Hybrasyl-specific parse path.

### 4.3 · 0x07 DisplayVisibleEntities

```text
Chaos-Server                                        Hybrasyl (User.cs:1189 / 1366 / 1384)
-----------------------------------                 --------------------------------------
u16 count                                           u16 count (always 1)
[for each entity]                                   u16 X
  u16 X, u16 Y                                      u16 Y
  u32 Id                                            u32 Id
  u16 sprite                                        u16 sprite (+ 0x8000 items / + 0x4000 creatures)
  if item:                                            // items only:
    u8 color                                          u8 color
    u8 0, u8 0                                        u8 0, u8 0, u8 0      // three zeros
  else creature:                                      // creatures only:
    4 bytes unknown                                   u8 0, u8 0, u8 0, u8 0 // four zeros
    u8 direction                                      u8 direction
    u8 0                                              u8 0
    u8 creatureType                                   if merchant: u8 0x02; str8 name
    if merchant: str8 name                          -- packet ends here --
(loop to count)
```
Impact: Two significant divergences. First, Hybrasyl emits one Visible-Entities packet per entity, always with `count=1`, while Chaos batches many into one packet. The count mismatch works (Chaos's reader loops `count` times). Second, for **items** Hybrasyl writes `{sprite+0x8000, color, 0, 0, 0}` — three trailing zeros, not two — which offsets the next entity's coordinates by one byte if `count>1`. Since Hybrasyl always sends `count=1`, this only matters if a custom client tries to batch. For **creatures**, Hybrasyl does *not* write a `creatureType` byte, so Chaos's reader picks up the "always zero" post-direction byte as the creature type (`Normal`), and for Merchants it reads `0x02` which maps to `CreatureType.WalkThrough` on Chaos's enum — off-by-one. Workaround: custom handler that reads the Hybrasyl creature layout.

### 4.4 · 0x0D DisplayPublicMessage / CastLine

```text
Chaos-Server (DisplayPublicMessageConverter)        Hybrasyl (ServerPackets/CastLine.cs)
---------------------------------------------       ----------------------------------------------
u8  PublicMessageType  (Normal=0, Shout=1, etc.)    u8  ChatType
u32 SourceId                                        u32 TargetId
str8 Message                                        u8  LineLength
                                                    raw bytes (no length prefix!)   ← WriteString, not WriteString8
                                                    u8 0, u8 0, u8 0
---------------------------------------------       ----------------------------------------------
```
Hybrasyl *also* uses 0x0D for the legacy chat-over-head form at `Objects/User.cs:457` which writes `{shout:bool}{speaker:u32}{msg:str8}` — this *does* match the Chaos layout (with `shout==true` → `PublicMessageType.Shout=1`, else Normal=0). So the same opcode has two formats on Hybrasyl:
- Chat: Chaos-compatible (fine).
- CastLine: wire-incompatible — Chaos will read `LineLength` as a str8 length byte, then consume `LineLength` bytes as the "message", then be stranded.

Impact: If a user casts a spell, Chaos.Client will try to render the chant text as a chat bubble. The client must either distinguish by length heuristics or server owners must disable CastLine emission. Recommend client-side: decode as both and pick based on context (if a local CastingSystem is active, treat as cast line).

### 4.5 · 0x15 MapInfo

```text
Chaos-Server                                        Hybrasyl (User.cs:1032, MapInfo.cs)
-------------------------------------               -------------------------------------
i16 MapId                                           u16 MapId
u8  Width                                           u8  Width % 256
u8  Height                                          u8  Height % 256
u8  Flags                                           u8  Flags  (Snow=1, Rain=2, Dark=3,
  (1=Snow, 2=Rain, 3=Dark low-nibble)                 NoMap=64, Snow-again=128)
u8  0, u8  0    // unknown                          u8  Width / 256
                                                    u8  Height / 256
u16 CheckSum                                        u8  CheckSum % 256
                                                    u8  CheckSum / 256
str8 Name                                           str8 Name
```
Same byte shape by coincidence for any map under 256 tiles (Hybrasyl's high-bytes of W/H are both zero). For larger maps (rare on retail) the "2 unknown" bytes of Chaos become the high bytes of W/H. Flags bit 128 ("Snow") is redundant with bit 1 but doesn't hurt Chaos client's weather renderer (which reads low nibble). Dark setting writes *both* bits 1 and 2 which, on Chaos's weather renderer, means "Rain" (bit 2), not "Dark". This is a visual divergence — client will render rain on Hybrasyl dark maps. Recommend the client treat bit combinations `1|2` as Dark per Hybrasyl convention (see `docs/re_notes/map_flags.md`).

### 4.6 · 0x1A BodyAnimation / PlayerAnimation

```text
Chaos-Server (BodyAnimationConverter)               Hybrasyl (ServerPackets/PlayerAnimation.cs)
-----------------------------------------           --------------------------------------------
u32 SourceId                                        u32 UserId
u8  BodyAnimation                                   u8  Animation
u16 AnimationSpeed   (UInt16)                       i16 Speed            (Int16, signed)
u8  Sound (0xFF = null)                             u8  0xFF  (always)
-----------------------------------------           --------------------------------------------
```
Impact: The signedness difference is a no-op for animation speeds in 0…32767. Hybrasyl always writes `0xFF` (no sound) — Chaos reads as "null sound". Byte-compatible in practice.

### 4.7 · 0x29 Animation / SpellAnimation

```text
Chaos-Server (AnimationConverter)                   Hybrasyl (ServerPackets/SpellAnimation.cs)
-----------------------------------------           --------------------------------------------
if TargetPoint set (ground):                        u8 0x00                    ← prepended byte
  u32 0                                             if Id != 0 (entity variant):
  u16 TargetAnimation                                 u32 Id
  u16 AnimationSpeed                                  u32 SenderId (or Id if 0)
  u16 X, u16 Y                                        u16 AnimationId
else (entity):                                        u16 SenderAnimationId (or 0)
  u32 TargetId                                        u16 Speed
  u32 SourceId                                        u8 0x00
  u16 TargetAnimation                               else (ground, Id == 0):
  u16 SourceAnimation                                 u32 0
  u16 AnimationSpeed                                  u16 AnimationId
                                                      u16 Speed
                                                      u16 X, u16 Y
-----------------------------------------           --------------------------------------------
```
BUT: inline callers in `Objects/User.cs:3015,3029,3042` skip the `SpellAnimation` class entirely and write raw: `{id:u32}{id:u32}{effect:u16}{0:u16}{speed:i16}{0:u8}` (entity) or `{0:u32}{effect:u16}{speed:i16}{x:i16}{y:i16}` (ground). These **do not** prepend the 0x00.

Impact: Hybrasyl is internally inconsistent. Chaos's reader is keyed on `sourceId == 0` as the ground-vs-entity discriminator. For inline variants this works; for `SpellAnimation.Packet()` callers the leading `0x00` shifts everything by one byte and Chaos reads `targetId` as a truncated value. Recommend: ignore the prefixed 0x00 case (Hybrasyl only uses the class for externally-driven spell effects, which are rarer) or fork the converter.

### 4.8 · 0x2F DisplayMenu / NpcReply

```text
Chaos (DisplayMenuConverter)                        Hybrasyl (MerchantResponse.cs / User.cs:4079)
-----------------------------------------           --------------------------------------------
u8 MenuType                                         u8 MerchantDialogType
u8 EntityType                                       u8 MerchantDialogObjectType
u32 SourceId                                        u32 ObjectId
u8 0                                                u8 0
u16 offsetSprite                                    i16 Tile1 (cast from ushort)
u8 color                                            u8 0          ← always zero color1
u8 0                                                u8 1
u16 offsetSprite                                    i16 Tile1 (same)
u8 color                                            u8 0          ← always zero color2
u8 shouldIllustrate (bool)                          u8 0
str8 Name                                           str8 Name
str16 Text                                          str16 Text
(subtype payload …)                                 (subtype payload …)
```
Menu-subtype codes also diverge — Hybrasyl's `MerchantDialogType` uses values like `Options=0`, `OptionsWithArgument=1`, `Input=2`, `InputWithArgument=3`, `MerchantShopItems=4`, `MerchantSkills=5`, `MerchantSpells=6`, `UserSkillBook=7`, `UserSpellBook=8`, `UserInventoryItems=9`. Chaos's `MenuType` uses `Menu=0`, `MenuWithArgs=1`, `TextEntry=2`, `TextEntryWithArgs=3`, `ShowItems=4`, `ShowPlayerItems=5`, `ShowSpells=6`, `ShowSkills=7`, `ShowPlayerSpells=8`, `ShowPlayerSkills=9`. The mapping is close (0-3 match intent) but 4-9 are shuffled. Recommend: fork the converter or add a Hybrasyl-specific subtype remap.

### 4.9 · 0x31 DisplayBoard / Board

Hybrasyl's `MessagingResponse` emits with a hard-coded type byte layout:
- `DisplayList`: `{0x01:u8}{count+1:u16}{0:u16}{"Mail":str8}[{id:u16}{name:str8}]*` — Chaos expects `BoardList=0` not `0x01`, so the type byte is wrong by one.
- `GetMailboxIndex`: `{0x04:u8}{0x01:u8}{id:u16}{name:str8}{count:u8}[post headers]` — Chaos's `MailBoard` is `0x03`, so `0x04` will be treated as an unknown type → `ArgumentOutOfRangeException`.
- `GetBoardMessage`: `{0x03:u8}{0x00:u8}{highlight:bool}{id:u16}{author:str8}{month:u8}{day:u8}{subject:str8}{body:str16}` — Chaos's `PublicPost=2` expects `{prevBtn:bool}{0:u8}{id:i16}…` — close but the type discriminator is off.

Recommend: convert via a Hybrasyl-specific board handler rather than reusing Chaos's.

### 4.10 · 0x32 Door / UserMoveResponse

```text
Chaos (DoorConverter, post-walk door sync)          Hybrasyl (User.cs:2041 UserMoveResponse)
-----------------------------------------           --------------------------------------------
u8 count                                            u8 0x00                ← ONE byte, no coords
[for each door:]                                    -- end --
  u8 x, u8 y
  u8 closed (bool)
  u8 openRight (bool)
-----------------------------------------           --------------------------------------------

Chaos-style door update is ALSO emitted by Hybrasyl (User.cs:3075 SendDoorUpdate):
  u8 0x01, u8 x, u8 y, u8 closed, u8 openRight    — matches Chaos layout with count=1.
```
Impact: The "move response" variant (single `0x00` byte) crashes Chaos's DoorConverter because `count=0` is valid but also means "list is empty" which *should* parse fine — re-checking: count=0 → no loop → empty door list → OK. So actually this is harmless and Chaos decodes it as "no doors in viewport". Downgrade to MATCH-with-note. Door-update variant is identical.

### 4.11 · 0x34 OtherProfile / Profile

Hybrasyl's `PlayerProfile` (really the 0x34 "other profile" — the name is misleading; 0x34 is the other-user profile packet on both):
```text
Chaos                                               Hybrasyl (User.cs:964)
-----------------------------------------           --------------------------------------------
u32 Id                                              u32 Id
18×{sprite:u16, color:u8}                           18×{sprite:u16, color:u8}  (via Equipment.GetEquipmentDisplayList)
u8 socialStatus                                     u8 GroupStatus
str8 Name                                           str8 Name
u8 nation                                           u8 Nation.Flag
str8 Title                                          str8 ""                    ← always empty
bool groupOpen                                      u8 (Grouping ? 1 : 0)
str8 GuildRank                                      str8 guildInfo.GuildRank
str8 DisplayClass                                   str8 className             ← NOT what Chaos expects
str8 GuildName                                      str8 guildInfo.GuildName
u8 legendCount                                      u8 legendCount
legend marks[icon,color,key,text]…                  legend marks[icon,color,prefix,text]…
u16 portraitLen+textLen+4                           u16 portraitLen+textLen+4
u16 portraitLen                                     u16 portraitLen
bytes portrait                                      bytes portrait
str16 ProfileText                                   str16 ProfileText
u8 0                                                u16 PlayerDisplay
                                                    u8 0x02
                                                    u32 0x00
                                                    u8 0x00
```
Impact: Chaos-extra trailing bytes (PlayerDisplay, 0x02 flag, u32 zero, u8 zero) are not written by Chaos-Server and not expected by Chaos.Client. Hybrasyl writes them instead of the Chaos `{0:u8}` "nfi" trailing byte. Reader won't crash (it reads until it has filled all fields and stops) but any follow-on packet framing that depends on byte offsets will drift. Likely parses fine with the SpanReader model — Chaos only reads what it declares.

### 4.12 · 0x39 SelfProfile

See main matrix row. Two `bool` Chaos fields (`EnableMasterAbilityMetaData`, `EnableMasterQuestMetaData`) land on Hybrasyl's `{0:u8}{0:u8}` filler, which means they'll always read as false — acceptable, just missing features. Trailing Hybrasyl bytes (`{0:u8}{PlayerDisplay:u16}{0x02:u8}{0:u32}{0:u8}`, 9 bytes) are read by Chaos as part of the legend-marks loop if `legendCount > actual count`, or as nothing if the SpanReader is constrained. Needs empirical testing.

### 4.13 · 0x63 DisplayGroupInvite / Group

Hybrasyl subtype values (GroupServerPacketType): `Ask = 0`, `Answer = 1`, `RecruitInit = 2`, `RecruitInfo = 3`. Chaos `ServerGroupSwitch`: `Invite = 1`, `ShowGroupBox = 2`, others. Subtype mismatch — Hybrasyl's "Ask" (0) lands on Chaos's implicit default. The `Invite` body shape on Chaos expects group-box info; Hybrasyl's `Ask` writes just `{name:str8}{0:u8}{0:u8}`.

Recommend: write a Hybrasyl-specific 0x63 handler in Chaos.Client that dispatches by Hybrasyl's subtype codes.

### 4.14 · 0x68 SynchronizeTicks / PingB

Payload is identical bytes (`{ticks:u32/i32}`). Semantics diverge:
- **Chaos.Client** currently auto-echoes 0x3B (HeartBeat) and *does* also echo 0x68 (SynchronizeTicks) — see `GameClient.cs` / `ConnectionManager.cs`. Verify this still happens; if not, Hybrasyl will idle-timeout the connection.
- Hybrasyl's `SendTickHeartbeat` fires every ~ByteHeartbeatInterval seconds (typically 60s) and expects the client to send its own `0x75` / equivalent response. If missing, the client is flagged idle.

No byte-layout change required; just confirm the client echoes.

---

## 5. Already-patched in Chaos.Client

- **0x56 ServerTableResponse** — strips the 2-byte zlib header + 4-byte Adler32 trailer before inflating, because .NET's strict `ZLibStream` rejects Hybrasyl's Adler32.  
  File: `Chaos.Client.Networking/ServerTableData.cs` (lines ~35–55, `// Hybrasyl writes a zlib wrapper …` comment).
- **0x60 LoginNotice** — same zlib workaround, reused from the ServerTableData path.  
  File: `Chaos.Client.Networking/ServerTableData.cs` (shared static helper) and the login-notice parse site in `Chaos.Client.Networking/ConnectionManager.cs`.
- **0x58 MapLoadComplete** — synthesized client-side from the arrival of 0x15 MapInfo; Hybrasyl either doesn't send it (defined class but no emission in the world server flow) or its `{0:u16}` form is functionally equivalent.  
  File: `Chaos.Client.Networking/ConnectionManager.cs` (see `HandleMapLoadComplete` at ~line 1704 and `SynthesizeMapChangePending` at ~1994).
- **0x67 MapChangePending** — synthesized from 0x15 MapInfo; Hybrasyl never emits.  
  File: `Chaos.Client.Networking/ConnectionManager.cs` (lines 1682–1694 — comment: *"Hybrasyl does not send MapChangePending, MapLoadComplete, or MapChangeComplete."*).

---

## 6. Open questions

1. **0x07 DisplayVisibleEntities** — Hybrasyl writes one packet per entity (`count=1`). Chaos's converter loops over `count`. Confirm there's no case in Hybrasyl's `Objects/*.cs` where a batched ADD is emitted with `count>1`; I only found `Merchant.cs:103`, `Reactor.cs:119`, and four callsites in `User.cs`, all with hard-coded `count=1`.
2. **0x29 SpellAnimation** leading 0x00 — Chaos's reader will mis-parse this. Is `SpellAnimation.Packet()` actually called, or is every effect emitted via the raw inline `User.cs` form? ripgrep for `new SpellAnimation` / `new EffectAnimation` returns the class defs; haven't located callsites, suggesting it may be dead code. Confirm.
3. **0x33 DisplayAisling "DisplayAsMonster"** branch — Hybrasyl writes `{helmet:u16}{monsterSprite:u16}...` whereas Chaos keys the monster branch on `headSprite==0xFFFF`. Need to confirm what value Hybrasyl writes to `Helmet` when `DisplayAsMonster` is true; if always 0xFFFF, this aligns.
4. **0x49 EditableProfileRequest / RequestPortrait** — Chaos-Server writes a 6-byte canonical prefix `03 00 00 00 00 00`; Hybrasyl writes `00 00`. Chaos.Client's handler currently treats 0x49 as a pseudo-ACK/prompt; behaviour under the 2-byte variant hasn't been verified.
5. **0x36 WorldList** — Hybrasyl packs `classAndFlags` as raw `(byte)user.Class` without ORing in the `IsGuilded` bit 0x08 — it uses `color` byte `84` as the guild tint instead. Chaos.Client extracts bit 0x08 to render the guild-brace icon. Need to confirm whether color=84 is a reasonable heuristic.
6. **0x2E WorldMap** — Hybrasyl writes an opaque `map.GetBytes()` blob. The exact byte layout is not inspected here; assumed to match the legacy DA world-map format that Chaos.Networking's `WorldMapConverter` also targets. Needs empirical test on a WorldMap portal.
7. **0x50 Manufacture** / **0x51 BlockInput** — not handled by Chaos.Networking. Raw bytes drop-through on receipt. If Hybrasyl invokes the manufacture system (crafting recipes) while a Chaos.Client is connected, the client will silently ignore those packets and the user won't see the recipe window. Expected behaviour for now.
8. **0x4B Bounce (Hybrasyl name)** — Chaos uses 0x4B as `ForceClientPacket`; Hybrasyl's `Bounce` class is not invoked. Safe to assume the client will never receive it from a Hybrasyl server.
