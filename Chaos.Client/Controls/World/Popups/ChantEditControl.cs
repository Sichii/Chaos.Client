#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Chant line editor popup using lssbook prefab. Shows skill/spell icon, name, level, and text inputs for chant lines.
///     Skills have 1 line, spells have one per CastLine. OK saves, Cancel discards. Uses butt001.epf for buttons. MidImage
///     (sstext.epf) is tiled vertically for multi-line spells.
/// </summary>
public sealed class ChantEditControl : PrefabPanel
{
    // butt001.epf frame indices
    private const int OK_NORMAL = 15;
    private const int OK_PRESSED = 16;
    private const int CANCEL_NORMAL = 21;
    private const int CANCEL_PRESSED = 22;

    private const int TOP_HEIGHT = 89;
    private const int MID_HEIGHT = 25;
    private const int BOT_HEIGHT = 45;
    private const int PANEL_WIDTH = 246;
    private const int TEXT_X = 26;
    private const int TEXT_WIDTH = 196;
    private const int TEXT_HEIGHT = 16;
    private const int MAX_LINES = 10;
    private readonly UIImage? BotImage;
    private readonly UIButton? CancelButton;

    private readonly UIImage? Icon;
    private readonly UILabel? LevelLabel;
    private readonly UIImage? MidImage;
    private readonly UILabel? NameLabel;

    private readonly UIButton? OkButton;

    private readonly UITextBox[] TextInputs;

    private byte EditingSlot;
    private bool IsSpell;
    private int LineCount;

    public ChantEditControl()
        : base("lssbook", false)
    {
        Name = "ChantEdit";
        Visible = false;

        Width = PANEL_WIDTH;
        Height = TOP_HEIGHT + MID_HEIGHT + BOT_HEIGHT;

        // Find mid and bot images from prefab
        MidImage = CreateImage("MidImage");
        BotImage = CreateImage("BotImage");

        // Get OK/Cancel rects from prefab for positioning, then create custom buttons with butt001.epf
        var okRect = GetRect("OK");
        var cancelRect = GetRect("Cancel");

        OkButton = CreateButtonWithEpf(
            "OkBtn",
            OK_NORMAL,
            OK_PRESSED,
            okRect != Rectangle.Empty ? okRect.X : 12,
            okRect != Rectangle.Empty ? okRect.Y : 116);

        CancelButton = CreateButtonWithEpf(
            "CancelBtn",
            CANCEL_NORMAL,
            CANCEL_PRESSED,
            cancelRect != Rectangle.Empty ? cancelRect.X : 152,
            cancelRect != Rectangle.Empty ? cancelRect.Y : 116);

        if (OkButton is not null)
            OkButton.OnClick += Confirm;

        if (CancelButton is not null)
            CancelButton.OnClick += Cancel;

        NameLabel = CreateLabel("Name");
        LevelLabel = CreateLabel("Level");
        Icon = CreateImage("Icon");

        // Pre-create all text boxes — shown/hidden based on line count
        TextInputs = new UITextBox[MAX_LINES];

        for (var i = 0; i < MAX_LINES; i++)
        {
            TextInputs[i] = new UITextBox
            {
                Name = $"ChantLine{i}",
                X = TEXT_X,
                Y = TOP_HEIGHT + 2 + i * MID_HEIGHT,
                Width = TEXT_WIDTH,
                Height = TEXT_HEIGHT,
                MaxLength = 32,
                Visible = false,
                ZIndex = 1
            };

            AddChild(TextInputs[i]);
        }
    }

    private void Cancel() => Hide();

    private void Confirm()
    {
        var chants = new string[LineCount];

        for (var i = 0; i < LineCount; i++)
            chants[i] = TextInputs[i].Text;

        OnChantSet?.Invoke(EditingSlot, chants, IsSpell);
        Hide();
    }

    private UIButton? CreateButtonWithEpf(
        string name,
        int normalFrame,
        int pressedFrame,
        int x,
        int y)
    {
        var cache = UiRenderer.Instance!;
        var normalTex = cache.GetEpfTexture("butt001.epf", normalFrame);
        var pressedTex = cache.GetEpfTexture("butt001.epf", pressedFrame);

        var button = new UIButton
        {
            Name = name,
            X = x,
            Y = y,
            Width = normalTex?.Width ?? 82,
            Height = normalTex?.Height ?? 31,
            NormalTexture = normalTex,
            PressedTexture = pressedTex,
            ZIndex = 1
        };

        AddChild(button);

        return button;
    }

    public override void Dispose()
    {
        if (Icon is not null)
            Icon.Texture = null;

        base.Dispose();
    }

    public override void Hide()
    {
        Visible = false;

        if (Icon is not null)
            Icon.Texture = null;
    }

    /// <summary>
    ///     Fired when OK is pressed. Parameters: slot (1-based), chant lines array, isSpell.
    /// </summary>
    public event Action<byte, string[], bool>? OnChantSet;

    public void Show(
        byte slot,
        string name,
        string level,
        Texture2D? icon,
        string[] chants,
        int lineCount,
        bool isSpell)
    {
        EditingSlot = slot;
        IsSpell = isSpell;
        LineCount = lineCount;

        NameLabel?.SetText(name);
        LevelLabel?.SetText(level);

        if (Icon is not null)
            Icon.Texture = icon;

        // Show/hide text inputs based on line count
        for (var i = 0; i < MAX_LINES; i++)
            if (i < LineCount)
            {
                TextInputs[i].Text = i < chants.Length ? chants[i] : string.Empty;
                TextInputs[i].Visible = true;
                TextInputs[i].IsFocused = i == 0;
            } else
            {
                TextInputs[i].Text = string.Empty;
                TextInputs[i].Visible = false;
                TextInputs[i].IsFocused = false;
            }

        // Reposition mid images, bot image, and buttons for the line count
        var totalMidHeight = LineCount * MID_HEIGHT;

        if (MidImage is not null)
        {
            MidImage.Visible = LineCount > 0;
            MidImage.Height = totalMidHeight;
            MidImage.Y = TOP_HEIGHT;
        }

        var botY = TOP_HEIGHT + totalMidHeight;

        if (BotImage is not null)
            BotImage.Y = botY;

        if (OkButton is not null)
            OkButton.Y = botY + 2;

        if (CancelButton is not null)
            CancelButton.Y = botY + 2;

        Height = TOP_HEIGHT + totalMidHeight + BOT_HEIGHT;
        X = (ChaosGame.VIRTUAL_WIDTH - Width) / 2;
        Y = (ChaosGame.VIRTUAL_HEIGHT - Height) / 2;

        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Cancel();

            return;
        }

        if (input.WasKeyPressed(Keys.Enter))
        {
            Confirm();

            return;
        }

        // Tab cycles focus between text inputs
        if (input.WasKeyPressed(Keys.Tab) && (LineCount > 1))
            for (var i = 0; i < LineCount; i++)
                if (TextInputs[i].IsFocused)
                {
                    TextInputs[i].IsFocused = false;
                    TextInputs[(i + 1) % LineCount].IsFocused = true;

                    break;
                }

        base.Update(gameTime, input);
    }
}