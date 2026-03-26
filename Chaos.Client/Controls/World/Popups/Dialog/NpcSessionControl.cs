#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Networking;
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
    // Container controls from lnpcd prefab
    private readonly UIButton? CloseButton;
    private readonly TextElement DialogText = new();
    private readonly DialogTextEntryPanel DialogTextEntry;
    private readonly MenuTextEntryPanel MenuTextEntry;
    private readonly MerchantBrowserPanel MerchantBrowser;
    private readonly UIButton? NextButton;

    private readonly UILabel? NpcNameLabel;

    // Sub-panels (Layer 2 content)
    private readonly OptionMenuPanel OptionMenu;
    private readonly Rectangle PortraitRect;
    private readonly UIButton? PreviousButton;
    private readonly ProtectedEntryPanel ProtectedEntry;
    private readonly Rectangle TextRect;
    private readonly UIButton? TopButton;
    private bool OwnsPortraitTexture;

    // Portrait texture
    private Texture2D? PortraitTexture;
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

        // Container buttons
        CloseButton = CreateButton("CloseBtn");
        NextButton = CreateButton("NextBtn");
        PreviousButton = CreateButton("PrevBtn");
        TopButton = CreateButton("TopBtn");

        // Background images
        CreateImage("MessageDialog"); // nd_talk.spf — bottom dialog bar
        CreateImage("NPCTile"); // nd_npcbg.spf — portrait background

        // Layout rects
        NpcNameLabel = CreateLabel("Name");
        TextRect = GetRect("Text");
        PortraitRect = GetRect("NPCTile");

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

        // Draw portrait centered in NPCTile rect
        if (PortraitTexture is not null && (PortraitRect != Rectangle.Empty))
        {
            var portraitX = sx + PortraitRect.X + (PortraitRect.Width - PortraitTexture.Width) / 2;
            var portraitY = sy + PortraitRect.Y + (PortraitRect.Height - PortraitTexture.Height) / 2;

            spriteBatch.Draw(PortraitTexture, new Vector2(portraitX, portraitY), Color.White);
        }

        // Draw dialog text (for Normal dialog type)
        if (TextRect != Rectangle.Empty)
            DialogText.Draw(
                spriteBatch,
                new Rectangle(
                    sx + TextRect.X,
                    sy + TextRect.Y,
                    TextRect.Width,
                    TextRect.Height));
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
        DialogText.Update(string.Empty, Color.White);
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

    private void SetDialogText(string? text)
    {
        if (text is null)
        {
            DialogText.Update(string.Empty, Color.White);

            return;
        }

        DialogText.UpdateWrapped(text, TextRect.Width, Color.White);
    }

    private void SetNavigationButtons(bool hasNext, bool hasPrevious)
    {
        if (NextButton is not null)
        {
            NextButton.Visible = hasNext;
            NextButton.Enabled = hasNext;
        }

        if (PreviousButton is not null)
        {
            PreviousButton.Visible = hasPrevious;
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
                // Option list sub-panel
                HideNavigationButtons();

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
            NpcNameLabel.Text = args.Name ?? string.Empty;

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
            NpcNameLabel.Text = args.Name ?? string.Empty;

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

        base.Update(gameTime, input);
    }
}