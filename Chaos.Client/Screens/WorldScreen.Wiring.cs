#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.Hud;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Controls.World.Options;
using Chaos.Client.Models;
using Chaos.Client.Rendering;
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
            Game.World.Exchange.Close();
        };
    }
    #endregion

    #region Mail Wiring
    private void WireMailControls()
    {
        // Mail list events
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
            MailSend.IsPublicBoard = MailList.IsPublicBoard;
            MailSend.ShowCompose();
        };

        MailList.OnDeletePost += postId => Game.Connection.SendBoardInteraction(BoardRequestType.Delete, MailList.BoardId, postId);

        MailList.OnReplyPost += postId => Game.Connection.SendBoardInteraction(
            BoardRequestType.ViewPost,
            MailList.BoardId,
            postId,
            controls: BoardControls.RequestPost);

        // Pagination: load next page of posts starting from the last visible post
        MailList.OnLoadMorePosts += lastPostId =>
        {
            LoadingMoreBoardPosts = true;
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, MailList.BoardId, startPostId: lastPostId);
        };

        // Mail read events
        MailRead.OnQuit += () =>
        {
            MailRead.Hide();

            // Re-request the mail list to refresh
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, MailRead.BoardId);
        };

        MailRead.OnReplyPost += _ =>
        {
            MailRead.Hide();
            MailSend.BoardId = MailRead.BoardId;
            MailSend.IsPublicBoard = MailRead.IsPublicBoard;
            MailSend.ShowCompose(MailRead.CurrentAuthor);
        };

        MailRead.OnDeletePost += postId =>
        {
            Game.Connection.SendBoardInteraction(BoardRequestType.Delete, MailRead.BoardId, postId);
            MailRead.Hide();
        };

        MailRead.OnNewMail += () =>
        {
            MailRead.Hide();
            MailSend.BoardId = MailRead.BoardId;
            MailSend.IsPublicBoard = MailRead.IsPublicBoard;
            MailSend.ShowCompose();
        };

        MailRead.OnPrev += () =>
        {
            // Server expects postId+1 for PreviousPage (it decrements internally)
            var prevId = (short)(MailRead.CurrentPostId + 1);

            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                MailRead.BoardId,
                prevId,
                controls: BoardControls.PreviousPage);
        };

        MailRead.OnNext += () =>
        {
            // Server expects postId-1 for NextPage (it increments internally)
            var nextId = (short)(MailRead.CurrentPostId - 1);

            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                MailRead.BoardId,
                nextId,
                controls: BoardControls.NextPage);
        };

        // Mail send events
        MailSend.OnSend += (recipient, subject, body) =>
        {
            if (MailSend.IsPublicBoard)
                Game.Connection.SendBoardInteraction(
                    BoardRequestType.NewPost,
                    MailSend.BoardId,
                    subject: subject,
                    message: body);
            else
                Game.Connection.SendBoardInteraction(
                    BoardRequestType.SendMail,
                    MailSend.BoardId,
                    to: recipient,
                    subject: subject,
                    message: body);

            MailSend.Hide();
        };

        MailSend.OnCancel += () => MailSend.Hide();
    }
    #endregion

    #region Merchant Dialog Wiring
    private void WireMerchantDialog()
    {
        MerchantDialog.OnClose += () =>
        {
            if (MerchantDialog.SourceId is { } sourceId)
                Game.Connection.SendMenuResponse(MerchantDialog.SourceEntityType, sourceId, MerchantDialog.PursuitId);
        };

        MerchantDialog.OnItemSelected += selectedIndex =>
        {
            if (MerchantDialog.SourceId is not { } sourceId)
                return;

            var slot = MerchantDialog.GetEntrySlot(selectedIndex);

            if (slot is null)
                return;

            // ShowPlayerItems/ShowPlayerSkills/ShowPlayerSpells send the slot byte
            // ShowItems/ShowSkills/ShowSpells send the name as an arg
            if (MerchantDialog.CurrentMenuType is MenuType.ShowPlayerItems or MenuType.ShowPlayerSkills or MenuType.ShowPlayerSpells)
                Game.Connection.SendMenuResponse(
                    MerchantDialog.SourceEntityType,
                    sourceId,
                    MerchantDialog.PursuitId,
                    slot.Value);
            else
            {
                var name = MerchantDialog.GetEntryName(selectedIndex);

                if (name is not null)
                    Game.Connection.SendMenuResponse(
                        MerchantDialog.SourceEntityType,
                        sourceId,
                        MerchantDialog.PursuitId,
                        args: [name]);
            }
        };
    }
    #endregion

    #region NPC Dialog Wiring
    private void WireNpcDialog()
    {
        NpcDialog.OnClose += () =>
        {
            if (NpcDialog.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    0); // dialogId 0 = close
        };

        NpcDialog.OnNext += () =>
        {
            if (NpcDialog.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId + 1));
        };

        NpcDialog.OnPrevious += () =>
        {
            if (NpcDialog.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId - 1));
        };

        NpcDialog.OnOptionSelected += optionIndex =>
        {
            if (NpcDialog.SourceId is not { } sourceId)
                return;

            if (NpcDialog.IsMenuMode)
            {
                // Menu responses use MenuInteraction opcode (0x39) with the option's pursuit ID
                var pursuitId = NpcDialog.GetOptionPursuitId(optionIndex);

                Game.Connection.SendMenuResponse(NpcDialog.SourceEntityType, sourceId, pursuitId);
            } else

                // Dialog responses use DialogInteraction opcode (0x3A) with option index
                Game.Connection.SendDialogResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId + 1),
                    DialogArgsType.MenuResponse,
                    (byte)(optionIndex + 1));
        };

        NpcDialog.OnTextSubmit += text =>
        {
            if (NpcDialog.SourceId is not { } sourceId)
                return;

            if (NpcDialog.IsMenuMode)

                // Menu text responses use MenuInteraction opcode (0x39)
                Game.Connection.SendMenuResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    args: [text]);
            else

                // Dialog text responses use DialogInteraction opcode (0x3A)
                Game.Connection.SendDialogResponse(
                    NpcDialog.SourceEntityType,
                    sourceId,
                    NpcDialog.PursuitId,
                    (ushort)(NpcDialog.DialogId + 1),
                    DialogArgsType.TextResponse,
                    args: [text]);
        };
    }
    #endregion

    private record struct AislingDrawDataEntry(
        AislingAppearance Appearance,
        int FrameIndex,
        bool Flip,
        bool IsFrontFacing,
        string AnimSuffix,
        int EmotionFrame,
        AislingDrawData? DrawData);

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
            hud.OptionButton.OnClick += () => MainOptions.Show();

        if (hud.HelpButton is not null)
            hud.HelpButton.OnClick += () => HotkeyHelp.Show();

        if (hud.GroupButton is not null)
            hud.GroupButton.OnClick += () => GroupPanel.Show();

        if (hud.UsersButton is not null)
            hud.UsersButton.OnClick += () =>
            {
                if (WorldList.Visible)
                {
                    WorldList.Hide();

                    return;
                }

                WorldList.Show(new List<WorldListEntry>(), 0);
                Game.Connection.RequestWorldList();
            };

        if (hud.BulletinButton is not null)
            hud.BulletinButton.OnClick += () => Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);

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
            hud.EmoteButton.OnClick += () =>
            {
                if (SocialStatusPicker.Visible)
                {
                    SocialStatusPicker.Visible = false;

                    return;
                }

                SocialStatusPicker.X = hud.EmoteButton!.ScreenX - SocialStatusPicker.Width / 2 + hud.EmoteButton.Width / 2;
                SocialStatusPicker.Y = hud.EmoteButton.ScreenY - SocialStatusPicker.Height - 2;

                if (SocialStatusPicker.X < 0)
                    SocialStatusPicker.X = 0;

                if ((SocialStatusPicker.X + SocialStatusPicker.Width) > 640)
                    SocialStatusPicker.X = 640 - SocialStatusPicker.Width;

                SocialStatusPicker.Show();
            };

        if (hud.MailButton is not null)
            hud.MailButton.OnClick += () => Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard);

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
        hud.SpellBookAlt.OnSlotClicked += HandleSpellSlotClicked;
        hud.SpellBookAlt.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SpellBook, s, t);

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
            Game.Settings.SoundVolume = volume;
            Game.Settings.Save();
        };

        MainOptions.OnMusicVolumeChanged += volume =>
        {
            Game.SoundSystem.SetMusicVolume(volume);
            Game.Settings.MusicVolume = volume;
            Game.Settings.Save();
        };

        // Apply saved volume settings
        MainOptions.SetSoundVolume(Game.Settings.SoundVolume);
        MainOptions.SetMusicVolume(Game.Settings.MusicVolume);
        Game.SoundSystem.SetSoundVolume(Game.Settings.SoundVolume);
        Game.SoundSystem.SetMusicVolume(Game.Settings.MusicVolume);
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