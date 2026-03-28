#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Dialog;

/// <summary>
///     NPC dialog/menu session container using the lnpcd prefab. This is the Layer 1 container that is always visible when
///     any dialog or menu is open. It provides the bottom bar (nd_talk.spf), NPC portrait (nd_npcbg.spf), NPC name, dialog
///     text, and navigation buttons (Next/Prev/Close/Top). Content sub-panels (Layer 2) float on top for specific
///     interaction types.
/// </summary>
public sealed class NpcSessionControl : PrefabPanel
{
    // Scroll arrow buttons for dialog text overflow (nd_arw.spf)
    private const float ARROW_ANIM_INTERVAL = 0.5f;

    // Container controls from lnpcd prefab
    private readonly UIButton? CloseButton;
    private readonly DialogTextEntryPanel DialogTextEntry;
    private readonly UILabel DialogTextLabel;
    private readonly MenuTextEntryPanel MenuTextEntry;
    private readonly MerchantBrowserPanel MerchantBrowser;
    private readonly UIButton? NextButton;

    private readonly UILabel? NpcNameLabel;
    private readonly UIImage? NpcTileImage;

    // Sub-panels (Layer 2 content)
    private readonly OptionMenuPanel OptionMenu;
    private readonly Rectangle PortraitRect;
    private readonly UIButton? PreviousButton;
    private readonly ProtectedEntryPanel ProtectedEntry;
    private readonly UIButton? ScrollDownButton;
    private readonly Texture2D?[] ScrollDownFrames = new Texture2D?[2];
    private readonly UIButton? ScrollUpButton;
    private readonly Texture2D?[] ScrollUpFrames = new Texture2D?[2];
    private readonly UIButton? TopButton;
    private bool ArrowAnimFrame;
    private float ArrowAnimTimer;
    private bool OwnsPortraitTexture;

    // Portrait texture
    private Texture2D? PortraitTexture;
    private int ScrollLine;
    public DialogType? CurrentDialogType { get; private set; }
    public MenuType? CurrentMenuType { get; private set; }
    public ushort DialogId { get; private set; }
    public bool IsDialogOpcode { get; private set; }

    // Portrait metadata for rendering by WorldScreen
    public string? NpcName { get; private set; }
    public DisplayColor PortraitColor { get; private set; }
    public ushort PortraitSpriteId { get; private set; }
    public ushort PursuitId { get; private set; }
    public bool ShouldIllustrate { get; private set; }

    // Session state
    public EntityType SourceEntityType { get; private set; }
    public uint? SourceId { get; private set; }

