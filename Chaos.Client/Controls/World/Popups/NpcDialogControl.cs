#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     NPC dialog/menu panel using lnpcd prefab variants. Handles DisplayMenuArgs and DisplayDialogArgs from the server.
///     Displays NPC portrait, text, selectable options, and navigation buttons.
/// </summary>
public sealed class NpcDialogControl : PrefabPanel
{
    private const int OPTION_ROW_HEIGHT = 16;

    private readonly UILabel? NpcNameLabel;
    private readonly List<OptionEntry> Options = [];
    private readonly Rectangle OptionsRect;
    private readonly UIImage? PortraitBackground;

    // NPC portrait
    private readonly Rectangle PortraitRect;

    // Dialog state
    private readonly UIImage? TextImage;
    private readonly Rectangle TextRect;
    private int HoveredOption = -1;
    private UIImage? PortraitSprite;
    private int SelectedOption = -1;

    // Current dialog/menu context
    public ushort DialogId { get; private set; }
    public bool IsMenuMode { get; private set; }

    /// <summary>
    ///     The creature/item sprite ID for the portrait.
    /// </summary>
    public ushort PortraitSpriteId { get; private set; }

    public ushort PursuitId { get; private set; }

    /// <summary>
    ///     Whether the current dialog has a portrait illustration.
    /// </summary>
    public bool ShouldIllustrate { get; private set; }

    public EntityType SourceEntityType { get; private set; }
    public uint? SourceId { get; private set; }

    public UIButton? CloseButton { get; }
    public UITextBox? InputTextBox { get; }
    public UIButton? NextButton { get; }
    public UIButton? PreviousButton { get; }
    public UIButton? TopButton { get; }

    public NpcDialogControl(GraphicsDevice device)
        : base(device, "lnpcd")
    {
        Name = "NpcDialog";
        Visible = false;

        var elements = AutoPopulate();

        CloseButton = elements.GetValueOrDefault("CloseBtn") as UIButton;
        NextButton = elements.GetValueOrDefault("NextBtn") as UIButton;
        PreviousButton = elements.GetValueOrDefault("PrevBtn") as UIButton;
        TopButton = elements.GetValueOrDefault("TopBtn") as UIButton;

        if (CloseButton is not null)
            CloseButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

        if (NextButton is not null)
            NextButton.OnClick += () => OnNext?.Invoke();

        if (PreviousButton is not null)
            PreviousButton.OnClick += () => OnPrevious?.Invoke();

        if (TopButton is not null)
            TopButton.OnClick += () => OnClose?.Invoke();

        // Layout rects
        PortraitRect = GetRect("NPCTile");
        TextRect = GetRect("Text");
        OptionsRect = GetRect("MenuDialog");

        // Portrait background (nd_npcbg.spf)
        PortraitBackground = elements.GetValueOrDefault("NPCTile") as UIImage;

        NpcNameLabel = CreateLabel("Name");

        TextImage = new UIImage
        {
            Name = "DialogText",
            X = TextRect.X,
            Y = TextRect.Y,
            Width = TextRect.Width,
            Height = TextRect.Height,
            Visible = false
        };
        AddChild(TextImage);
    }

    private void ClearOptions()
    {
        foreach (var opt in Options)
            opt.CachedText?.Dispose();

        Options.Clear();
        SelectedOption = -1;
        HoveredOption = -1;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var sx = ScreenX;
        var sy = ScreenY;

        // Option text rows
        for (var i = 0; i < Options.Count; i++)
        {
            var opt = Options[i];

            var color = i == SelectedOption
                ? Color.Yellow
                : i == HoveredOption
                    ? Color.LightGoldenrodYellow
                    : Color.White;

            opt.CachedText ??= new CachedText(Device);
            opt.CachedText.Update(opt.Text, color);
            opt.CachedText.Draw(spriteBatch, new Vector2(sx + opt.Rect.X, sy + opt.Rect.Y));
        }
    }

    /// <summary>
    ///     Gets the pursuit ID for the menu option at the given index, or 0 if not a menu or out of range.
    /// </summary>
    public ushort GetOptionPursuitId(int optionIndex)
    {
        if ((optionIndex < 0) || (optionIndex >= Options.Count))
            return 0;

        return Options[optionIndex].PursuitId;
    }

    public override void Hide()
    {
        Visible = false;
        ClearOptions();
        SetPortrait(null);

        if (PortraitBackground is not null)
            PortraitBackground.Visible = false;
    }

    // Events
    public event Action? OnClose;
    public event Action? OnNext;
    public event Action<int>? OnOptionSelected;
    public event Action? OnPrevious;
    public event Action<string>? OnTextSubmit;

    private void RenderDialogText(string text)
    {
        if (TextImage is null || (TextRect == Rectangle.Empty))
            return;

        TextImage.Texture?.Dispose();

        TextImage.Texture = string.IsNullOrEmpty(text)
            ? null
            : TextRenderer.RenderWrappedText(
                Device,
                text,
                TextRect.Width,
                TextRect.Height,
                Color.White);

        TextImage.Visible = !string.IsNullOrEmpty(text);
    }

