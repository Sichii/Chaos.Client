#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Display-only town map overlay composited from national.dat assets. Shows the player's current location on the
///     national map background with an animated player marker. Triggered by T key or HUD button, dismissed by Escape, T,
///     or any click.
/// </summary>
public sealed class TownMap : UIPanel
{
    private const int FRAME_WIDTH = 568;
    private const int FRAME_HEIGHT = 406;
    private const float MARKER_FRAME_INTERVAL = 0.083f;

    private record struct TownMapEntry(int MapId, int X, int Y, int TileWidth, int TileHeight);

    private List<TownMapEntry>? CoordEntries;

    // UIImage children — created on Show, removed on Hide
    private UIImage? BackgroundImage;
    private UIImage? IconBarImage;
    private UIImage? TownImageLayer;
    private UIImage? NameLabelImage;
    private UIImage? MarkerImage;

    // Marker animation frames (swapped onto MarkerImage.Texture each tick)
    private Texture2D[]? MarkerFrames;

    // Marker animation + projection state
    private int MarkerFrame;
    private float MarkerTimer;
    private TownMapEntry ActiveEntry;

    // Click-to-dismiss tracking (requires down then up)
    private bool MouseDownReceived;

    public TownMap()
    {
        Width = FRAME_WIDTH;
        Height = FRAME_HEIGHT;
        X = (640 - FRAME_WIDTH) / 2;
        Y = (480 - FRAME_HEIGHT) / 2;
        Visible = false;
        ZIndex = 2;
    }

    /// <summary>
    ///     Shows the town map for the given map ID if a matching town image exists. Loads all assets from national.dat,
    ///     computes the player marker position, and sets Visible = true.
    /// </summary>
    public void Show(short mapId, int playerTileX, int playerTileY)
    {
        EnsureCoordsParsed();

        // Find matching entry
        var entryIndex = -1;

        for (var i = 0; i < CoordEntries!.Count; i++)
            if (CoordEntries[i].MapId == mapId)
            {
                entryIndex = i;

                break;
            }

        if (entryIndex < 0)
            return;

        // Check that the town SPF exists before committing to show
        if (!DatArchives.National.TryGetValue($"_t{mapId}.spf", out _))
            return;

        var entry = CoordEntries[entryIndex];

        // Clear previous children
        ClearLayers();

        // Layer 1: Background at (0, 0) — fills the panel
        BackgroundImage = LoadSpfAsImage("_t_back.spf");

        if (BackgroundImage is not null)
            AddChild(BackgroundImage);

        // Layer 2: Icon bar
        IconBarImage = LoadSpfAsImage("_t_icon.spf");

        if (IconBarImage is not null)
        {
            // Center horizontally within the frame, position in lower area
            IconBarImage.X = (FRAME_WIDTH - IconBarImage.Width) / 2;
            IconBarImage.Y = 301;
            AddChild(IconBarImage);
        }

        // Layer 3: Town image — _tcoord.txt X is negated centering offset, Y is vertical offset
        TownImageLayer = LoadSpfAsImage($"_t{mapId}.spf");

        if (TownImageLayer is not null)
        {
            TownImageLayer.X = -entry.X;
            TownImageLayer.Y = entry.Y;
            AddChild(TownImageLayer);
        }

        // Layer 4: Name label — centered horizontally, near top
        NameLabelImage = LoadSpfAsImage($"_t{mapId}n.spf");

        if (NameLabelImage is not null)
        {
            NameLabelImage.X = (FRAME_WIDTH - NameLabelImage.Width) / 2;
            NameLabelImage.Y = 24;
            AddChild(NameLabelImage);
        }

        // Layer 5: Player position marker
        ActiveEntry = entry;
        MarkerFrames = LoadEpfMarkerFrames("tmuser.epf");

        if (MarkerFrames is not null && TownImageLayer is not null)
        {
            MarkerImage = new UIImage
            {
                Texture = MarkerFrames[0],
                Width = MarkerFrames[0].Width,
                Height = MarkerFrames[0].Height
            };
            UpdateMarkerPosition(playerTileX, playerTileY);
            AddChild(MarkerImage);
        }

        MarkerFrame = 0;
        MarkerTimer = 0;
        MouseDownReceived = false;
        Visible = true;
    }

    /// <summary>
    ///     Hides the town map and disposes all loaded assets.
    /// </summary>
    public void Hide()
    {
        Visible = false;
        ClearLayers();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Hide();
        base.Dispose();
    }

