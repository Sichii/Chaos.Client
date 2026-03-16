#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     NPC dialog/menu panel using lnpcd prefab variants. Handles DisplayMenuArgs and DisplayDialogArgs from the server.
///     Displays NPC portrait, text, selectable options, and navigation buttons.
/// </summary>
public class NpcDialogControl : PrefabPanel
{
    private const float TEXT_FONT_SIZE = 0f;
    private const int OPTION_ROW_HEIGHT = 16;

    private readonly UILabel? NpcNameLabel;
    private readonly List<OptionEntry> Options = [];
    private readonly Rectangle OptionsRect;

    // NPC info
    private readonly Rectangle PortraitRect;
    private readonly Rectangle TextRect;
    private int SelectedOption = -1;

    // Dialog state
    private readonly UIImage? TextImage;

    // Current dialog/menu context
    public ushort DialogId { get; private set; }
    public ushort PursuitId { get; private set; }
    public uint? SourceId { get; private set; }

    public UIButton? CloseButton { get; }
    public UITextBox? InputTextBox { get; }
    public UIButton? NextButton { get; }
    public UIButton? OkButton { get; }
    public UIButton? PreviousButton { get; }

    public NpcDialogControl(GraphicsDevice device)
        : base(device, "lnpcd")
    {
        Name = "NpcDialog";
        Visible = false;

        var elements = AutoPopulate();

        CloseButton = elements.GetValueOrDefault("Close") as UIButton;
        NextButton = elements.GetValueOrDefault("Next") as UIButton;
        PreviousButton = elements.GetValueOrDefault("Previous") as UIButton ?? elements.GetValueOrDefault("Prev") as UIButton;
        OkButton = elements.GetValueOrDefault("OK") as UIButton;

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

        if (OkButton is not null)
            OkButton.OnClick += () => OnTextSubmit?.Invoke(InputTextBox?.Text ?? string.Empty);

        // Layout rects — try likely control names
        PortraitRect = GetRect("Portrait") != Rectangle.Empty ? GetRect("Portrait") : GetRect("NpcImage");
        TextRect = GetRect("Text") != Rectangle.Empty ? GetRect("Text") : GetRect("Talk");
        OptionsRect = GetRect("Options") != Rectangle.Empty ? GetRect("Options") : GetRect("Menu");

        NpcNameLabel = CreateLabel("Name") ?? CreateLabel("NpcName");

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
            var color = i == SelectedOption ? Color.Yellow : Color.White;

            opt.CachedText ??= new CachedText(Device);
            opt.CachedText.Update(opt.Text, TEXT_FONT_SIZE, color);
            opt.CachedText.Draw(spriteBatch, new Vector2(sx + opt.Rect.X, sy + opt.Rect.Y));
        }
    }

    public new void Hide()
    {
        Visible = false;
        ClearOptions();
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
                TEXT_FONT_SIZE,
                Color.White);

        TextImage.Visible = !string.IsNullOrEmpty(text);
    }

    /// <summary>
    ///     Shows an NPC dialog with text and optional Next/Previous navigation.
    /// </summary>
    public void ShowDialog(DisplayDialogArgs args)
    {
        DialogId = args.DialogId;
        PursuitId = args.PursuitId ?? 0;
        SourceId = args.SourceId;

        NpcNameLabel?.SetText(args.Name ?? string.Empty);
        RenderDialogText(args.Text ?? string.Empty);

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
        PursuitId = args.PursuitId;
        SourceId = args.SourceId;

        NpcNameLabel?.SetText(args.Name ?? string.Empty);
        RenderDialogText(args.Text ?? string.Empty);

        if (NextButton is not null)
            NextButton.Visible = false;

        if (PreviousButton is not null)
            PreviousButton.Visible = false;

        if (InputTextBox is not null)
            InputTextBox.Visible = false;

        ClearOptions();

        if (args.Options is not null)
        {
            var y = OptionsRect.Y;

            foreach ((var text, _) in args.Options)
            {
                Options.Add(
                    new OptionEntry(
                        text,
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

        // Option selection via mouse click
        if (input.WasLeftButtonPressed && (Options.Count > 0))
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
                    SelectedOption = i;
                    OnOptionSelected?.Invoke(i);

                    break;
                }
            }

        // Text submit via Enter
        if (InputTextBox is not null && InputTextBox.Visible && input.WasKeyPressed(Keys.Enter))
            OnTextSubmit?.Invoke(InputTextBox.Text);

        base.Update(gameTime, input);
    }

    private sealed class OptionEntry(string text, Rectangle rect)
    {
        public CachedText? CachedText { get; set; }
        public Rectangle Rect { get; } = rect;
        public string Text { get; } = text;
    }
}