#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public class CharacterCreationControl : UIPanel
{
    private const int MAX_MALE_HAIR_STYLE = 18;
    private const int MAX_FEMALE_HAIR_STYLE = 17;
    private const int HAIR_COLOR_COLUMNS = 7;
    private const int HAIR_COLOR_ROWS = 2;

    // Swatch cell index → DisplayColor (row-major, top-left = cell 0)
    private static readonly DisplayColor[] SWATCH_COLOR_MAP =
    [
        DisplayColor.Teal,
        DisplayColor.Green,
        DisplayColor.Olive,
        DisplayColor.Yellow,
        DisplayColor.Pumpkin,
        DisplayColor.Apple,
        DisplayColor.Violet,
        DisplayColor.Default,
        DisplayColor.Navy,
        DisplayColor.Blue,
        DisplayColor.Gray,
        DisplayColor.Carrot,
        DisplayColor.Brown,
        DisplayColor.Black
    ];

    // Body center anchor — matches AislingRenderer's BODY_CENTER_X/Y (57/2, 85/2)
    private const int BODY_CENTER_X = 28;
    private const int BODY_CENTER_Y = 42;

    // Character preview — cycle: idle, walk1, walk2, walk3, walk4 (5 frames total)
    private const int ANIM_FRAME_COUNT = 5;
    private const float WALK_FRAME_INTERVAL_MS = 350f;
    private readonly Texture2D?[] AnimFrameTextures = new Texture2D?[ANIM_FRAME_COUNT];

    private readonly GraphicsDevice Device;
    private readonly Texture2D? FemaleSelected;
    private readonly UIElement FemaleToggleArea;
    private readonly Texture2D? FemaleUnselected;
    private readonly Texture2D? MaleSelected;

    // Gender toggle hit areas (relative to panel)
    private readonly UIElement MaleToggleArea;

    // Gender toggle images
    private readonly Texture2D? MaleUnselected;
    private readonly int PreviewHeight;
    private readonly int PreviewWidth;

    // Preview area bounds (relative to panel)
    private readonly int PreviewX;
    private readonly int PreviewY;
    private readonly AislingRenderer Renderer;
    private readonly int SwatchHeight;

    // Hair color swatch
    private readonly UIImage SwatchImage;
    private readonly int SwatchWidth;

    // Hair color swatch area bounds (relative to panel)
    private readonly int SwatchX;
    private readonly int SwatchY;
    private bool FemaleHovered;
    private bool MaleHovered;
    private bool PreviewDirty = true;
    private int WalkFrame;
    private float WalkTimer;
    public int SelectedDirection { get; private set; } = 1; // Direction index: Up=0, Right=1, Down=2, Left=3

    public Gender SelectedGender { get; private set; } = Gender.Male;
    public DisplayColor SelectedHairColor { get; private set; } = DisplayColor.Default;
    public byte SelectedHairStyle { get; private set; } = 1;
    public UIButton AngleLeftButton { get; }
    public UIButton AngleRightButton { get; }
    public UIButton CancelButton { get; }
    public UIButton HairLeftButton { get; }
    public UIButton HairRightButton { get; }

    public UITextBox NameField { get; }
    public UIButton OkButton { get; }
    public UITextBox PasswordConfirmField { get; }
    public UITextBox PasswordField { get; }

    public CharacterCreationControl(GraphicsDevice device, AislingRenderer renderer)
    {
        Device = device;
        Renderer = renderer;
        Name = "CharacterCreation";
        Visible = false;

        var prefabSet = DataContext.UserControls.Get("_ncreate");

        if (prefabSet is null)
            throw new InvalidOperationException("Failed to load _ncreate control prefab set");

        // Anchor — full screen background
        var anchor = prefabSet[0];
        var anchorRect = anchor.Control.Rect!.Value;

        Width = (int)anchorRect.Width;
        Height = (int)anchorRect.Height;
        X = 0;
        Y = 0;

        if (anchor.Images.Count > 0)
            Background = TextureConverter.ToTexture2D(device, anchor.Images[0]);

        // Character preview area (HUMAN)
        var humanPrefab = prefabSet["HUMAN"];
        var humanRect = humanPrefab.Control.Rect!.Value;
        PreviewX = (int)humanRect.Left;
        PreviewY = (int)humanRect.Top;
        PreviewWidth = (int)humanRect.Width;
        PreviewHeight = (int)humanRect.Height;

        // Gender toggle — Male
        var malePrefab = prefabSet["Male"];
        var maleRect = malePrefab.Control.Rect!.Value;

        MaleUnselected = malePrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, malePrefab.Images[0]) : null;
        MaleSelected = malePrefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, malePrefab.Images[1]) : null;

        MaleToggleArea = new UIImage
        {
            Name = "MaleToggle",
            X = (int)maleRect.Left,
            Y = (int)maleRect.Top,
            Width = (int)maleRect.Width,
            Height = (int)maleRect.Height
        };
        AddChild(MaleToggleArea);

        // Gender toggle — Female
        var femalePrefab = prefabSet["Female"];
        var femaleRect = femalePrefab.Control.Rect!.Value;

        FemaleUnselected = femalePrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, femalePrefab.Images[0]) : null;
        FemaleSelected = femalePrefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, femalePrefab.Images[1]) : null;

        FemaleToggleArea = new UIImage
        {
            Name = "FemaleToggle",
            X = (int)femaleRect.Left,
            Y = (int)femaleRect.Top,
            Width = (int)femaleRect.Width,
            Height = (int)femaleRect.Height
        };
        AddChild(FemaleToggleArea);

        // Name text field (NAME — left side)
        var namePrefab = prefabSet["NAME"];
        var nameRect = namePrefab.Control.Rect!.Value;

        NameField = new UITextBox(device)
        {
            Name = "CharName",
            X = (int)nameRect.Left,
            Y = (int)nameRect.Top,
            Width = (int)nameRect.Width,
            Height = (int)nameRect.Height,
            MaxLength = 12,
            IsMasked = false,
            IsFocused = false
        };
        NameField.OnFocused += OnTextBoxFocused;
        AddChild(NameField);

        // Password text field (PASSWD — left side)
        var passPrefab = prefabSet["PASSWD"];
        var passRect = passPrefab.Control.Rect!.Value;

        PasswordField = new UITextBox(device)
        {
            Name = "CharPassword",
            X = (int)passRect.Left,
            Y = (int)passRect.Top,
            Width = (int)passRect.Width,
            Height = (int)passRect.Height,
            MaxLength = 8,
            IsMasked = true,
            IsFocused = false
        };
        PasswordField.OnFocused += OnTextBoxFocused;
        AddChild(PasswordField);

        // Password confirm text field (PASSWD2 — left side)
        var passConfirmPrefab = prefabSet["PASSWD2"];
        var passConfirmRect = passConfirmPrefab.Control.Rect!.Value;

        PasswordConfirmField = new UITextBox(device)
        {
            Name = "CharPasswordConfirm",
            X = (int)passConfirmRect.Left,
            Y = (int)passConfirmRect.Top,
            Width = (int)passConfirmRect.Width,
            Height = (int)passConfirmRect.Height,
            MaxLength = 8,
            IsMasked = true,
            IsFocused = false
        };
        PasswordConfirmField.OnFocused += OnTextBoxFocused;
        AddChild(PasswordConfirmField);

        // OK button
        var okPrefab = prefabSet["OK"];
        var okRect = okPrefab.Control.Rect!.Value;

        OkButton = new UIButton
        {
            Name = "OK",
            X = (int)okRect.Left,
            Y = (int)okRect.Top,
            Width = (int)okRect.Width,
            Height = (int)okRect.Height,
            NormalTexture = okPrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, okPrefab.Images[0]) : null,
            PressedTexture = okPrefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, okPrefab.Images[1]) : null
        };
        OkButton.OnClick += () => OnOk?.Invoke();
        AddChild(OkButton);

        // Cancel button
        var cancelPrefab = prefabSet["Cancel"];
        var cancelRect = cancelPrefab.Control.Rect!.Value;

        CancelButton = new UIButton
        {
            Name = "Cancel",
            X = (int)cancelRect.Left,
            Y = (int)cancelRect.Top,
            Width = (int)cancelRect.Width,
            Height = (int)cancelRect.Height,
            NormalTexture = cancelPrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, cancelPrefab.Images[0]) : null,
            PressedTexture = cancelPrefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, cancelPrefab.Images[1]) : null
        };
        CancelButton.OnClick += () => OnCancel?.Invoke();
        AddChild(CancelButton);

        // Arrow buttons — [0]=normal, [1]=hover, [2]=pressed for left arrows
        //                  [0]=normal (mirrored), [1]=hover, [2]=pressed for right arrows
        AngleLeftButton = CreateArrowButton(device, prefabSet, "AngleLeft");
        AngleLeftButton.OnClick += OnAngleLeftClicked;
        AddChild(AngleLeftButton);

        AngleRightButton = CreateArrowButton(device, prefabSet, "AngleRight");
        AngleRightButton.OnClick += OnAngleRightClicked;
        AddChild(AngleRightButton);

        HairLeftButton = CreateArrowButton(device, prefabSet, "HairLeft");
        HairLeftButton.OnClick += OnHairLeftClicked;
        AddChild(HairLeftButton);

        HairRightButton = CreateArrowButton(device, prefabSet, "HairRight");
        HairRightButton.OnClick += OnHairRightClicked;
        AddChild(HairRightButton);

        // Hair color swatches (HairColor)
        var colorPrefab = prefabSet["HairColor"];
        var colorRect = colorPrefab.Control.Rect!.Value;

        SwatchX = (int)colorRect.Left;
        SwatchY = (int)colorRect.Top;
        SwatchWidth = (int)colorRect.Width;
        SwatchHeight = (int)colorRect.Height;

        SwatchImage = new UIImage
        {
            Name = "HairColorSwatch",
            X = SwatchX,
            Y = SwatchY,
            Width = SwatchWidth,
            Height = SwatchHeight,
            Texture = colorPrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, colorPrefab.Images[0]) : null
        };
        AddChild(SwatchImage);
    }

    private static UIButton CreateArrowButton(GraphicsDevice device, ControlPrefabSet prefabSet, string controlName)
    {
        var prefab = prefabSet[controlName];
        var rect = prefab.Control.Rect!.Value;

        return new UIButton
        {
            Name = controlName,
            X = (int)rect.Left,
            Y = (int)rect.Top,
            Width = (int)rect.Width,
            Height = (int)rect.Height,
            NormalTexture = prefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, prefab.Images[0]) : null,
            PressedTexture = prefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, prefab.Images[^1]) : null
        };
    }

    public override void Dispose()
    {
        for (var i = 0; i < AnimFrameTextures.Length; i++)
        {
            AnimFrameTextures[i]
                ?.Dispose();
            AnimFrameTextures[i] = null;
        }

        MaleUnselected?.Dispose();
        MaleSelected?.Dispose();
        FemaleUnselected?.Dispose();
        FemaleSelected?.Dispose();
        Renderer.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var sx = ScreenX;
        var sy = ScreenY;

        // Draw gender toggle images (hover-only for 2nd image)
        var maleTexture = MaleHovered ? MaleSelected : MaleUnselected;
        var femaleTexture = FemaleHovered ? FemaleSelected : FemaleUnselected;

        if (maleTexture is not null)
            spriteBatch.Draw(maleTexture, new Vector2(sx + MaleToggleArea.X, sy + MaleToggleArea.Y), Color.White);

        if (femaleTexture is not null)
            spriteBatch.Draw(femaleTexture, new Vector2(sx + FemaleToggleArea.X, sy + FemaleToggleArea.Y), Color.White);

        // Draw character preview centered in the HUMAN rect
        var currentFrame = AnimFrameTextures[WalkFrame];

        if (currentFrame is not null)
        {
            var centerX = sx + PreviewX + PreviewWidth / 2 - BODY_CENTER_X;
            var centerY = sy + PreviewY + PreviewHeight / 2 - BODY_CENTER_Y;
            spriteBatch.Draw(currentFrame, new Vector2(centerX, centerY), Color.White);
        }
    }

    private void HandleSwatchClick(int mouseX, int mouseY)
    {
        if (!SwatchImage.ContainsPoint(mouseX, mouseY))
            return;

        var localX = mouseX - SwatchImage.ScreenX;
        var localY = mouseY - SwatchImage.ScreenY;

        var cellWidth = SwatchWidth / HAIR_COLOR_COLUMNS;
        var cellHeight = SwatchHeight / HAIR_COLOR_ROWS;

        if ((cellWidth <= 0) || (cellHeight <= 0))
            return;

        var col = Math.Clamp(localX / cellWidth, 0, HAIR_COLOR_COLUMNS - 1);
        var row = Math.Clamp(localY / cellHeight, 0, HAIR_COLOR_ROWS - 1);
        var cellIndex = row * HAIR_COLOR_COLUMNS + col;

        if (cellIndex >= SWATCH_COLOR_MAP.Length)
            return;

        SelectedHairColor = SWATCH_COLOR_MAP[cellIndex];
        PreviewDirty = true;
    }

    public void Hide()
    {
        Visible = false;
        NameField.IsFocused = false;
        PasswordField.IsFocused = false;
        PasswordConfirmField.IsFocused = false;
        NameField.Text = string.Empty;
        PasswordField.Text = string.Empty;
        PasswordConfirmField.Text = string.Empty;
    }

    // Left button = clockwise: Down→Left→Up→Right→Down
    private void OnAngleLeftClicked()
    {
        SelectedDirection = SelectedDirection switch
        {
            0 => 1, // Up → Right
            1 => 2, // Right → Down
            2 => 3, // Down → Left
            3 => 0, // Left → Up
            _ => 2
        };

        PreviewDirty = true;
    }

    // Right button = counterclockwise: Down→Right→Up→Left→Down
    private void OnAngleRightClicked()
    {
        SelectedDirection = SelectedDirection switch
        {
            0 => 3, // Up → Left
            1 => 0, // Right → Up
            2 => 1, // Down → Right
            3 => 2, // Left → Down
            _ => 2
        };

        PreviewDirty = true;
    }

    public event Action? OnCancel;

    private void OnHairLeftClicked()
    {
        var maxStyle = SelectedGender == Gender.Male ? MAX_MALE_HAIR_STYLE : MAX_FEMALE_HAIR_STYLE;

        SelectedHairStyle = SelectedHairStyle <= 1 ? (byte)maxStyle : (byte)(SelectedHairStyle - 1);

        PreviewDirty = true;
    }

    private void OnHairRightClicked()
    {
        var maxStyle = SelectedGender == Gender.Male ? MAX_MALE_HAIR_STYLE : MAX_FEMALE_HAIR_STYLE;

        SelectedHairStyle = SelectedHairStyle >= maxStyle ? (byte)1 : (byte)(SelectedHairStyle + 1);

        PreviewDirty = true;
    }

    public event Action? OnOk;

    private void OnTextBoxFocused(UITextBox focused)
    {
        if (focused != NameField)
            NameField.IsFocused = false;

        if (focused != PasswordField)
            PasswordField.IsFocused = false;

        if (focused != PasswordConfirmField)
            PasswordConfirmField.IsFocused = false;
    }

    private void RenderWalkFrames()
    {
        for (var i = 0; i < ANIM_FRAME_COUNT; i++)
        {
            AnimFrameTextures[i]
                ?.Dispose();

            // Frame 0 = idle (walkFrame 0), frames 1-4 = walk cycle (walkFrame 1-4)
            AnimFrameTextures[i] = Renderer.RenderPreview(
                Device,
                SelectedGender,
                SelectedHairStyle,
                SelectedHairColor,
                SelectedDirection,
                i);
        }
    }

    private void SetGender(Gender gender)
    {
        if (SelectedGender == gender)
            return;

        SelectedGender = gender;

        // Clamp hair style to the valid range for the new gender
        var maxStyle = gender == Gender.Male ? MAX_MALE_HAIR_STYLE : MAX_FEMALE_HAIR_STYLE;

        if (SelectedHairStyle > maxStyle)
            SelectedHairStyle = (byte)maxStyle;

        PreviewDirty = true;
    }

    public void Show()
    {
        Visible = true;
        NameField.Text = string.Empty;
        PasswordField.Text = string.Empty;
        PasswordConfirmField.Text = string.Empty;
        SelectedGender = Gender.Male;
        SelectedHairStyle = 1;
        SelectedHairColor = DisplayColor.Default;
        SelectedDirection = 1;
        WalkFrame = 0;
        WalkTimer = 0;
        NameField.IsFocused = true;
        PasswordField.IsFocused = false;
        PasswordConfirmField.IsFocused = false;
        PreviewDirty = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Tab cycles focus: Name → Password → PasswordConfirm → Name
        if (input.WasKeyPressed(Keys.Tab))
        {
            if (NameField.IsFocused)
            {
                NameField.IsFocused = false;
                PasswordField.IsFocused = true;
            } else if (PasswordField.IsFocused)
            {
                PasswordField.IsFocused = false;
                PasswordConfirmField.IsFocused = true;
            } else
            {
                PasswordConfirmField.IsFocused = false;
                NameField.IsFocused = true;
            }
        }

        // Enter triggers OK
        if (input.WasKeyPressed(Keys.Enter))
            OkButton.PerformClick();

        // Gender toggle hover + click
        MaleHovered = MaleToggleArea.ContainsPoint(input.MouseX, input.MouseY);
        FemaleHovered = FemaleToggleArea.ContainsPoint(input.MouseX, input.MouseY);

        if (input.WasLeftButtonPressed)
        {
            if (MaleHovered)
                SetGender(Gender.Male);
            else if (FemaleHovered)
                SetGender(Gender.Female);

            // Hair color swatch click detection
            HandleSwatchClick(input.MouseX, input.MouseY);
        }

        // Pre-render all walk frames when appearance or direction changes
        if (PreviewDirty)
        {
            RenderWalkFrames();
            PreviewDirty = false;
        }

        // Advance walk animation — just cycle through pre-rendered textures
        WalkTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        if (WalkTimer >= WALK_FRAME_INTERVAL_MS)
        {
            WalkTimer -= WALK_FRAME_INTERVAL_MS;
            WalkFrame = (WalkFrame + 1) % ANIM_FRAME_COUNT;
        }

        base.Update(gameTime, input);
    }
}