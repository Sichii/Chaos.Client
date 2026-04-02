#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.Hud;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Controls.World.Popups.Options;
using Chaos.Client.Extensions;
using Chaos.Client.Systems;
using Chaos.DarkAges.Definitions;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen
{
    #region Exchange Wiring
    private void WireExchange()
    {
        Exchange.OnOk += () => Game.Connection.SendExchangeInteraction(ExchangeRequestType.Accept, Exchange.OtherUserId);

        Exchange.OnCancel += () =>
        {
            Game.Connection.SendExchangeInteraction(ExchangeRequestType.Cancel, Exchange.OtherUserId);
            WorldState.Exchange.Close();
        };
    }
    #endregion

    #region NPC Session Wiring
    private void WireNpcSession()
    {
        // Close/Escape just hides the UI — no response sent to the server.
        // NpcSessionControl already calls HideAll() before firing OnClose.

        NpcSession.OnTop += () =>
        {
            if (NpcSession.SourceId is { } sourceId)
                Game.Connection.ClickEntity(sourceId);
        };

        NpcSession.OnNext += () =>
        {
            if (NpcSession.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcSession.SourceEntityType,
                    sourceId,
                    NpcSession.PursuitId,
                    (ushort)(NpcSession.DialogId + 1));
        };

        NpcSession.OnPrevious += () =>
        {
            if (NpcSession.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcSession.SourceEntityType,
                    sourceId,
                    NpcSession.PursuitId,
                    (ushort)(NpcSession.DialogId - 1));
        };

        NpcSession.OnOptionSelected += optionIndex =>
        {
            if (NpcSession.SourceId is not { } sourceId)
                return;

            if (NpcSession.IsDialogOpcode)
                Game.Connection.SendDialogResponse(
                    NpcSession.SourceEntityType,
                    sourceId,
                    NpcSession.PursuitId,
                    (ushort)(NpcSession.DialogId + 1),
                    DialogArgsType.MenuResponse,
                    (byte)(optionIndex + 1));
            else
            {
                var pursuitId = NpcSession.GetOptionPursuitId(optionIndex);

                if (NpcSession.MenuArgs is not null)
                    Game.Connection.SendMenuResponse(
                        NpcSession.SourceEntityType,
                        sourceId,
                        pursuitId,
                        args: [NpcSession.MenuArgs]);
                else
                    Game.Connection.SendMenuResponse(NpcSession.SourceEntityType, sourceId, pursuitId);
            }
        };

        NpcSession.OnTextSubmit += text =>
        {
            if (NpcSession.SourceId is not { } sourceId)
                return;

            if (NpcSession.IsDialogOpcode)
            {
                // Speak: broadcast the combined prompt + input + epilog as a public Say first
                if (NpcSession.CurrentDialogType is DialogType.Speak)
                {
                    var sayParts = new[]
                    {
                        NpcSession.SpeakPrompt,
                        text,
                        NpcSession.SpeakEpilog
                    };

                    var sayText = string.Join(" ", sayParts.Where(s => !string.IsNullOrEmpty(s)));

                    Game.Connection.SendPublicMessage(sayText);
                }

                Game.Connection.SendDialogResponse(
                    NpcSession.SourceEntityType,
                    sourceId,
                    NpcSession.PursuitId,
                    (ushort)(NpcSession.DialogId + 1),
                    DialogArgsType.TextResponse,
                    args: [text]);
            } else
            {
                // Include previous args for TextEntryWithArgs
                var prevArgs = NpcSession.GetMenuTextPreviousArgs();

                if (prevArgs is not null)
                    Game.Connection.SendMenuResponse(
                        NpcSession.SourceEntityType,
                        sourceId,
                        NpcSession.PursuitId,
                        args:
                        [
                            prevArgs,
                            text
                        ]);
                else
                    Game.Connection.SendMenuResponse(
                        NpcSession.SourceEntityType,
                        sourceId,
                        NpcSession.PursuitId,
                        args: [text]);
            }
        };

        NpcSession.OnProtectedSubmit += (id, password) =>
        {
            if (NpcSession.SourceId is not { } sourceId)
                return;

            Game.Connection.SendDialogResponse(
                NpcSession.SourceEntityType,
                sourceId,
                NpcSession.PursuitId,
                (ushort)(NpcSession.DialogId + 1),
                DialogArgsType.TextResponse,
                args:
                [
                    id,
                    password
                ]);
        };

        NpcSession.OnItemHoverEnter += name => ItemTooltip.Show(
            name,
            0,
            0,
            Game.Input.MouseX,
            Game.Input.MouseY);

        NpcSession.OnItemHoverExit += () => ItemTooltip.Hide();

        NpcSession.OnMerchantItemSelected += selectedIndex =>
        {
            if (NpcSession.SourceId is not { } sourceId)
                return;

            var name = NpcSession.GetMerchantEntryName(selectedIndex);

            if (name is not null)
                Game.Connection.SendMenuResponse(
                    NpcSession.SourceEntityType,
                    sourceId,
                    NpcSession.PursuitId,
                    args: [name]);
        };

        NpcSession.OnListItemSelected += selectedIndex =>
        {
            if (NpcSession.SourceId is not { } sourceId)
                return;

            var slot = NpcSession.GetListEntrySlot(selectedIndex);

            if (slot is null)
                return;

            if (NpcSession.CurrentMenuType is MenuType.ShowPlayerItems or MenuType.ShowPlayerSkills or MenuType.ShowPlayerSpells)
                Game.Connection.SendMenuResponse(
                    NpcSession.SourceEntityType,
                    sourceId,
                    NpcSession.PursuitId,
                    slot.Value);
            else
            {
                var name = NpcSession.GetListEntryName(selectedIndex);

                if (name is not null)
                    Game.Connection.SendMenuResponse(
                        NpcSession.SourceEntityType,
                        sourceId,
                        NpcSession.PursuitId,
                        args: [name]);
            }
        };
    }
    #endregion

    #region Board/Mail Wiring
    private void WireMailControls()
    {
        WireBoardListControl();
        WireArticleListControl();
        WireMailListControl();
        WireArticleReadControl();
        WireMailReadControl();
        WireArticleSendControl();
        WireMailSendControl();

        DeleteConfirm.OnOk += () =>
        {
            PendingDeleteAction?.Invoke();
            PendingDeleteAction = null;
            DeleteConfirm.Hide();
        };

        DeleteConfirm.OnCancel += () =>
        {
            PendingDeleteAction = null;
            DeleteConfirm.Hide();
        };

        WorldState.Board.SessionClosed += HideAllBoardControls;
    }

    private void ToggleSocialStatusPicker()
    {
        if (SocialStatusPicker.Visible)
        {
            SocialStatusPicker.Visible = false;

            if (WorldHud.EmoteButton is not null)
                WorldHud.EmoteButton.IsSelected = false;

            return;
        }

        var emoteBtn = WorldHud.EmoteButton;
        var viewport = WorldHud.ViewportBounds;

        if (emoteBtn is not null)
        {
            SocialStatusPicker.X = emoteBtn.ScreenX - SocialStatusPicker.Width / 2 + emoteBtn.Width / 2;
            SocialStatusPicker.Y = emoteBtn.ScreenY - SocialStatusPicker.Height - 2 + 24;
        } else
        {
            // Fallback positioning when no emote button exists
            SocialStatusPicker.CenterHorizontallyIn(viewport);
            SocialStatusPicker.Y = viewport.Y + viewport.Height - SocialStatusPicker.Height;
        }

        if (SocialStatusPicker.X < viewport.X)
            SocialStatusPicker.X = viewport.X;

        if ((SocialStatusPicker.X + SocialStatusPicker.Width) > (viewport.X + viewport.Width))
            SocialStatusPicker.X = viewport.X + viewport.Width - SocialStatusPicker.Width;

        if (emoteBtn is not null)
            emoteBtn.IsSelected = true;

        SocialStatusPicker.Show();
    }

    private void HideAllBoardControls()
    {
        BoardList.Hide();
        ArticleList.Hide();
        ArticleRead.Hide();
        ArticleSend.Hide();
        MailList.Hide();
        MailRead.Hide();
        MailSend.Hide();
    }

    private void WireBoardListControl()
    {
        BoardList.OnViewBoard += boardId =>
        {
            WorldState.Board.IsBoardListPending = true;
            WorldState.Board.WasOpenedFromBoardList = true;
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, boardId, startPostId: short.MaxValue);
        };

        BoardList.OnClose += () => WorldState.Board.CloseSession();
    }

    private void WireArticleListControl()
    {
        ArticleList.OnViewPost += postId =>
        {
            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                ArticleList.BoardId,
                postId,
                controls: BoardControls.RequestPost);
        };

        ArticleList.OnNewPost += () =>
        {
            ArticleList.Hide();
            ArticleSend.BoardId = ArticleList.BoardId;
            ArticleSend.ShowCompose(WorldHud.PlayerName);
        };

        ArticleList.OnDeletePost += postId =>
        {
            PendingDeleteAction = () =>
            {
                Game.Connection.SendBoardInteraction(BoardRequestType.Delete, ArticleList.BoardId, postId);
                ArticleList.RemoveEntry(postId);
            };

            DeleteConfirm.Show("Delete this post?");
        };

        ArticleList.OnHighlight += postId =>
        {
            Game.Connection.SendBoardInteraction(BoardRequestType.Highlight, ArticleList.BoardId, postId);
        };

        ArticleList.OnLoadMorePosts += lastPostId =>
        {
            LoadingMoreBoardPosts = true;
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, ArticleList.BoardId, startPostId: lastPostId);
        };

        ArticleList.OnUp += () =>
        {
            ArticleList.Hide();

            if (WorldState.Board.WasOpenedFromBoardList && WorldState.Board.AvailableBoards is { Count: > 0 })
                BoardList.ShowBoards(
                    WorldState.Board
                              .AvailableBoards
                              .Select(b => (b.BoardId, b.Name))
                              .ToList(),
                    false);
            else
                WorldState.Board.CloseSession();
        };

        ArticleList.OnClose += () => WorldState.Board.CloseSession();
    }

    private void WireMailListControl()
    {
        MailList.OnViewPost += postId =>
        {
            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                MailList.BoardId,
                postId,
                controls: BoardControls.RequestPost);
        };

        MailList.OnNewMail += () =>
        {
            MailList.Hide();
            MailSend.BoardId = MailList.BoardId;
            MailSend.ShowCompose();
        };

        MailList.OnDeletePost += postId =>
        {
            PendingDeleteAction = () =>
            {
                Game.Connection.SendBoardInteraction(BoardRequestType.Delete, MailList.BoardId, postId);
                MailList.RemoveEntry(postId);
            };

            DeleteConfirm.Show("Delete this post?");
        };

        MailList.OnReplyPost += postId =>
        {
            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                MailList.BoardId,
                postId,
                controls: BoardControls.RequestPost);
        };

        MailList.OnLoadMorePosts += lastPostId =>
        {
            LoadingMoreBoardPosts = true;
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, MailList.BoardId, startPostId: lastPostId);
        };

        MailList.OnUp += () =>
        {
            MailList.Hide();

            if (WorldState.Board.WasOpenedFromBoardList && WorldState.Board.AvailableBoards is { Count: > 0 })
                BoardList.ShowBoards(
                    WorldState.Board
                              .AvailableBoards
                              .Select(b => (b.BoardId, b.Name))
                              .ToList(),
                    false);
            else
                WorldState.Board.CloseSession();
        };

        MailList.OnClose += () => WorldState.Board.CloseSession();
    }

    private void WireArticleReadControl()
    {
        ArticleRead.OnUp += () =>
        {
            ArticleRead.Hide();
            ArticleList.Show();
        };

        ArticleRead.OnClose += () => WorldState.Board.CloseSession();

        ArticleRead.OnPrev += () =>
        {
            var prevId = (short)Math.Min(ArticleRead.CurrentPostId + 1, short.MaxValue);

            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                ArticleRead.BoardId,
                prevId,
                controls: BoardControls.PreviousPage);
        };

        ArticleRead.OnNext += () =>
        {
            var nextId = (short)Math.Max(ArticleRead.CurrentPostId - 1, 1);

            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                ArticleRead.BoardId,
                nextId,
                controls: BoardControls.NextPage);
        };

        ArticleRead.OnDeletePost += postId =>
        {
            PendingDeleteAction = () =>
            {
                Game.Connection.SendBoardInteraction(BoardRequestType.Delete, ArticleRead.BoardId, postId);
                ArticleRead.Hide();
                ArticleList.Show();
            };

            DeleteConfirm.Show("Delete this post?");
        };

        ArticleRead.OnNewPost += () =>
        {
            ArticleRead.Hide();
            ArticleSend.BoardId = ArticleRead.BoardId;
            ArticleSend.ShowCompose(WorldHud.PlayerName);
        };
    }

    private void WireMailReadControl()
    {
        MailRead.OnUp += () =>
        {
            MailRead.Hide();
            MailList.Show();
        };

        MailRead.OnQuit += () => WorldState.Board.CloseSession();

        MailRead.OnPrev += () =>
        {
            var prevId = (short)Math.Min(MailRead.CurrentPostId + 1, short.MaxValue);

            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                MailRead.BoardId,
                prevId,
                controls: BoardControls.PreviousPage);
        };

        MailRead.OnNext += () =>
        {
            var nextId = (short)Math.Max(MailRead.CurrentPostId - 1, 1);

            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                MailRead.BoardId,
                nextId,
                controls: BoardControls.NextPage);
        };

        MailRead.OnReplyPost += _ =>
        {
            MailRead.Hide();
            MailSend.BoardId = MailRead.BoardId;
            MailSend.ShowCompose(MailRead.CurrentAuthor);
        };

        MailRead.OnDeletePost += postId =>
        {
            PendingDeleteAction = () =>
            {
                Game.Connection.SendBoardInteraction(BoardRequestType.Delete, MailRead.BoardId, postId);
                MailRead.Hide();
                MailList.Show();
            };

            DeleteConfirm.Show("Delete this post?");
        };

        MailRead.OnNewMail += () =>
        {
            MailRead.Hide();
            MailSend.BoardId = MailRead.BoardId;
            MailSend.ShowCompose();
        };
    }

    private void WireArticleSendControl()
    {
        ArticleSend.OnSend += (subject, body) =>
        {
            Game.Connection.SendBoardInteraction(
                BoardRequestType.NewPost,
                ArticleSend.BoardId,
                subject: subject,
                message: body);

            // Re-request post list — compose stays visible until server responds
            WorldState.Board.IsBoardListPending = true;
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, ArticleSend.BoardId, startPostId: short.MaxValue);
        };

        ArticleSend.OnCancel += () =>
        {
            ArticleSend.Hide();
            ArticleList.Show();
        };
    }

    private void WireMailSendControl()
    {
        MailSend.OnSend += (recipient, subject, body) =>
        {
            Game.Connection.SendBoardInteraction(
                BoardRequestType.SendMail,
                MailSend.BoardId,
                to: recipient,
                subject: subject,
                message: body);

            // Re-request post list — compose stays visible until server responds
            WorldState.Board.IsBoardListPending = true;
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, MailSend.BoardId, startPostId: short.MaxValue);
        };

        MailSend.OnCancel += () =>
        {
            MailSend.Hide();
            MailList.Show();
        };
    }
    #endregion

    #region HUD Panel Wiring
    private void WireHudPanels(IWorldHud hud)
    {
        // Layout/expand
        if (hud.ChangeLayoutButton is not null)
            hud.ChangeLayoutButton.OnClick += SwapHudLayout;

        if (hud.ExpandButton is not null)
            hud.ExpandButton.OnClick += () => hud.ToggleExpand();

        // Action buttons
        if (hud.OptionButton is not null)
        {
            hud.OptionButton.OnClick += () =>
            {
                hud.OptionButton!.IsSelected = true;
                MainOptions.Show();
            };

            MainOptions.OnClose += () => hud.OptionButton.IsSelected = false;
        }

        if (hud.HelpButton is not null)
            hud.HelpButton.OnClick += () => HotkeyHelp.Show();

        if (hud.SettingsButton is not null)
            hud.SettingsButton.OnClick += () => SettingsDialog.Show();

        if (hud.GroupButton is not null)
            hud.GroupButton.OnClick += () => GroupPanel.Show();

        if (hud.GroupIndicator is not null)
            hud.GroupIndicator.OnClick += () => Game.Connection.ToggleGroup();

        if (hud.UsersButton is not null)
        {
            hud.UsersButton.OnClick += () =>
            {
                if (WorldList.Visible)
                {
                    WorldList.Hide();

                    return;
                }

                hud.UsersButton!.IsSelected = true;
                Game.Connection.RequestWorldList();
            };

            WorldList.OnClose += () => hud.UsersButton.IsSelected = false;
        }

        if (hud.BulletinButton is not null)
        {
            hud.BulletinButton.OnClick += () =>
            {
                hud.BulletinButton!.IsSelected = true;
                Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);
            };

            WorldState.Board.SessionClosed += () => hud.BulletinButton.IsSelected = false;
        }

        if (hud.LegendButton is not null)
            hud.LegendButton.OnClick += () =>
            {
                SelfProfileRequested = true;
                Game.Connection.RequestSelfProfile();
            };

        if (hud.TownMapButton is not null)
            hud.TownMapButton.OnClick += () =>
            {
                if (WorldMap.Visible)
                    WorldMap.HideMap();
            };

        if (hud.EmoteButton is not null)
            hud.EmoteButton.OnClick += ToggleSocialStatusPicker;

        if (hud.EmoteButton is not null)
            SocialStatusPicker.OnClosed += () => hud.EmoteButton.IsSelected = false;

        if (hud.MailButton is not null)
        {
            hud.MailButton.OnClick += () =>
            {
                hud.MailButton!.IsSelected = true;
                Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);
            };

            WorldState.Board.SessionClosed += () => hud.MailButton.IsSelected = false;
        }

        // Slot events
        hud.Inventory.OnSlotClicked += HandleInventorySlotClicked;
        hud.Inventory.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.Inventory, s, t);
        hud.Inventory.OnSlotDroppedOutside += HandleInventoryDropInViewport;
        hud.SkillBook.OnSlotClicked += HandleSkillSlotClicked;
        hud.SkillBook.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SkillBook, s, t);
        hud.SkillBookAlt.OnSlotClicked += HandleSkillSlotClicked;
        hud.SkillBookAlt.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SkillBook, s, t);
        hud.SpellBook.OnSlotClicked += HandleSpellSlotClicked;
        hud.SpellBook.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SpellBook, s, t);
        hud.SpellBook.OnSlotDroppedOutside += HandleSpellSlotDropped;
        hud.SpellBookAlt.OnSlotClicked += HandleSpellSlotClicked;
        hud.SpellBookAlt.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SpellBook, s, t);
        hud.SpellBookAlt.OnSlotDroppedOutside += HandleSpellSlotDropped;

        WireAbilityRightClicks(hud.SkillBook);
        WireAbilityRightClicks(hud.SkillBookAlt);
        WireAbilityRightClicks(hud.SpellBook);
        WireAbilityRightClicks(hud.SpellBookAlt);

        hud.Inventory.OnSlotHoverEnter += HandleInventoryHoverEnter;
        hud.Inventory.OnSlotHoverExit += HandleInventoryHoverExit;

        foreach (var panel in new PanelBase[]
                 {
                     hud.Inventory,
                     hud.SkillBook,
                     hud.SkillBookAlt,
                     hud.SpellBook,
                     hud.SpellBookAlt
                 })
        {
            panel.OnSlotHoverEnter += slot => WorldHud.SetDescription(slot.SlotName);
            panel.OnSlotHoverExit += () => WorldHud.SetDescription(null);
        }
    }

    private void SwapHudLayout()
    {
        WorldHud.Inventory.ForceHoverExit();

        var activeTab = WorldHud.ActiveTab;

        ((UIPanel)WorldHud).Visible = false;
        WorldHud = WorldHud == SmallHud ? LargeHud : SmallHud;
        ((UIPanel)WorldHud).Visible = true;
        WorldHud.ShowTab(activeTab);

        var viewport = WorldHud.ViewportBounds;
        Camera.Resize(viewport.Width, viewport.Height);
        WorldList.SetViewportBounds(viewport);
        BoardList.SetViewportBounds(viewport);
        ArticleList.SetViewportBounds(viewport);
        ArticleRead.SetViewportBounds(viewport);
        ArticleSend.SetViewportBounds(viewport);
        MailList.SetViewportBounds(viewport);
        MailRead.SetViewportBounds(viewport);
        MailSend.SetViewportBounds(viewport);

        FollowPlayerCamera();
    }

    /// <summary>
    ///     Calls an action on all HUD instances so both stay in sync regardless of which is visible.
    /// </summary>
    private void UpdateHuds(Action<IWorldHud> action)
    {
        action(SmallHud);
        action(LargeHud);
    }
    #endregion

    #region Options Dialog Wiring
    private void WireOptionsDialog()
    {
        MainOptions.OnMacro += () => ToggleSubPanel(MacroMenu);
        MainOptions.OnSettings += () => ToggleSubPanel(SettingsDialog);
        MainOptions.OnFriends += () => ToggleSubPanel(FriendsList);

        MainOptions.OnExit += () => Game.Connection.RequestExit();

        MainOptions.OnSoundVolumeChanged += volume =>
        {
            Game.SoundSystem.SetSoundVolume(volume);
            ClientSettings.SoundVolume = volume;
            ClientSettings.Save();
        };

        MainOptions.OnMusicVolumeChanged += volume =>
        {
            Game.SoundSystem.SetMusicVolume(volume);
            ClientSettings.MusicVolume = volume;
            ClientSettings.Save();
        };

        // Apply saved volume settings
        MainOptions.SetSoundVolume(ClientSettings.SoundVolume);
        MainOptions.SetMusicVolume(ClientSettings.MusicVolume);
        Game.SoundSystem.SetSoundVolume(ClientSettings.SoundVolume);
        Game.SoundSystem.SetMusicVolume(ClientSettings.MusicVolume);
    }

    private static void ToggleSubPanel(PrefabPanel panel)
    {
        if (panel.Visible)
            panel.Hide();
        else if (panel is MacroMenuControl macro)
            macro.SlideIn();
        else if (panel is SettingsControl settings)
            settings.SlideIn();
        else if (panel is FriendsListControl friends)
            friends.SlideIn();
    }
    #endregion
}