    /// <summary>
    ///     Sets the NPC portrait sprite texture, centered in the NPCTile rect.
    /// </summary>
    public void SetPortrait(Texture2D? texture)
    {
        if (PortraitSprite is not null)
        {
            Children.Remove(PortraitSprite);
            PortraitSprite = null;
        }

        if (texture is null || (PortraitRect == Rectangle.Empty))
            return;

        PortraitSprite = new UIImage
        {
            Name = "PortraitSprite",
            Texture = texture,
            X = PortraitRect.X + (PortraitRect.Width - texture.Width) / 2,
            Y = PortraitRect.Y + (PortraitRect.Height - texture.Height) / 2,
            Width = texture.Width,
            Height = texture.Height,
            Visible = true
        };
        AddChild(PortraitSprite);
    }

    /// <summary>
    ///     Shows an NPC dialog with text and optional Next/Previous navigation.
    /// </summary>
    public void ShowDialog(DisplayDialogArgs args)
    {
        IsMenuMode = false;
        SourceEntityType = args.EntityType;
        DialogId = args.DialogId;
        PursuitId = args.PursuitId ?? 0;
        SourceId = args.SourceId;
        ShouldIllustrate = args.ShouldIllustrate;
        PortraitSpriteId = args.Sprite;

        if (PortraitBackground is not null)
            PortraitBackground.Visible = args.ShouldIllustrate;

        NpcNameLabel?.SetText(args.Name);
        RenderDialogText(args.Text);

        // Navigation buttons
        if (NextButton is not null)
            NextButton.Visible = args.HasNextButton;

        if (PreviousButton is not null)
            PreviousButton.Visible = args.HasPreviousButton;

        // Input field for text entry dialogs
        if (InputTextBox is not null)
        {
            var hasInput = args.TextBoxLength.HasValue;
            InputTextBox.Visible = hasInput;

            if (hasInput)
            {
                InputTextBox.MaxLength = args.TextBoxLength!.Value;
                InputTextBox.Text = string.Empty;
                InputTextBox.IsFocused = true;
            }
        }

        // Dialog options
        ClearOptions();

        if (args.Options is not null)
        {
            var y = OptionsRect.Y;

            foreach (var option in args.Options)
            {
                Options.Add(
                    new OptionEntry(
                        option,
                        0,
                        new Rectangle(
                            OptionsRect.X,
                            y,
                            OptionsRect.Width,
                            OPTION_ROW_HEIGHT)));
                y += OPTION_ROW_HEIGHT;
            }
        }

        Visible = true;
    }

    /// <summary>
    ///     Shows an NPC menu with selectable options (pursuit-based).
    /// </summary>
    public void ShowMenu(DisplayMenuArgs args)
    {
        IsMenuMode = true;
        SourceEntityType = args.EntityType;
        PursuitId = args.PursuitId;
        SourceId = args.SourceId;
        ShouldIllustrate = args.ShouldIllustrate;
        PortraitSpriteId = args.Sprite;

        if (PortraitBackground is not null)
            PortraitBackground.Visible = args.ShouldIllustrate;

        NpcNameLabel?.SetText(args.Name);
        RenderDialogText(args.Text);

        if (NextButton is not null)
            NextButton.Visible = false;

        if (PreviousButton is not null)
            PreviousButton.Visible = false;

        if (InputTextBox is not null)
        {
            var hasInput = args.MenuType is MenuType.TextEntry or MenuType.TextEntryWithArgs;
            InputTextBox.Visible = hasInput;

            if (hasInput)
            {
                InputTextBox.Text = string.Empty;
                InputTextBox.IsFocused = true;
            }
        }

        ClearOptions();

        if (args.Options is not null)
        {
            var y = OptionsRect.Y;

            foreach ((var text, var pursuitId) in args.Options)
            {
                Options.Add(
                    new OptionEntry(
                        text,
                        pursuitId,
                        new Rectangle(
                            OptionsRect.X,
                            y,
                            OptionsRect.Width,
                            OPTION_ROW_HEIGHT)));
                y += OPTION_ROW_HEIGHT;
            }
        }

        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        // Option hover tracking
        HoveredOption = -1;

        if (Options.Count > 0)
        {
            for (var i = 0; i < Options.Count; i++)
            {
                var optRect = Options[i].Rect;
                var sx = ScreenX + optRect.X;
                var sy = ScreenY + optRect.Y;

                if ((input.MouseX >= sx)
                    && (input.MouseX < (sx + optRect.Width))
                    && (input.MouseY >= sy)
                    && (input.MouseY < (sy + optRect.Height)))
                {
                    HoveredOption = i;

                    break;
                }
            }
        }

        // Option selection via mouse click
        if (input.WasLeftButtonPressed && (HoveredOption >= 0))
        {
            SelectedOption = HoveredOption;
            OnOptionSelected?.Invoke(HoveredOption);
        }

        // Text submit via Enter
        if (InputTextBox is not null && InputTextBox.Visible && input.WasKeyPressed(Keys.Enter))
            OnTextSubmit?.Invoke(InputTextBox.Text);

        base.Update(gameTime, input);
    }

    private sealed class OptionEntry(string text, ushort pursuitId, Rectangle rect)
    {
        public CachedText? CachedText { get; set; }
        public ushort PursuitId { get; } = pursuitId;
        public Rectangle Rect { get; } = rect;
        public string Text { get; } = text;
    }
}