    /// <inheritdoc />
    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible)
            return;

        // Dismiss on Escape or T
        if (input.WasKeyPressed(Keys.Escape) || input.WasKeyPressed(Keys.T))
        {
            Hide();

            return;
        }

        // Dismiss on mouse click (down then up, matching original client)
        if (input.WasLeftButtonPressed)
            MouseDownReceived = true;

        if (MouseDownReceived && input.WasLeftButtonReleased)
        {
            Hide();

            return;
        }

        // Update marker position to follow player
        var player = WorldState.GetPlayerEntity();

        if (player is not null)
            UpdateMarkerPosition(player.TileX, player.TileY);

        // Animate player marker
        MarkerTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;

        if (MarkerTimer >= MARKER_FRAME_INTERVAL)
        {
            MarkerTimer -= MARKER_FRAME_INTERVAL;
            MarkerFrame = (MarkerFrame + 1) % 7;

            if (MarkerImage is not null && MarkerFrames is not null)
                MarkerImage.Texture = MarkerFrames[MarkerFrame];
        }
    }

    #region Asset Loading
    private static UIImage? LoadSpfAsImage(string fileName)
    {
        var texture = UiRenderer.Instance!.GetNationalSpfTexture(fileName);

        return new UIImage
        {
            Texture = texture,
            Width = texture.Width,
            Height = texture.Height
        };
    }

    private static Texture2D[]? LoadEpfMarkerFrames(string fileName)
    {
        var frameCount = DataContext.UserControls.GetNationalEpfFrameCount(fileName);

        if (frameCount == 0)
            return null;

        var frames = new Texture2D[frameCount];

        for (var i = 0; i < frameCount; i++)
            frames[i] = UiRenderer.Instance!.GetNationalEpfTexture(fileName, i);

        return frames;
    }
    #endregion

    #region Coordinate Parsing
    private void EnsureCoordsParsed()
    {
        if (CoordEntries is not null)
            return;

        CoordEntries = [];

        if (!DatArchives.National.TryGetValue("_tcoord.txt", out var entry))
            return;

        var text = System.Text.Encoding.GetEncoding(949).GetString(entry.ToSpan());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var numbers = ExtractNumbers(line);

            if (numbers.Count >= 5)
                CoordEntries.Add(
                    new TownMapEntry(
                        numbers[0],
                        numbers[1],
                        numbers[2],
                        numbers[3],
                        numbers[4]));
        }
    }

    private static List<int> ExtractNumbers(string line)
    {
        var numbers = new List<int>();
        var i = 0;

        while (i < line.Length)
        {
            if (!char.IsDigit(line[i]) && line[i] != '-')
            {
                i++;

                continue;
            }

            var start = i;

            if (line[i] == '-')
            {
                if (i + 1 < line.Length && char.IsDigit(line[i + 1]))
                    i++;
                else
                {
                    i++;

                    continue;
                }
            }

            while (i < line.Length && char.IsDigit(line[i]))
                i++;

            if (int.TryParse(line.AsSpan(start, i - start), out var value))
                numbers.Add(value);
        }

        return numbers;
    }
    #endregion

    #region Marker Projection
    private void UpdateMarkerPosition(int playerTileX, int playerTileY)
    {
        if (MarkerImage is null || TownImageLayer is null)
            return;

        var imgX = TownImageLayer.X;
        var imgY = TownImageLayer.Y;
        var imgW = TownImageLayer.Width;
        var imgH = TownImageLayer.Height;
        var tileW = ActiveEntry.TileWidth;
        var tileH = ActiveEntry.TileHeight;
        var totalTiles = tileW + tileH;

        if (totalTiles == 0)
            return;

        var pivotX = (imgW * tileH) / totalTiles + imgX;

        var pixelX = ((imgX - 1 + imgW - pivotX) * playerTileX) / tileW
                     + pivotX
                     + ((imgX - pivotX) * playerTileY) / tileH;

        var pixelY = (((imgH * tileW) / totalTiles) * playerTileX) / tileW
                     + imgY
                     + (((imgH * tileH) / totalTiles) * playerTileY) / tileH;

        MarkerImage.X = pixelX - 6;
        MarkerImage.Y = pixelY - 19;
    }
    #endregion

    #region Cleanup
    private void ClearLayers()
    {
        // All textures are CachedTexture2D from UiRenderer — Dispose is a no-op, just detach
        BackgroundImage = null;
        IconBarImage = null;
        TownImageLayer = null;
        NameLabelImage = null;
        MarkerImage = null;
        MarkerFrames = null;

        // Null out textures before clearing so UIImage.Dispose doesn't try to release cached textures
        foreach (var child in Children)
        {
            if (child is UIImage image)
                image.Texture = null;
        }

        Children.Clear();
    }
    #endregion

}