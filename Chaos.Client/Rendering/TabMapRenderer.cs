#region
using Chaos.Client.Data;
using Chaos.Client.Definitions;
using Chaos.Client.Models;
using Chaos.DarkAges.Definitions;
using DALib.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TileFlags = DALib.Definitions.TileFlags;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Renders the Tab map overlay (press Tab to toggle). Wall tiles shown as isometric diamonds (20x10, ~36% of full
///     56x27) with black fill and white border. Adjacent walls collapse shared borders via a 4-bit neighbor mask → 16
///     pre-baked atlas variants. Entities: yellow (player), red (monsters), green (merchants), white (aislings).
///     PageUp/PageDown to zoom. Map centered so player diamond aligns with aisling on screen.
/// </summary>
public sealed class TabMapRenderer : IDisposable
{
    // ~33% of original 56x27 tile, rounded to clean staircase (20x10)
    private const int TILE_W = 20;
    private const int TILE_H = 10;
    private const int HALF_TILE_W = 10;
    private const int HALF_TILE_H = 5;
    private const int TOP_HALF_MAX_ROW = 4;
    private const float ZOOM_MIN = 0.25f;
    private const float ZOOM_MAX = 4.0f;
    private const float ZOOM_STEP = 0.25f;
    private const float ZOOM_DEFAULT = 1.0f;

    // Neighbor mask bits — which edges have adjacent tab-map tiles
    private const int MASK_TOP_LEFT = 0x1;
    private const int MASK_TOP_RIGHT = 0x2;
    private const int MASK_BOTTOM_RIGHT = 0x4;
    private const int MASK_BOTTOM_LEFT = 0x8;

    // Atlas indices for entity diamonds (after the 16 border-collapse variants)
    private const int ATLAS_PLAYER = 16;
    private const int ATLAS_CREATURE = 17;
    private const int ATLAS_AISLING = 18;
    private const int ATLAS_MERCHANT = 19;
    private const int ATLAS_TOTAL = 20;

    // Fill is 25% transparent black; border is fully opaque white
    private static readonly Color FILL_COLOR = Color.LightGray * 0.25f;
    private static readonly Color BORDER_COLOR = Color.White;
    private static readonly Color COLOR_PLAYER = new(255, 231, 57);
    private static readonly Color COLOR_CREATURE = new(206, 0, 16);
    private static readonly Color COLOR_MERCHANT = new(0, 255, 0);
    private static readonly Color COLOR_AISLING = new(123, 166, 247);

    // Diamond row bounds for 20x10 tile (staircase: 4px at top, +4px/row, full 20px at center)
    private static readonly (int StartX, int EndX)[] DiamondRows = ComputeDiamondRows();

    // Stencil states for entity overlap masking
    private static readonly DepthStencilState StencilWrite = new()
    {
        StencilEnable = true,
        StencilFunction = CompareFunction.Always,
        StencilPass = StencilOperation.IncrementSaturation,
        ReferenceStencil = 0,
        DepthBufferEnable = false
    };

    private static readonly DepthStencilState StencilTestSingleCoverage = new()
    {
        StencilEnable = true,
        StencilFunction = CompareFunction.Equal,
        ReferenceStencil = 1,
        StencilPass = StencilOperation.Keep,
        DepthBufferEnable = false
    };

    private static readonly BlendState NoColorWrite = new()
    {
        ColorWriteChannels = ColorWriteChannels.None
    };

    private static readonly RasterizerState ScissorRasterizer = new()
    {
        ScissorTestEnable = true
    };

    private readonly byte[] SotpData;
    private AlphaTestEffect? AlphaTest;
    private Texture2D? Atlas;
    private Rectangle[] AtlasSourceRects = [];
    private (int X, int Y, byte Mask)[] Entries = [];

    // Precomputed: which tiles are walls, and their neighbor masks
    private bool[,]? IsWallTile;
    private int MapHeight;
    private int MapWidth;

    public float Zoom { get; private set; } = ZOOM_DEFAULT;

    public TabMapRenderer() => SotpData = DataContext.Tiles.SotpData;

    /// <inheritdoc />
    public void Dispose()
    {
        Atlas?.Dispose();
        Atlas = null;
        AlphaTest?.Dispose();
        AlphaTest = null;
    }

    /// <summary>
    ///     Builds a texture atlas: 16 border-collapse wall variants + 3 solid entity diamonds. All colors fully opaque —
    ///     overall transparency applied via SpriteBatch tint at draw time.
    /// </summary>
    private void BuildAtlas(GraphicsDevice device)
    {
        var atlasWidth = TILE_W * ATLAS_TOTAL;
        var pixels = new Color[atlasWidth * TILE_H];

        // 16 border-collapse variants (indices 0-15)
        for (var mask = 0; mask < 16; mask++)
        {
            var hasTopLeft = (mask & MASK_TOP_LEFT) != 0;
            var hasTopRight = (mask & MASK_TOP_RIGHT) != 0;
            var hasBottomRight = (mask & MASK_BOTTOM_RIGHT) != 0;
            var hasBottomLeft = (mask & MASK_BOTTOM_LEFT) != 0;

            var xOffset = mask * TILE_W;

            for (var row = 0; row < TILE_H; row++)
            {
                (var sx, var ex) = DiamondRows[row];
                var isTopHalf = row <= TOP_HALF_MAX_ROW;

                for (var x = sx; x <= ex; x++)
                {
                    var isLeftBorder = (x - sx) < 2;
                    var isRightBorder = (ex - x) < 2;
                    var drawBorder = false;

                    if (isLeftBorder)
                    {
                        var hasNeighbor = isTopHalf ? hasTopLeft : hasBottomLeft;

                        if (!hasNeighbor)
                            drawBorder = true;
                    }

                    if (isRightBorder)
                    {
                        var hasNeighbor = isTopHalf ? hasTopRight : hasBottomRight;

                        if (!hasNeighbor)
                            drawBorder = true;
                    }

                    pixels[row * atlasWidth + xOffset + x] = drawBorder ? BORDER_COLOR : FILL_COLOR;
                }
            }
        }

        // Entity diamonds: solid fill (indices 16-19)
        Color[] entityColors =
        [
            COLOR_PLAYER,
            COLOR_CREATURE,
            COLOR_AISLING,
            COLOR_MERCHANT
        ];

        for (var i = 0; i < entityColors.Length; i++)
        {
            var xOffset = (16 + i) * TILE_W;
            var color = entityColors[i];

            for (var row = 0; row < TILE_H; row++)
            {
                (var sx, var ex) = DiamondRows[row];

                for (var x = sx; x <= ex; x++)
                    pixels[row * atlasWidth + xOffset + x] = color;
            }
        }

        Atlas = new Texture2D(device, atlasWidth, TILE_H);
        Atlas.SetData(pixels);

        AtlasSourceRects = new Rectangle[ATLAS_TOTAL];

        for (var m = 0; m < ATLAS_TOTAL; m++)
            AtlasSourceRects[m] = new Rectangle(
                m * TILE_W,
                0,
                TILE_W,
                TILE_H);
    }

    /// <summary>
    ///     Diamond row bounds for 20x10 tile. Row 0: 4px centered, +4px/row, full 20px at rows 4-5.
    /// </summary>
    private static (int StartX, int EndX)[] ComputeDiamondRows()
    {
        var rows = new (int, int)[TILE_H];

        for (var r = 0; r < TILE_H; r++)
        {
            int expandRow;

            if (r <= TOP_HALF_MAX_ROW)
                expandRow = r;
            else
                expandRow = TILE_H - 1 - r;

            var startX = HALF_TILE_W - 2 - expandRow * 2;
            var endX = HALF_TILE_W + 1 + expandRow * 2;

            rows[r] = (Math.Max(0, startX), Math.Min(TILE_W - 1, endX));
        }

        return rows;
    }

    /// <summary>
    ///     Draws the tab map overlay. Manages its own SpriteBatch Begin/End blocks. Walls drawn normally. Entities drawn via
    ///     stencil: overlapping pixels become transparent.
    /// </summary>
    public void Draw(
        SpriteBatch spriteBatch,
        GraphicsDevice device,
        Rectangle viewport,
        int playerTileX,
        int playerTileY,
        IReadOnlyList<WorldEntity> entities,
        uint playerEntityId)
    {
        if (Atlas is null || (Entries.Length == 0))
            return;

        // Player's tile center in iso space
        var playerIsoX = (MapHeight - 1 + playerTileX - playerTileY) * HALF_TILE_W + HALF_TILE_W;
        var playerIsoY = (playerTileX + playerTileY) * HALF_TILE_H + HALF_TILE_H;

        // Offset so player's iso position maps to viewport center
        var centerX = viewport.X + viewport.Width / 2f;
        var centerY = viewport.Y + viewport.Height / 2f;
        var offsetX = centerX - playerIsoX * Zoom;
        var offsetY = centerY - playerIsoY * Zoom;

        var scaledTileW = (int)(TILE_W * Zoom);
        var scaledTileH = (int)(TILE_H * Zoom);

        // Pass 1: Draw wall tiles (normal alpha blend)
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: ScissorRasterizer);
        device.ScissorRectangle = viewport;

        foreach ((var tx, var ty, var mask) in Entries)
        {
            var isoX = (MapHeight - 1 + tx - ty) * HALF_TILE_W;
            var isoY = (tx + ty) * HALF_TILE_H;
            var screenX = (int)(offsetX + isoX * Zoom);
            var screenY = (int)(offsetY + isoY * Zoom);

            spriteBatch.Draw(
                Atlas,
                new Rectangle(
                    screenX,
                    screenY,
                    scaledTileW,
                    scaledTileH),
                AtlasSourceRects[mask],
                Color.White);
        }

        spriteBatch.End();

        // AlphaTestEffect discards transparent pixels so only diamond-shaped pixels write to stencil
        AlphaTest ??= new AlphaTestEffect(device)
        {
            VertexColorEnabled = true
        };

        var vp = device.Viewport;

        AlphaTest.Projection = Matrix.CreateOrthographicOffCenter(
            0,
            vp.Width,
            vp.Height,
            0,
            0,
            1);

        // Pass 2: Stamp entity diamonds into stencil buffer (no color output)
        // Only opaque diamond pixels increment stencil — transparent rectangle areas are discarded
        device.Clear(
            ClearOptions.Stencil,
            Color.Transparent,
            0,
            0);

        spriteBatch.Begin(
            blendState: NoColorWrite,
            depthStencilState: StencilWrite,
            samplerState: SamplerState.PointClamp,
            rasterizerState: ScissorRasterizer,
            effect: AlphaTest);
        device.ScissorRectangle = viewport;

        DrawEntityDiamonds(
            spriteBatch,
            entities,
            playerEntityId,
            offsetX,
            offsetY,
            scaledTileW,
            scaledTileH);
        spriteBatch.End();

        // Pass 3: Draw entity diamonds with color, but only where stencil == 1 (single coverage)
        // Pixels where multiple entities overlap (stencil > 1) stay transparent
        spriteBatch.Begin(
            depthStencilState: StencilTestSingleCoverage,
            samplerState: SamplerState.PointClamp,
            rasterizerState: ScissorRasterizer,
            effect: AlphaTest);
        device.ScissorRectangle = viewport;

        DrawEntityDiamonds(
            spriteBatch,
            entities,
            playerEntityId,
            offsetX,
            offsetY,
            scaledTileW,
            scaledTileH);
        spriteBatch.End();
    }

    private void DrawEntityDiamonds(
        SpriteBatch spriteBatch,
        IReadOnlyList<WorldEntity> entities,
        uint playerEntityId,
        float offsetX,
        float offsetY,
        int scaledTileW,
        int scaledTileH)
    {
        foreach (var entity in entities)
        {
            if (entity.Type == ClientEntityType.GroundItem)
                continue;

            int atlasIndex;

            if (entity.Id == playerEntityId)
                atlasIndex = ATLAS_PLAYER;
            else if (entity.Type == ClientEntityType.Aisling)
                atlasIndex = ATLAS_AISLING;
            else if (entity.CreatureType == CreatureType.Merchant)
                atlasIndex = ATLAS_MERCHANT;
            else
                atlasIndex = ATLAS_CREATURE;

            var entIsoX = (MapHeight - 1 + entity.TileX - entity.TileY) * HALF_TILE_W;
            var entIsoY = (entity.TileX + entity.TileY) * HALF_TILE_H;
            var entScreenX = (int)(offsetX + entIsoX * Zoom);
            var entScreenY = (int)(offsetY + entIsoY * Zoom);

            spriteBatch.Draw(
                Atlas,
                new Rectangle(
                    entScreenX,
                    entScreenY,
                    scaledTileW,
                    scaledTileH),
                AtlasSourceRects[atlasIndex],
                Color.White);
        }
    }

    /// <summary>
    ///     Generates the tab map data from a MapFile. Builds the atlas on first call. Only wall tiles (detected via SOTP
    ///     HasFlag) are shown. Neighbor masks precomputed for border collapse.
    /// </summary>
    public void Generate(GraphicsDevice device, MapFile mapFile)
    {
        if (Atlas is null)
            BuildAtlas(device);

        MapWidth = mapFile.Width;
        MapHeight = mapFile.Height;

        // Determine which tiles are walls via SOTP (matching ChaosAssetManager's detection)
        IsWallTile = new bool[MapWidth, MapHeight];

        for (var y = 0; y < MapHeight; y++)
            for (var x = 0; x < MapWidth; x++)
            {
                var tile = mapFile.Tiles[x, y];

                if (IsWall(tile.LeftForeground) || IsWall(tile.RightForeground))
                    IsWallTile[x, y] = true;
            }

        // Precompute entries with neighbor masks for border collapse
        var entries = new List<(int, int, byte)>();

        for (var y = 0; y < MapHeight; y++)
            for (var x = 0; x < MapWidth; x++)
            {
                if (!IsWallTile[x, y])
                    continue;

                byte mask = 0;

                // Top-left edge neighbor: (x-1, y)
                if ((x > 0) && IsWallTile[x - 1, y])
                    mask |= MASK_TOP_LEFT;

                // Top-right edge neighbor: (x, y-1)
                if ((y > 0) && IsWallTile[x, y - 1])
                    mask |= MASK_TOP_RIGHT;

                // Bottom-right edge neighbor: (x+1, y)
                if ((x < (MapWidth - 1)) && IsWallTile[x + 1, y])
                    mask |= MASK_BOTTOM_RIGHT;

                // Bottom-left edge neighbor: (x, y+1)
                if ((y < (MapHeight - 1)) && IsWallTile[x, y + 1])
                    mask |= MASK_BOTTOM_LEFT;

                entries.Add((x, y, mask));
            }

        Entries = entries.ToArray();
    }

    /// <summary>
    ///     Checks if a foreground tile index is a wall via SOTP flags. Index 0 = no tile. Otherwise index-1 into SOTP, check
    ///     HasFlag(Wall). Matches ChaosAssetManager's MapEditorRenderUtil.IsWall().
    /// </summary>
    private bool IsWall(int fgIndex)
    {
        if (fgIndex == 0)
            return false;

        var sotpIndex = fgIndex - 1;

        if (sotpIndex >= SotpData.Length)
            return false;

        return ((TileFlags)SotpData[sotpIndex]).HasFlag(TileFlags.Wall);
    }

    public void ZoomIn() => Zoom = MathF.Min(Zoom + ZOOM_STEP, ZOOM_MAX);

    public void ZoomOut() => Zoom = MathF.Max(Zoom - ZOOM_STEP, ZOOM_MIN);
}