    public NpcSessionControl()
        : base("lnpcd", false)
    {
        Name = "NpcSession";
        Visible = false;
        X = 0;
        Y = 0;

        // Background images (drawn first — buttons must be added after to render on top)
        CreateImage("MessageDialog"); // nd_talk.spf — bottom dialog bar
        NpcTileImage = CreateImage("NPCTile"); // nd_npcbg.spf — portrait background

        // Container buttons (added after images so they draw on top)
        CloseButton = CreateButton("CloseBtn");
        NextButton = CreateButton("NextBtn");
        PreviousButton = CreateButton("PrevBtn");
        TopButton = CreateButton("TopBtn");

        // Scroll arrow buttons for dialog text overflow (nd_arw.spf: 0-1 = up, 2-3 = down)
        var uiCache = UiRenderer.Instance!;
        ScrollUpFrames[0] = uiCache.GetSpfTexture("nd_arw.spf");
        ScrollUpFrames[1] = uiCache.GetSpfTexture("nd_arw.spf", 1);
        ScrollDownFrames[0] = uiCache.GetSpfTexture("nd_arw.spf", 2);
        ScrollDownFrames[1] = uiCache.GetSpfTexture("nd_arw.spf", 3);
        var upArrowTexture = ScrollUpFrames[0]!;
        var downArrowTexture = ScrollDownFrames[0]!;

        if (CloseButton is not null)
        {
            ScrollDownButton = new UIButton
            {
                Name = "ScrollDown",
                NormalTexture = downArrowTexture,
                X = CloseButton.X + CloseButton.Width - downArrowTexture.Width - 3,
                Y = CloseButton.Y - downArrowTexture.Height - 1,
                Width = downArrowTexture.Width,
                Height = downArrowTexture.Height,
                Visible = false
            };

            ScrollUpButton = new UIButton
            {
                Name = "ScrollUp",
                NormalTexture = upArrowTexture,
                X = ScrollDownButton.X - 3,
                Y = ScrollDownButton.Y - upArrowTexture.Height - 27,
                Width = upArrowTexture.Width,
                Height = upArrowTexture.Height,
                Visible = false
            };

            AddChild(ScrollDownButton);
            AddChild(ScrollUpButton);

            ScrollDownButton.OnClick += () => ScrollText(1);
            ScrollUpButton.OnClick += () => ScrollText(-1);
        }

        // Layout rects
        NpcNameLabel = CreateLabel("Name");
        PortraitRect = GetRect("NPCTile");

        // Dialog text label — word-wrapped, shifted up 10px from prefab rect
        var textRect = GetRect("Text");

        DialogTextLabel = new UILabel
        {
            X = textRect.X,
            Y = textRect.Y + 1,
            Width = textRect.Width,
            Height = 3 * TextRenderer.CHAR_HEIGHT + 2,
            WordWrap = true,
            ForegroundColor = Color.White
        };

        AddChild(DialogTextLabel);

        // Wire container button events
        if (CloseButton is not null)
            CloseButton.OnClick += () =>
            {
                HideAll();
                OnClose?.Invoke();
            };

        if (NextButton is not null)
            NextButton.OnClick += () => OnNext?.Invoke();

        if (PreviousButton is not null)
            PreviousButton.OnClick += () => OnPrevious?.Invoke();

        if (TopButton is not null)
            TopButton.OnClick += () =>
            {
                HideAll();
                OnClose?.Invoke();
            };

        // Create sub-panels as children
        OptionMenu = new OptionMenuPanel();
        DialogTextEntry = new DialogTextEntryPanel();
        MenuTextEntry = new MenuTextEntryPanel();
        MerchantBrowser = new MerchantBrowserPanel();
        ProtectedEntry = new ProtectedEntryPanel();

        AddChild(OptionMenu);
        AddChild(DialogTextEntry);
        AddChild(MenuTextEntry);
        AddChild(MerchantBrowser);
        AddChild(ProtectedEntry);

        // Wire sub-panel events — forward to container events
        OptionMenu.OnOptionSelected += index => OnOptionSelected?.Invoke(index);

        OptionMenu.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        DialogTextEntry.OnTextSubmit += text => OnTextSubmit?.Invoke(text);

        DialogTextEntry.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        MenuTextEntry.OnTextSubmit += text => OnTextSubmit?.Invoke(text);

        MenuTextEntry.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        MerchantBrowser.OnItemSelected += index => OnMerchantItemSelected?.Invoke(index);

        MerchantBrowser.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        ProtectedEntry.OnProtectedSubmit += (id, pw) => OnProtectedSubmit?.Invoke(id, pw);

        ProtectedEntry.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };
    }

    public override void Dispose()
    {
        DisposePortrait();
        base.Dispose();
    }

    private void DisposePortrait()
    {
        if (OwnsPortraitTexture)
            PortraitTexture?.Dispose();

        PortraitTexture = null;
        OwnsPortraitTexture = false;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var sx = ScreenX;
        var sy = ScreenY;

        if (PortraitTexture is not null)
        {
            if (ShouldIllustrate)
            {
                // NPCIllustration: left-aligned, bottom edge sits on top of the bottom bar (y=372)
                var illustY = 372 - PortraitTexture.Height;
                spriteBatch.Draw(PortraitTexture, new Vector2(0, illustY), Color.White);
            } else if (PortraitRect != Rectangle.Empty)
            {
                // Creature/item sprite: centered in NPCTile rect
                var portraitX = sx + PortraitRect.X + (PortraitRect.Width - PortraitTexture.Width) / 2;
                var portraitY = sy + PortraitRect.Y + (PortraitRect.Height - PortraitTexture.Height) / 2;

                spriteBatch.Draw(PortraitTexture, new Vector2(portraitX, portraitY), Color.White);
            }
        }
    }

    /// <summary>
    ///     Returns the previous args string for menu text entry (TextEntryWithArgs).
    /// </summary>
    public string? GetMenuTextPreviousArgs() => MenuTextEntry.PreviousArgs;

    /// <summary>
    ///     Returns the name of the merchant entry at the given index.
    /// </summary>
    public string? GetMerchantEntryName(int index) => MerchantBrowser.GetEntryName(index);

    /// <summary>
    ///     Returns the slot byte for the merchant entry at the given index.
    /// </summary>
    public byte? GetMerchantEntrySlot(int index) => MerchantBrowser.GetEntrySlot(index);

    /// <summary>
    ///     Returns the pursuit ID for the option at the given index in the OptionMenu sub-panel.
    /// </summary>
    public ushort GetOptionPursuitId(int index) => OptionMenu.GetOptionPursuitId(index);

    /// <summary>
    ///     Hides the container and all sub-panels.
    /// </summary>
    public void HideAll()
    {
        DisposePortrait();
        HideAllSubPanels();
        Hide();
    }

    private void HideAllSubPanels()
    {
        OptionMenu.Hide();
        DialogTextEntry.Hide();
        MenuTextEntry.Hide();
        MerchantBrowser.Hide();
        ProtectedEntry.Hide();
        ScrollLine = 0;
        DialogTextLabel.ScrollOffset = 0;
        DialogTextLabel.Text = string.Empty;
        UpdateScrollButtons();
    }

    private void HideNavigationButtons()
    {
        if (NextButton is not null)
        {
            NextButton.Visible = false;
            NextButton.Enabled = false;
        }

        if (PreviousButton is not null)
        {
            PreviousButton.Visible = false;
            PreviousButton.Enabled = false;
        }

        if (CloseButton is not null)
        {
            CloseButton.Visible = false;
            CloseButton.Enabled = false;
        }

        if (TopButton is not null)
        {
            TopButton.Visible = false;
            TopButton.Enabled = false;
        }
    }

    private bool IsNormalDialogActive() => IsDialogOpcode && CurrentDialogType is DialogType.Normal;

    // Events — WorldScreen.Wiring subscribes to these
    public event Action? OnClose;
    public event Action<int>? OnMerchantItemSelected;
    public event Action? OnNext;
    public event Action<int>? OnOptionSelected;
    public event Action? OnPrevious;
    public event Action<string, string>? OnProtectedSubmit;
    public event Action<string>? OnTextSubmit;

    private void ScrollText(int direction)
    {
        var totalLines = DialogTextLabel.ContentHeight / TextRenderer.CHAR_HEIGHT;
        var innerH = DialogTextLabel.Height - DialogTextLabel.PaddingTop * 2;
        var visibleLines = innerH / TextRenderer.CHAR_HEIGHT;
        var maxScroll = Math.Max(0, totalLines - visibleLines);

        ScrollLine = Math.Clamp(ScrollLine + direction, 0, maxScroll);
        DialogTextLabel.ScrollOffset = ScrollLine * TextRenderer.CHAR_HEIGHT;

        UpdateScrollButtons();
    }

    private void SetDialogText(string? text)
    {
        ScrollLine = 0;
        DialogTextLabel.ScrollOffset = 0;
        DialogTextLabel.Text = text ?? string.Empty;
        UpdateScrollButtons();
    }

    private void SetNavigationButtons(bool hasNext, bool hasPrevious)
    {
        if (NextButton is not null)
        {
            NextButton.Visible = true;
            NextButton.Enabled = hasNext;
        }

        if (PreviousButton is not null)
        {
            PreviousButton.Visible = true;
            PreviousButton.Enabled = hasPrevious;
        }

        if (CloseButton is not null)
        {
            CloseButton.Visible = true;
            CloseButton.Enabled = true;
        }

        if (TopButton is not null)
        {
            TopButton.Visible = true;
            TopButton.Enabled = true;
        }
    }

    /// <summary>
    ///     Sets the NPC portrait texture. If ownsTexture is true, the container will dispose the texture when replaced or
    ///     hidden.
    /// </summary>
    public void SetPortrait(Texture2D? texture, bool ownsTexture)
    {
        DisposePortrait();
        PortraitTexture = texture;
        OwnsPortraitTexture = ownsTexture;

        // Show the NPCTile background only for sprite portraits (not illustrations, not when hidden)
        if (NpcTileImage is not null)
            NpcTileImage.Visible = texture is not null && !ownsTexture;
    }

    /// <summary>
    ///     Shows the container for a DisplayDialog packet (opcode 0x30).
    /// </summary>
    public void ShowDialog(DisplayDialogArgs args)
    {
        IsDialogOpcode = true;
        CurrentDialogType = args.DialogType;
        CurrentMenuType = null;
        SourceEntityType = args.EntityType;
        SourceId = args.SourceId;
        PursuitId = args.PursuitId ?? 0;
        DialogId = args.DialogId;
        NpcName = args.Name;
        PortraitSpriteId = args.Sprite;
        PortraitColor = args.Color;
        ShouldIllustrate = args.ShouldIllustrate;

        HideAllSubPanels();

        switch (args.DialogType)
        {
            case DialogType.Normal:
                // Container-only: show text + navigation buttons
                SetDialogText(args.Text);
                SetNavigationButtons(args.HasNextButton, args.HasPreviousButton);

                break;

            case DialogType.DialogMenu:
            case DialogType.CreatureMenu:
                // Option list sub-panel — dialog text in bottom bar, prev/next per server, close visible, top hidden
                SetDialogText(args.Text);
                SetNavigationButtons(args.HasNextButton, args.HasPreviousButton);

                if (TopButton is not null)
                {
                    TopButton.Visible = false;
                    TopButton.Enabled = false;
                }

                if (args.Options is not null)
                {
                    var options = args.Options
                                      .Select(o => (o, (ushort)0))
                                      .ToList();
                    OptionMenu.ShowOptions(options);
                }

                break;

            case DialogType.TextEntry:
            case DialogType.Speak:
                // Text entry sub-panel (lnpcd4)
                HideNavigationButtons();

                DialogTextEntry.ShowTextEntry(
                    args.TextBoxPrompt ?? args.Text ?? string.Empty,
                    (byte)(args.TextBoxLength ?? 255),
                    string.Empty);

                break;

            case DialogType.Protected:
                // Dual ID/password entry sub-panel
                HideNavigationButtons();
                ProtectedEntry.ShowProtected(args.Text ?? string.Empty);

                break;

            default:
                return;
        }

        if (NpcNameLabel is not null)
        {
            NpcNameLabel.Text = args.Name ?? string.Empty;
            NpcNameLabel.ForegroundColor = new Color(0, 255, 0);
        }

        Show();
    }

    /// <summary>
    ///     Shows the container for a DisplayMenu packet (opcode 0x2F).
    /// </summary>
    public void ShowMenu(DisplayMenuArgs args, ConnectionManager connection)
    {
        IsDialogOpcode = false;
        CurrentDialogType = null;
        CurrentMenuType = args.MenuType;
        SourceEntityType = args.EntityType;
        SourceId = args.SourceId;
        PursuitId = args.PursuitId;
        DialogId = 0;
        NpcName = args.Name;
        PortraitSpriteId = args.Sprite;
        PortraitColor = args.Color;
        ShouldIllustrate = args.ShouldIllustrate;

        HideAllSubPanels();
        HideNavigationButtons();

        switch (args.MenuType)
        {
            case MenuType.Menu:
            case MenuType.MenuWithArgs:
                if (args.Options is not null)
                {
                    var options = args.Options
                                      .Select(o => (o.Text, o.Pursuit))
                                      .ToList();
                    OptionMenu.ShowOptions(options);
                }

                break;

            case MenuType.TextEntry:
                MenuTextEntry.ShowTextEntry(null);

                break;

            case MenuType.TextEntryWithArgs:
                MenuTextEntry.ShowTextEntry(args.Args);

                break;

            case MenuType.ShowItems:
            case MenuType.ShowPlayerItems:
            case MenuType.ShowSkills:
            case MenuType.ShowSpells:
            case MenuType.ShowPlayerSkills:
            case MenuType.ShowPlayerSpells:
                MerchantBrowser.ShowMerchant(args, connection);

                break;

            default:
                return;
        }

        if (NpcNameLabel is not null)
        {
            NpcNameLabel.Text = args.Name ?? string.Empty;
            NpcNameLabel.ForegroundColor = new Color(0, 255, 0);
        }

        Show();
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Escape closes everything — only if no sub-panel handled it first
        // Sub-panels handle Escape internally and fire OnClose which calls HideAll
        // For Normal dialog (no sub-panel), we handle Escape here
        if (input.WasKeyPressed(Keys.Escape) && IsNormalDialogActive())
        {
            HideAll();
            OnClose?.Invoke();

            return;
        }

        // Animate scroll arrow buttons (flip frames every 500ms)
        var anyArrowVisible = ScrollUpButton is { Visible: true } || ScrollDownButton is { Visible: true };

        if (anyArrowVisible)
        {
            ArrowAnimTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (ArrowAnimTimer >= ARROW_ANIM_INTERVAL)
            {
                ArrowAnimTimer -= ARROW_ANIM_INTERVAL;
                ArrowAnimFrame = !ArrowAnimFrame;
                var frameIndex = ArrowAnimFrame ? 1 : 0;

                if (ScrollUpButton is { Visible: true })
                    ScrollUpButton.NormalTexture = ScrollUpFrames[frameIndex];

                if (ScrollDownButton is { Visible: true })
                    ScrollDownButton.NormalTexture = ScrollDownFrames[frameIndex];
            }
        }

        base.Update(gameTime, input);
    }

    private void UpdateScrollButtons()
    {
        var totalLines = DialogTextLabel.ContentHeight / TextRenderer.CHAR_HEIGHT;
        var innerH = DialogTextLabel.Height - DialogTextLabel.PaddingTop * 2;
        var visibleLines = innerH / TextRenderer.CHAR_HEIGHT;

        if (totalLines <= visibleLines)
        {
            if (ScrollUpButton is not null)
                ScrollUpButton.Visible = false;

            if (ScrollDownButton is not null)
                ScrollDownButton.Visible = false;

            return;
        }

        var maxScroll = totalLines - visibleLines;

        if (ScrollUpButton is not null)
        {
            ScrollUpButton.Visible = ScrollLine > 0;
            ScrollUpButton.Enabled = ScrollLine > 0;
        }

        if (ScrollDownButton is not null)
        {
            ScrollDownButton.Visible = ScrollLine < maxScroll;
            ScrollDownButton.Enabled = ScrollLine < maxScroll;
        }
    }
}