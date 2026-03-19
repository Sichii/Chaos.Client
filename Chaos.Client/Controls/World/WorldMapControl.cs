#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Full-screen field map overlay. Displays a background image with clickable destination nodes. Shown when the server
///     sends a WorldMap (SFieldMap) packet. Nodes are positioned at screen coordinates provided by the server. Clicking a
///     node sends a world map click response. Escape closes.
/// </summary>
public sealed class WorldMapControl : UIPanel
{
    private readonly ConnectionManager Connection;
    private readonly GraphicsDevice Device;
    private readonly List<WorldMapNodeControl> NodeControls = [];
    private Texture2D? BackgroundTexture;
    private int HoveredNodeIndex = -1;

    public WorldMapControl(GraphicsDevice device, ConnectionManager connection)
    {
        Device = device;
        Connection = connection;
        Width = 640;
        Height = 480;
        X = 0;
        Y = 0;
        Visible = false;
        ZIndex = 1;
    }

    private void ClearBackground()
    {
        BackgroundTexture?.Dispose();
        BackgroundTexture = null;
    }

    private void ClearNodes()
    {
        foreach (var control in NodeControls)
            control.Dispose();

        NodeControls.Clear();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        ClearNodes();
        ClearBackground();
        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        // Draw background image
        if (BackgroundTexture is not null)
            spriteBatch.Draw(
                BackgroundTexture,
                new Rectangle(
                    0,
                    0,
                    640,
                    480),
                Color.White);

        // Draw node controls
        foreach (var control in NodeControls)
            control.Draw(spriteBatch);
    }

    public void HideMap()
    {
        Visible = false;
        ClearNodes();
        ClearBackground();
    }

    private Texture2D? LoadFieldImage(string fieldName)
    {
        using var image = DataContext.UserControls.GetFieldImage(fieldName);

        return image is not null ? TextureConverter.ToTexture2D(Device, image) : null;
    }

    public void Show(WorldMapArgs args)
    {
        ClearNodes();
        ClearBackground();

        BackgroundTexture = LoadFieldImage(args.FieldName);
        HoveredNodeIndex = -1;

        var nodes = args.Nodes.ToList();

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            var control = new WorldMapNodeControl(
                Device,
                i,
                node.Text,
                node.MapId,
                node.DestinationPoint.X,
                node.DestinationPoint.Y,
                node.CheckSum)
            {
                X = node.ScreenPosition.X,
                Y = node.ScreenPosition.Y
            };

            NodeControls.Add(control);
        }

        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible)
            return;

        // Escape to close
        if (input.WasKeyPressed(Keys.Escape))
        {
            HideMap();

            return;
        }

        // Track hover via box bounds only
        var previousHovered = HoveredNodeIndex;
        HoveredNodeIndex = -1;

        for (var i = 0; i < NodeControls.Count; i++)
            if (NodeControls[i]
                .ContainsBoxPoint(input.MouseX, input.MouseY))
            {
                HoveredNodeIndex = i;

                break;
            }

        // Update hover visuals only when changed
        if (previousHovered != HoveredNodeIndex)
        {
            if ((previousHovered >= 0) && (previousHovered < NodeControls.Count))
                NodeControls[previousHovered]
                    .SetHovered(false);

            if (HoveredNodeIndex >= 0)
                NodeControls[HoveredNodeIndex]
                    .SetHovered(true);
        }

        // Click on box — send to server, map stays visible until MapChangePending
        if (input.WasLeftButtonPressed && (HoveredNodeIndex >= 0))
        {
            var control = NodeControls[HoveredNodeIndex];

            Connection.ClickWorldMapNode(
                control.MapId,
                control.DestX,
                control.DestY,
                control.CheckSum);
        }
    }
}