#region
using Chaos.Client.Collections;
using Chaos.Client.Data;
using Chaos.Client.Data.Utilities;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Chaos.Client.Rendering.Models;
using Chaos.Client.Systems;
using Chaos.Client.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Chaos.Geometry.Abstractions.Definitions;
using Chaos.Networking.Entities.Server;
using DALib.Definitions;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen
{
    #region Server Event Handlers
    // --- Entity display / removal ---

    private void HandleDisplayAisling(DisplayAislingArgs args)
    {
        // Update player name in HUD when the player's own aisling is displayed
        if (args.Id == Game.Connection.AislingId)
        {
            UpdateHuds(h => h.SetPlayerName(args.Name));
            WorldList.PlayerName = args.Name;
            Exchange.PlayerName = args.Name;
            UpdateHuds(h => h.SetServerName(Game.Connection.ServerName));
            DataContext.PlayerData.Initialize(args.Name);
            LoadPlayerFamilyList();
            LoadPlayerFriendList();
            LoadPlayerMacros();
            WorldState.ReloadChants();
        }

        // Check for idle animation ("04") frames on this aisling's body
        var entity = WorldState.GetEntity(args.Id);

        if (entity?.Appearance is { } appearance)
        {
            entity.IdleAnimFrameCount = Game.AislingRenderer.GetIdleAnimFrameCount(in appearance);

            // Start idle cycling if entity is currently idle
            if (entity.AnimState == EntityAnimState.Idle)
                AnimationSystem.ResetToIdle(entity);
        }
    }

    private void HandleRemoveEntity(uint id)
    {
        // Capture creature sprite for death dissolve before removing from WorldState
        var entity = WorldState.GetEntity(id);

        if (entity is { Type: ClientEntityType.Creature })
            CreateDyingEffect(entity);

        // Clean up aisling composited texture cache
        if (AislingCache.Remove(id, out var removed))
            removed.Texture?.Dispose();

        // Clean up all overlay caches (name tag, chat bubble, health bar, chant)
        Overlays.RemoveEntity(id);

        // Clean up cached debug label texture
        DebugRenderer.RemoveEntity(id);

        // Remove entity from WorldState (ChaosGame skips removal when WorldScreen is active)
        WorldState.RemoveEntity(id);
    }

    private void CreateDyingEffect(WorldEntity entity)
    {
        var creatureRenderer = Game.CreatureRenderer;
        var animInfo = creatureRenderer.GetAnimInfo(entity.SpriteId);

        if (animInfo is null)
            return;

        var info = animInfo.Value;
        (var frameIndex, var flip) = AnimationSystem.GetCreatureFrame(entity, in info);

        var spriteFrame = creatureRenderer.GetFrame(entity.SpriteId, frameIndex);

        if (spriteFrame is null)
            return;

        var frame = spriteFrame.Value;

        var dyingEffect = new DyingEffect(
            Device,
            frame.Texture,
            entity.TileX,
            entity.TileY,
            frame.CenterX,
            frame.CenterY,
            frame.Left,
            frame.Top,
            flip);

        WorldState.DyingEffects.Add(dyingEffect);
    }

    // --- Movement ---

    /// <summary>
    ///     Client-side prediction: sends Walk packet and immediately starts the walk animation locally without waiting for
    ///     server confirmation. The server response reconciles position if needed.
    /// </summary>
    private void PredictAndWalk(WorldEntity player, Direction direction)
    {
        // Bounds check — don't walk off the map edge
        (var dx, var dy) = direction.ToTileOffset();
        var newX = player.TileX + dx;
        var newY = player.TileY + dy;

        if (MapFile is null || (newX < 0) || (newY < 0) || (newX >= MapFile.Width) || (newY >= MapFile.Height))
            return;

        // Swimming check — if on a water tile and don't have the "swimming" skill, block movement
        if (player.IsOnSwimmingTile && !IsGameMaster && !WorldState.SkillBook.HasSkillByName("swimming"))
        {
            WorldState.Chat.AddMessage("You need to learn how to swim.", Color.White);

            return;
        }

        // Collision check — GM bypasses all collision
        if (!IsGameMaster && !IsTilePassable(newX, newY))
            return;

        Game.Connection.Walk(direction);

        // Record prediction for server reconciliation
        PendingWalks.Enqueue(
            new PendingWalk(
                player.TileX,
                player.TileY,
                newX,
                newY,
                direction));

        // Predict position locally
        player.TileX = newX;
        player.TileY = newY;
        WorldState.MarkSortDirty();

        var walkFrames = player.UsesCreatureWalkTiming ? Game.CreatureRenderer.GetWalkFrameCount(player.SpriteId) : null;

        AnimationSystem.StartWalk(
            player,
            direction,
            player.UsesCreatureWalkTiming,
            true,
            walkFrames);
        UpdateHuds(h => h.SetCoords(player.TileX, player.TileY));
    }

    private void HandleClientWalkResponse(Direction direction, int oldX, int oldY)
    {
        var player = WorldState.GetPlayerEntity();

        if (player is null)
            return;

        (var dx, var dy) = direction.ToTileOffset();
        var serverX = oldX + dx;
        var serverY = oldY + dy;

        // Check if the server confirmation matches our oldest pending prediction
        if (PendingWalks.TryPeek(out var pending) && (pending.FromX == oldX) && (pending.FromY == oldY) && (pending.Direction == direction))
        {
            // Prediction confirmed — discard it
            PendingWalks.Dequeue();

            return;
        }

        // Mismatch — server sent a walk we didn't predict. Clear all pending predictions,
        // rubberband to the server's source position, and play a walk to the destination.
        PendingWalks.Clear();
        QueuedWalkDirection = null;

        player.TileX = serverX;
        player.TileY = serverY;
        WorldState.MarkSortDirty();

        var walkFrames = player.UsesCreatureWalkTiming ? Game.CreatureRenderer.GetWalkFrameCount(player.SpriteId) : null;

        AnimationSystem.StartWalk(
            player,
            direction,
            player.UsesCreatureWalkTiming,
            true,
            walkFrames);

        UpdateHuds(h => h.SetCoords(serverX, serverY));
        Pathfinding.Clear();
    }

    // --- Attributes ---

    private void HandleAttributes(AttributesArgs args)
        => IsGameMaster = args.StatUpdateType.HasFlag(StatUpdateType.GameMasterA)
                          || args.StatUpdateType.HasFlag(StatUpdateType.GameMasterB);

    // --- Chat / messages ---

    private void HandleDisplayPublicMessage(DisplayPublicMessageArgs args)
    {
        var entityExists = WorldState.GetEntity(args.SourceId) is not null;

        if (args.PublicMessageType == PublicMessageType.Chant)
        {
            if (entityExists)
                Overlays.AddChantOverlay(args.SourceId, args.Message);

            return;
        }

        var color = args.PublicMessageType switch
        {
            PublicMessageType.Shout => Color.Yellow,
            _                       => Color.White
        };

        WorldState.Chat.AddMessage(args.Message, color);

        if (!entityExists)
            return;

        var isShout = args.PublicMessageType == PublicMessageType.Shout;
        Overlays.AddChatBubble(args.SourceId, args.Message, isShout);
    }

    private void HandleServerMessage(ServerMessageArgs args)
    {
        switch (args.ServerMessageType)
        {
            case ServerMessageType.Whisper:
                WorldState.Chat.AddMessage(args.Message, new Color(100, 149, 237));

                break;

            case ServerMessageType.GroupChat:
                WorldState.Chat.AddMessage(args.Message, new Color(154, 205, 50));

                break;

            case ServerMessageType.GuildChat:
                WorldState.Chat.AddMessage(args.Message, new Color(128, 128, 0));

                break;

            case ServerMessageType.OrangeBar1
                 or ServerMessageType.OrangeBar2
                 or ServerMessageType.ActiveMessage
                 or ServerMessageType.OrangeBar3
                 or ServerMessageType.AdminMessage
                 or ServerMessageType.OrangeBar5:
                WorldState.Chat.AddOrangeBarMessage(args.Message);

                break;

            case ServerMessageType.PersistentMessage:
                UpdateHuds(h => h.ShowPersistentMessage(args.Message));

                break;

            case ServerMessageType.ScrollWindow:
                TextPopup.Show(args.Message);

                break;

            case ServerMessageType.NonScrollWindow:
                TextPopup.Show(args.Message, PopupStyle.NonScroll);

                break;

            case ServerMessageType.WoodenBoard:
                TextPopup.Show(args.Message, PopupStyle.Wooden);

                break;

            case ServerMessageType.UserOptions:
                ParseUserOptions(args.Message);

                break;

            case ServerMessageType.ClosePopup:
                TextPopup.Hide();

                break;

            default:
                WorldState.Chat.AddOrangeBarMessage(args.Message);

                break;
        }
    }

    /// <summary>
    ///     Parses the server's UserOptions response. Two formats:
    ///     Full request: "0{desc}:{state}\t{desc}:{state}\t..." — '0' prefix, digits stripped, options ordered by position.
    ///     Single toggle: "{digit}{desc}:{state}" — leading digit identifies the option (1-based).
    /// </summary>
    private void ParseUserOptions(string message)
    {
        if (message.Length < 2)
            return;

        // Single option toggle response: "{digit}{description,-25}:{ON/OFF,-3}"
        if (message[0] != '0')
        {
            if (!char.IsDigit(message[0]))
                return;

            var optionIndex = message[0] - '1';

            if (optionIndex is < 0 or >= UserOptions.SETTING_COUNT)
                return;

            ParseOptionEntry(optionIndex, message[1..]);

            return;
        }

        // Full request response: "0{opt1_desc}:{state}\t{opt2_desc}:{state}\t..."
        // Leading '0' prefix, then 8 options in order with digits stripped
        var entries = message[1..]
            .Split('\t', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; (i < entries.Length) && (i < 8); i++)
            ParseOptionEntry(i, entries[i]);
    }

    /// <summary>
    ///     Parses a single option entry in the format "{description,-25}:{ON/OFF,-3}" and applies it.
    /// </summary>
    private void ParseOptionEntry(int optionIndex, string entry)
    {
        if (!UserOptions.IsServerSetting(optionIndex))
            return;

        var colonIdx = entry.LastIndexOf(':');

        if (colonIdx < 1)
            return;

        var stateStr = entry[(colonIdx + 1)..]
            .Trim();
        var isOn = stateStr.StartsWithI("ON");

        // Server settings: use the full formatted text as the display name (includes :ON/:OFF)
        SettingsDialog.SetSettingName(optionIndex, entry.TrimEnd());
        WorldState.UserOptions.SetValue(optionIndex, isOn);
    }

    // --- NPC dialog / menu ---

    private void HandleDialogChanged()
    {
        var dialog = WorldState.NpcInteraction.CurrentDialog;

        Console.WriteLine($"[Dialog] Type={dialog?.DialogType}");

        if (dialog is null || (dialog.DialogType == DialogType.CloseDialog))
        {
            NpcSession.HideAll();

            return;
        }

        NpcSession.ShowDialog(dialog);
        RenderNpcSessionPortrait();
    }

    private void HandleMenuChanged()
    {
        var menu = WorldState.NpcInteraction.CurrentMenu;

        Console.WriteLine($"[Menu] Type={menu?.MenuType}");

        if (menu is null)
            return;

        NpcSession.ShowMenu(menu, Game.Connection);
        RenderNpcSessionPortrait();
    }

    private void RenderNpcSessionPortrait()
    {
        if (!NpcSession.ShouldIllustrate)
        {
            NpcSession.SetPortrait(null, false);

            return;
        }

        // Phase 1: Try full-art illustration SPF from NPCIllust metadata + npcbase.dat
        if (!string.IsNullOrEmpty(NpcSession.NpcName))
        {
            var illustTexture = TryLoadNpcIllustration(NpcSession.NpcName);

            if (illustTexture is not null)
            {
                NpcSession.SetPortrait(illustTexture, true);

                return;
            }
        }

        // Phase 2: Fall back to entity sprite portrait based on EntityType
        if (NpcSession.PortraitSpriteId == 0)
        {
            NpcSession.SetPortrait(null, false);

            return;
        }

        switch (NpcSession.SourceEntityType)
        {
            case EntityType.Creature:
            {
                var frame = RenderCreaturePortrait(NpcSession.PortraitSpriteId);

                if (frame is not null)
                    NpcSession.SetPortrait(frame.Value);
                else
                    NpcSession.SetPortrait(null, false);

                break;
            }

            case EntityType.Item:
            {
                var sprite = Game.ItemRenderer.GetSprite(NpcSession.PortraitSpriteId, (byte)NpcSession.PortraitColor);
                NpcSession.SetPortrait(sprite?.Texture, false);

                break;
            }

            default:
                NpcSession.SetPortrait(null, false);

                break;
        }
    }

    /// <summary>
    ///     Attempts to load a full-art NPC illustration SPF from npcbase.dat via the NPCIllust metadata mapping.
    /// </summary>
    private static Texture2D? TryLoadNpcIllustration(string npcName)
    {
        var illustMeta = DataContext.MetaFiles.GetNpcIllustrationMetadata();

        if ((illustMeta?.Illustrations.TryGetValue(npcName, out var spfFileName) != true) || spfFileName is null)
            return null;

        if (!DatArchives.Npcbase.TryGetValue(spfFileName, out var entry))
            return null;

        var spf = SpfFile.FromEntry(entry);

        if (spf.Count == 0)
            return null;

        var frame = spf[0];

        using var image = (spf.Format == SpfFormatType.Palettized) && spf.PrimaryColors is not null
            ? Graphics.RenderImage(frame, spf.PrimaryColors)
            : Graphics.RenderImage(frame);

        return TextureConverter.ToTexture2D(image);
    }

    private SpriteFrame? RenderCreaturePortrait(ushort spriteId)
    {
        var info = Game.CreatureRenderer.GetAnimInfo(spriteId);

        if (info is null)
            return null;

        (var frameIndex, _) = AnimationSystem.GetCreatureIdleFrame(info.Value, Direction.Down);

        return Game.CreatureRenderer.GetFrame(spriteId, frameIndex);
    }

    private void HandleRefreshResponse()
        =>

            // Server acknowledged the refresh request — re-center camera
            FollowPlayerCamera();

    // --- Exchange ---

    private void HandleExchangeAmountRequested(byte fromSlot)
    {
        ExchangeAmountSlot = fromSlot;
        GoldDrop.ShowForTarget(Exchange.OtherUserId, 0, 0);
    }

    // --- Board / mail ---

    private void HandleBoardListReceived()
    {
        var boards = WorldState.Board.AvailableBoards;

        if (boards is null or { Count: 0 })
            return;

        WorldState.Board.OpenSession();

        BoardList.ShowBoards(
            boards.Select(b => (b.BoardId, b.Name))
                  .ToList());
    }

    private void HandleBoardPostListChanged()
    {
        var board = WorldState.Board;
        var posts = board.Posts.ToList();

        if (board.IsPublicBoard)
        {
            if (LoadingMoreBoardPosts && ArticleList.Visible && (ArticleList.BoardId == board.BoardId))
                ArticleList.AppendEntries(posts);
            else
            {
                HideAllBoardControls();
                ArticleList.ShowArticles(board.BoardId, posts);
                ArticleList.SetHighlightEnabled(IsGameMaster);
            }
        } else
        {
            if (LoadingMoreBoardPosts && MailList.Visible && (MailList.BoardId == board.BoardId))
                MailList.AppendEntries(posts);
            else
            {
                HideAllBoardControls();
                MailList.ShowMailList(board.BoardId, posts);
            }
        }

        LoadingMoreBoardPosts = false;
    }

    private void HandleBoardPostViewed()
    {
        var post = WorldState.Board.CurrentPost;

        if (post is not { } p)
            return;

        var board = WorldState.Board;

        HideAllBoardControls();

        if (board.IsPublicBoard)
        {
            ArticleRead.BoardId = board.BoardId;

            ArticleRead.ShowArticle(
                p.PostId,
                p.Author,
                p.MonthOfYear,
                p.DayOfMonth,
                p.Subject,
                p.Message,
                board.EnablePrevButton);
        } else
        {
            MailRead.BoardId = board.BoardId;

            MailRead.ShowMail(
                p.PostId,
                p.Author,
                p.MonthOfYear,
                p.DayOfMonth,
                p.Subject,
                p.Message,
                board.EnablePrevButton);
        }
    }

    // --- Group ---

    private void HandleGroupInviteReceived()
    {
        var invite = WorldState.GroupInvite.Current;

        if (invite is null)
            return;

        var sourceName = invite.SourceName;

        switch (invite.ServerGroupSwitch)
        {
            case ServerGroupSwitch.Invite:
            {
                WorldState.Chat.AddOrangeBarMessage($"{sourceName} invites you to join a group.");

                var vp = WorldHud.ViewportBounds;
                var menuX = vp.X + vp.Width / 2;
                var menuY = vp.Y + vp.Height / 2;

                ContextMenu.Show(
                    menuX,
                    menuY,
                    ($"Accept {sourceName}'s invite", () => Game.Connection.SendGroupInvite(ClientGroupSwitch.AcceptInvite, sourceName)),
                    ("Decline", () => { }));

                break;
            }

            case ServerGroupSwitch.RequestToJoin:
            {
                WorldState.Chat.AddOrangeBarMessage($"{sourceName} wants to join your group.");

                var vp = WorldHud.ViewportBounds;
                var menuX = vp.X + vp.Width / 2;
                var menuY = vp.Y + vp.Height / 2;

                ContextMenu.Show(
                    menuX,
                    menuY,
                    ($"Accept {sourceName}", () => Game.Connection.SendGroupInvite(ClientGroupSwitch.AcceptInvite, sourceName)),
                    ("Decline", () => { }));

                break;
            }

            case ServerGroupSwitch.ShowGroupBox:
            {
                WorldState.Chat.AddOrangeBarMessage($"{sourceName} has an open group box.");

                break;
            }
        }
    }

    // --- Profiles ---

    private void HandleEditableProfileRequest() => Game.Connection.SendEditableProfile(PlayerPortrait, StatusBook.GetProfileText());

    private static byte[] LoadPortraitFile(string name)
    {
        if (string.IsNullOrEmpty(name))
            return [];

        var jpgPath = Path.Combine(GlobalSettings.DataPath, $"{name}.jpg");

        if (File.Exists(jpgPath))
            return File.ReadAllBytes(jpgPath);

        var noExtPath = Path.Combine(GlobalSettings.DataPath, name);

        if (File.Exists(noExtPath))
            return File.ReadAllBytes(noExtPath);

        return [];
    }

    private static string LoadProfileText(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var profilePath = Path.Combine(GlobalSettings.DataPath, $"{name}.txt");

        return File.Exists(profilePath) ? File.ReadAllText(profilePath) : string.Empty;
    }

    private void SaveProfileText(string text)
    {
        var name = Game.Connection.AislingName;

        if (string.IsNullOrEmpty(name))
            return;

        var profilePath = Path.Combine(GlobalSettings.DataPath, $"{name}.txt");
        File.WriteAllText(profilePath, text);
    }

    private void HandleSelfProfile(SelfProfileArgs args)
    {
        // Social status display
        var status = SocialStatusPicker.CurrentStatus;
        StatusBook.SetEmoticonState((byte)status, status.ToString());

        // Populate and show the status book
        StatusBook.SetPlayerInfo(
            WorldHud.PlayerName,
            args.DisplayClass,
            args.GuildName ?? string.Empty,
            args.GuildRank ?? string.Empty,
            args.Title ?? string.Empty);

        // Legend marks
        var marks = args.LegendMarks
                        .Select(m => new LegendMarkEntry(
                            m.Text,
                            MapMarkColor(m.Color),
                            (byte)m.Icon,
                            m.Key))
                        .ToList();

        StatusBook.SetLegendMarks(marks);

        // Ability metadata (skills/spells from SClass file)
        var abilityMetadata = DataContext.MetaFiles.GetAbilityMetadata((byte)args.BaseClass);

        if (abilityMetadata is not null)
            StatusBook.SetAbilityMetadata(abilityMetadata);
        else
            StatusBook.ClearSkills();

        // Event metadata (quests from SEvent files)
        var eventMetadata = DataContext.MetaFiles.GetEventMetadata();

        if (eventMetadata.Count > 0)
        {
            // Build a set of completed event IDs from legend marks for O(1) lookup
            var completedEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mark in args.LegendMarks)
                completedEventIds.Add(mark.Key);

            StatusBook.SetEvents(
                eventMetadata,
                completedEventIds,
                args.BaseClass,
                args.EnableMasterQuestMetaData);
        } else
            StatusBook.ClearEvents();

        // Family info
        StatusBook.SetFamilyInfo(args.Name, args.SpouseName ?? string.Empty);
        LoadPlayerFamilyList();

        // Paperdoll — render the player's full aisling at south-facing idle
        var playerEntity = WorldState.GetPlayerEntity();

        if (playerEntity?.Appearance is { } appearance)
            StatusBook.SetPaperdoll(Game.AislingRenderer, in appearance);

        // Group open state — server is source of truth, sync all UI
        StatusBook.SetGroupOpen(args.GroupOpen);
        WorldState.UserOptions.SetValue(12, args.GroupOpen);

        // Group members — parse GroupString into state, UI subscribes via event
        if (!string.IsNullOrEmpty(args.GroupString))
        {
            if (args.GroupString.StartsWithI(GROUP_MEMBERS_PREFIX))
                WorldState.Group.ParseAndSet(args.GroupString);
            else if (args.GroupString.StartsWithI(SPOUSE_PREFIX))
            {
                var spouseName = args.GroupString[SPOUSE_PREFIX.Length..]
                                     .Trim();
                StatusBook.SetFamilyInfo(args.Name, spouseName);
                WorldState.Group.Clear();
            } else
                WorldState.Group.Clear();
        } else
            WorldState.Group.Clear();

        if (GroupHighlightRequested)
        {
            GroupHighlightRequested = false;
            ApplyGroupHighlight();
        } else if (SelfProfileRequested)
        {
            SelfProfileRequested = false;
            ShowStatusBook();
        }
    }

    private void ApplyGroupHighlight()
    {
        GroupHighlightedIds.Clear();
        ClearGroupTintCache();

        var members = WorldState.Group.Members;

        if (members.Count == 0)
            return;

        var memberSet = new HashSet<string>(members, StringComparer.OrdinalIgnoreCase);

        foreach (var entity in WorldState.GetSortedEntities())
        {
            if (entity.Type != ClientEntityType.Aisling)
                continue;

            if (!string.IsNullOrEmpty(entity.Name) && memberSet.Contains(entity.Name))
                GroupHighlightedIds.Add(entity.Id);
        }

        if (GroupHighlightedIds.Count > 0)
            GroupHighlightTimer = 1000f;
    }

    private void ShowStatusBook()
    {
        StatusBook.RefreshEquipment();

        if (WorldState.Attributes.Current is { } attrs)
            StatusBook.UpdateEquipmentStats(
                attrs.Str,
                attrs.Int,
                attrs.Wis,
                attrs.Con,
                attrs.Dex,
                attrs.Ac);

        StatusBook.SwitchTab(StatusBookTab.Equipment);
        StatusBook.Show();
    }

    private void HandleOtherProfile(OtherProfileArgs args)
    {
        var marks = args.LegendMarks
                        .Select(m => new LegendMarkEntry(
                            m.Text,
                            MapMarkColor(m.Color),
                            (byte)m.Icon,
                            m.Key))
                        .ToList();

        OtherProfile.Show(args, marks, Game.AislingRenderer);
    }

    // --- Animations / effects / sound ---

    private void HandleBodyAnimation(BodyAnimationArgs args)
    {
        var entity = WorldState.GetEntity(args.SourceId);

        if (entity is null)
            return;

        // Emotes are body animations — ignore if any body anim or emote overlay is already playing
        if ((entity.AnimState == EntityAnimState.BodyAnim) || (entity.ActiveEmoteFrame >= 0))
            return;

        // Creatures use their MpfFile attack frame counts; aislings use EPF suffix-based frame counts
        if (entity.Type == ClientEntityType.Creature)
        {
            var animInfo = Game.CreatureRenderer.GetAnimInfo(entity.SpriteId);

            if (animInfo is { } info)
                AnimationSystem.StartCreatureBodyAnimation(
                    entity,
                    args.BodyAnimation,
                    args.AnimationSpeed,
                    in info);
        } else
        {
            (_, var framesPerDir, _, _) = AnimationSystem.ResolveBodyAnimParams(args.BodyAnimation);

            if (framesPerDir > 0)
            {
                if (entity.Appearance.HasValue && !Game.AislingRenderer.HasArmorAnimation(entity.Appearance.Value, args.BodyAnimation))
                    return;

                AnimationSystem.StartBodyAnimation(entity, args.BodyAnimation, args.AnimationSpeed);
            } else if (DataUtilities.IsEmote(args.BodyAnimation))
            {
                // Emote overlay — face/bubble icon composited into the aisling sprite
                (var startFrame, var frameCount, var durationMs) = AnimationSystem.ResolveEmoteFrames(args.BodyAnimation);

                if (startFrame >= 0)
                {
                    entity.EmoteStartFrame = startFrame;
                    entity.EmoteFrameCount = frameCount;
                    entity.ActiveEmoteFrame = startFrame;
                    entity.EmoteDurationMs = durationMs;
                    entity.EmoteElapsedMs = 0;
                    entity.EmoteRemainingMs = durationMs;
                }
            }
        }

        if (args.Sound.HasValue)
            Game.SoundSystem.PlaySound(args.Sound.Value);
    }

    private void HandleAnimation(AnimationArgs args)
    {
        // Ground-targeted effect
        if (args is { TargetPoint: not null, TargetAnimation: > 0 })
            CreateEffect(
                args.TargetAnimation,
                args.AnimationSpeed,
                targetTileX: args.TargetPoint.Value.X,
                targetTileY: args.TargetPoint.Value.Y);

        // Entity-targeted effect on target
        if (args is { TargetId: > 0, TargetAnimation: > 0 })
            CreateEffect(args.TargetAnimation, args.AnimationSpeed, args.TargetId.Value);

        // Source-side effect (caster visual)
        if (args is { SourceId: > 0, SourceAnimation: > 0 })
            CreateEffect(args.SourceAnimation, args.AnimationSpeed, args.SourceId.Value);
    }

    private void CreateEffect(
        int effectId,
        ushort animationSpeed,
        uint? targetEntityId = null,
        int? targetTileX = null,
        int? targetTileY = null)
    {
        var info = Game.EffectRenderer.GetEffectInfo(effectId);

        if (info is null)
            return;

        (var frameCount, var fileIntervalMs, var isEfa, var blendMode) = info.Value;

        // EFA effects use the interval from the file; EPF effects use the packet's animation speed
        float frameIntervalMs = isEfa
            ? fileIntervalMs > 0 ? fileIntervalMs : 50
            : animationSpeed > 0
                ? animationSpeed
                : 50;

        // Cancel any existing effect on the same entity — only one effect per entity at a time
        if (targetEntityId.HasValue)
            WorldState.ActiveEffects.RemoveAll(e => e.TargetEntityId == targetEntityId);

        WorldState.ActiveEffects.Add(
            new ActiveEffect
            {
                EffectId = effectId,
                TargetEntityId = targetEntityId,
                TileX = targetTileX,
                TileY = targetTileY,
                FrameCount = frameCount,
                FrameIntervalMs = frameIntervalMs,
                BlendMode = blendMode
            });
    }

    private void HandleSound(SoundArgs args)
    {
        if (args.IsMusic)
            Game.SoundSystem.PlayMusic(args.Sound);
        else
            Game.SoundSystem.PlaySound(args.Sound);
    }

    // --- World / map / doors ---

    private void HandleWorldMap(WorldMapArgs args) => WorldMap.Show(args);

    private void HandleDoor(DoorArgs args)
    {
        if (MapFile is null)
            return;

        foreach (var door in args.Doors)
        {
            if ((door.X < 0) || (door.X >= MapFile.Width) || (door.Y < 0) || (door.Y >= MapFile.Height))
                continue;

            var tile = MapFile.Tiles[door.X, door.Y];

            if (door.Closed)
            {
                // Restore closed tile: find the open tile currently set and swap it back
                var closedLeft = DoorTileTable.GetClosedTileId(tile.LeftForeground);
                var closedRight = DoorTileTable.GetClosedTileId(tile.RightForeground);

                if (closedLeft.HasValue)
                    tile.LeftForeground = closedLeft.Value;

                if (closedRight.HasValue)
                    tile.RightForeground = closedRight.Value;
            } else
            {
                // Open door: find the closed tile and swap to open
                var openLeft = DoorTileTable.GetOpenTileId(tile.LeftForeground);
                var openRight = DoorTileTable.GetOpenTileId(tile.RightForeground);

                if (openLeft.HasValue)
                    tile.LeftForeground = openLeft.Value;

                if (openRight.HasValue)
                    tile.RightForeground = openRight.Value;
            }
        }
    }

    private void HandleMapChangePending()
    {
        MapPreloaded = false;
        QueuedWalkDirection = null;
        Pathfinding.Clear();
        WorldMap.HideMap();
    }

    // --- Health / effects / light ---

    private void HandleEffect(EffectArgs args) => WorldHud.EffectBar.SetEffect(args.EffectIcon, args.EffectColor);

    private void HandleHealthBar(HealthBarArgs args)
    {
        Overlays.AddOrResetHealthBar(args.SourceId, args.HealthPercent);

        if (args.Sound.HasValue)
            Game.SoundSystem.PlaySound(args.Sound.Value);
    }

    private void HandleLightLevel(LightLevelArgs args) => DarknessRenderer.OnLightLevel(args);

    private void HandleMetaDataSyncComplete()
    {
        DarknessRenderer.ReloadMetadata();
        DataContext.MetaFiles.BuildItemIndex();
    }

    // --- Notepad ---

    private void HandleDisplayReadonlyNotepad(DisplayReadonlyNotepadArgs args)
        => Notepad.ShowReadonly(args.Width, args.Height, args.Message);

    private void HandleDisplayEditableNotepad(DisplayEditableNotepadArgs args)
        => Notepad.ShowEditable(
            args.Slot,
            args.Width,
            args.Height,
            args.Message);

    // --- Exit / state ---

    private void HandleExitResponse(ExitResponseArgs args)
    {
        // Server confirmed exit — send the actual logout (isRequest=false triggers server-side redirect to login)
        if (args.ExitConfirmed)
            Game.Connection.RequestExit(false);
    }

    private void HandleStateChanged(ConnectionState oldState, ConnectionState newState)
    {
        // Server redirected us back to login (e.g., after logout)
        // State transitions go World → Connecting → Login, so just check for Login arrival
        if (newState == ConnectionState.Login)
        {
            RedirectInProgress = false;
            PendingLoginSwitch = true;

            return;
        }

        // Unexpected disconnect — show reconnect prompt (skip if this is part of a redirect sequence)
        if ((newState == ConnectionState.Disconnected) && !RedirectInProgress)
            DisconnectPopup.Show("Connection lost.");
    }

    // --- Helpers ---

    private static Color MapMarkColor(MarkColor color)
        => color switch
        {
            MarkColor.White       => Color.White,
            MarkColor.LightOrange => new Color(255, 200, 100),
            MarkColor.LightYellow => new Color(255, 255, 150),
            MarkColor.Yellow      => Color.Yellow,
            MarkColor.LightGreen  => new Color(150, 255, 150),
            MarkColor.Blue        => new Color(100, 149, 237),
            MarkColor.Cyan        => new Color(0, 200, 200),
            MarkColor.LightPink   => new Color(255, 150, 200),
            MarkColor.DarkPurple  => new Color(150, 100, 200),
            MarkColor.Pink        => new Color(255, 182, 193),
            MarkColor.Red         => Color.Red,
            MarkColor.Orange      => Color.Orange,
            MarkColor.Green       => new Color(100, 255, 100),
            MarkColor.Brown       => new Color(180, 120, 60),
            _                     => Color.White
        };
    #endregion
}