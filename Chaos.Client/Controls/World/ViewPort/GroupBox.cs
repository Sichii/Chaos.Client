#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.ViewPort;

/// <summary>
///     Group recruitment text banner rendered as a gc_pane2.spf panel frame with centered text. Displayed above aisling
///     entities that have an active GroupBox. Always visible while text is non-empty.
/// </summary>
public sealed class GroupBox : UIPanel
{
    // gc_pane2.spf is 103x19 — wooden frame with teal interior
    public const int PANEL_WIDTH = 103;
    public const int PANEL_HEIGHT = 19;

    // TITLE rect from lgcpane.txt: (3,3)-(100,16), 97x13 text area
    private const int TITLE_X = 3;
    private const int TITLE_Y = 3;
    private const int TITLE_WIDTH = 97;
    private const int TITLE_HEIGHT = 13;

    private static readonly Color NormalFillColor = new(
        0,
        0,
        0,
        128);

    private static readonly Color HoverFillColor = new(
        0,
        0,
        120,
        90);

    private static Texture2D? NormalPanelTexture;
    private static Texture2D? HoverPanelTexture;
    private static bool PanelTexturesInitialized;

    private readonly UILabel Label;
    public bool IsHovered { get; set; }

    public uint EntityId { get; }

    public GroupBox(uint entityId)
    {
        EntityId = entityId;
        Width = PANEL_WIDTH;
        Height = PANEL_HEIGHT;

        Label = new UILabel
        {
            X = TITLE_X,
            Y = TITLE_Y,
            Width = TITLE_WIDTH,
            Height = TITLE_HEIGHT,
            PaddingLeft = 1,
            PaddingTop = 1,
            Alignment = TextAlignment.Center,
            ForegroundColor = new Color(187, 187, 187)
        };

        AddChild(Label);
    }

    /// <summary>
    ///     Loads gc_pane2.spf and fills the TITLE rect interior with semi-transparent black. The original client fills this
    ///     area with white then uses blend mode 0x79 to make it see-through with a dark tint.
    /// </summary>
    private static Texture2D? BuildPanelTexture(Color titleFill)
    {
        var source = UiRenderer.Instance!.GetSpfTexture("gc_pane2.spf");

        if (source.Width < PANEL_WIDTH || source.Height < PANEL_HEIGHT)
            return source;

        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);

        for (var y = TITLE_Y; y < TITLE_Y + TITLE_HEIGHT; y++)
            for (var x = TITLE_X; x < TITLE_X + TITLE_WIDTH; x++)
                pixels[y * source.Width + x] = titleFill;

        var texture = new Texture2D(ChaosGame.Device, source.Width, source.Height);
        texture.SetData(pixels);

        return texture;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        if (!PanelTexturesInitialized)
        {
            PanelTexturesInitialized = true;
            NormalPanelTexture = BuildPanelTexture(NormalFillColor);
            HoverPanelTexture = BuildPanelTexture(HoverFillColor);
        }

        // Draw the panel frame — swap texture based on hover state
        var panel = IsHovered ? HoverPanelTexture : NormalPanelTexture;

        if (panel is not null && !panel.IsDisposed)
            spriteBatch.Draw(panel, new Vector2(ScreenX, ScreenY), Color.White);

        // Draw children (label)
        base.Draw(spriteBatch);
    }

    /// <summary>
    ///     Updates the displayed text.
    /// </summary>
    public void UpdateText(string text) => Label.Text = text;
}