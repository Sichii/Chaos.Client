#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using DALib.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Screens;

/// <summary>
///     Temporary screen that loads and renders a hardcoded map with keyboard panning. This preserves the M1/M2 test
///     behavior until real screens (Login, Game) replace it.
/// </summary>
public sealed class MapTestScreen : IScreen
{
    private const float CAMERA_SPEED = 300f;
    private Camera Camera = null!;

    private ChaosGame Game = null!;
    private MapFile? MapFile;
    private MapRenderer MapRenderer = null!;

    /// <inheritdoc />
    public UIPanel? Root => null;

    /// <inheritdoc />
    public void Dispose() { }

    /// <inheritdoc />
    public void Draw(SpriteBatch spriteBatch, GameTime gameTime)
    {
        if (MapFile is null)
            return;

        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);
        MapRenderer.Draw(spriteBatch, MapFile, Camera);
        spriteBatch.End();
    }

    /// <inheritdoc />
    public void Initialize(ChaosGame game) => Game = game;

    /// <inheritdoc />
    public void LoadContent(GraphicsDevice graphicsDevice)
    {
        Camera = new Camera(ChaosGame.VIRTUAL_WIDTH, ChaosGame.VIRTUAL_HEIGHT);
        MapRenderer = new MapRenderer();

        MapFile = DataContext.MapsFiles.GetMapFile("lod500", 70, 70);

        if (MapFile is not null)
        {
            MapRenderer.PreloadMapTiles(graphicsDevice, MapFile);

            var center = Camera.TileToWorld(MapFile.Width / 2, MapFile.Height / 2, MapFile.Height);
            Camera.Position = center;
        }
    }

    /// <inheritdoc />
    public void UnloadContent() => MapRenderer.Dispose();

    /// <inheritdoc />
    public void Update(GameTime gameTime)
    {
        var input = Game.Input;
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        if (input.WasKeyPressed(Keys.Escape))
            Game.Exit();

        var move = Vector2.Zero;

        if (input.IsKeyHeld(Keys.Left) || input.IsKeyHeld(Keys.A))
            move.X -= 1;

        if (input.IsKeyHeld(Keys.Right) || input.IsKeyHeld(Keys.D))
            move.X += 1;

        if (input.IsKeyHeld(Keys.Up) || input.IsKeyHeld(Keys.W))
            move.Y -= 1;

        if (input.IsKeyHeld(Keys.Down) || input.IsKeyHeld(Keys.S))
            move.Y += 1;

        if (move != Vector2.Zero)
        {
            move.Normalize();
            Camera.Position += move * CAMERA_SPEED * dt;
        }

        // Camera viewport is fixed at virtual resolution — no need to update
    }
}