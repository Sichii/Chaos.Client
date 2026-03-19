#region
using System.Text;
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Definitions;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Social status picker popup built from the lemot prefab. The prefab defines Emot0 and Emot1 as templates;
///     Emot2-Emot7 are generated following the same layout pattern (3 frames per status from emot000.epf). Hovering shows
///     frame[1] and updates the Description label. Clicking selects that status.
/// </summary>
public sealed class SocialStatusControl : PrefabPanel
{
    private const int STATUS_COUNT = 8;
    private const int FRAMES_PER_STATUS = 3;
    private const int MSG_TBL_FIRST_STATUS_LINE = 36;
    private readonly UILabel? DescriptionLabel;
    private readonly UIButton?[] StatusButtons = new UIButton?[STATUS_COUNT];
    private readonly string[] StatusNames = new string[STATUS_COUNT];
    private int HoveredIndex = -1;

    public SocialStatus CurrentStatus { get; private set; }

    public SocialStatusControl(GraphicsDevice device)
        : base(device, "lemot")
    {
        Visible = false;
        ZIndex = 2;

        // Load status names from msg.tbl
        LoadStatusNames();

        var elements = AutoPopulate();

        // Description label from prefab
        var descRect = GetRect("Description");

        if (descRect != Rectangle.Empty)
        {
            DescriptionLabel = new UILabel(device)
            {
                Name = "StatusName",
                X = descRect.X,
                Y = descRect.Y,
                Width = descRect.Width,
                Height = descRect.Height,
                Alignment = TextAlignment.Center
            };

            AddChild(DescriptionLabel);
        }

        // Wire Emot0 and Emot1 from the prefab — set hover and selected textures from prefab images
        for (var i = 0; i < STATUS_COUNT; i++)
            if (elements.TryGetValue($"Emot{i}", out var element) && element is UIButton btn)
            {
                btn.HoverTexture = btn.PressedTexture;

                // Frame[2] = selected texture (not loaded by CreateButtonFromPrefab)
                var prefab = PrefabSet[$"Emot{i}"];

                if (prefab.Images.Count > 2)
                    btn.SelectedTexture = TextureConverter.ToTexture2D(device, prefab.Images[2]);

                WireButton(btn, i);
                StatusButtons[i] = btn;
            }

        // Generate missing buttons (Emot2-Emot7) using the same layout pattern
        if (StatusButtons[0] is { } first && StatusButtons[1] is { } second)
        {
            var stride = second.X - first.X;
            var frames = DataContext.UserControls.GetEpfImages("emot000.epf");

            for (var i = 2; i < STATUS_COUNT; i++)
            {
                if (StatusButtons[i] is not null)
                    continue;

                var frameBase = i * FRAMES_PER_STATUS;

                if ((frameBase >= frames.Length) || frames[frameBase] is null)
                    continue;

                var normalTex = TextureConverter.ToTexture2D(device, frames[frameBase]);

                Texture2D? hoverTex = null;
                Texture2D? selectedTex = null;

                if (((frameBase + 1) < frames.Length) && frames[frameBase + 1] is { } hoverImg)
                    hoverTex = TextureConverter.ToTexture2D(device, hoverImg);

                if (((frameBase + 2) < frames.Length) && frames[frameBase + 2] is { } selectedImg)
                    selectedTex = TextureConverter.ToTexture2D(device, selectedImg);

                var btn = new UIButton
                {
                    Name = $"Emot{i}",
                    X = first.X + i * stride,
                    Y = first.Y,
                    Width = first.Width,
                    Height = first.Height,
                    NormalTexture = normalTex,
                    PressedTexture = hoverTex,
                    HoverTexture = hoverTex,
                    SelectedTexture = selectedTex
                };

                WireButton(btn, i);
                AddChild(btn);
                StatusButtons[i] = btn;
            }

            foreach (var img in frames)
                img?.Dispose();
        }
    }

    private void LoadStatusNames()
    {
        for (var i = 0; i < STATUS_COUNT; i++)
            StatusNames[i] = ((SocialStatus)i).ToString();

        if (!DatArchives.Setoa.TryGetValue("msg.tbl", out var entry))
            return;

        using var ms = new MemoryStream();

        using (var s = entry.ToStreamSegment())
            s.CopyTo(ms);

        var text = Encoding.GetEncoding(949)
                           .GetString(ms.ToArray());
        var lines = text.Split('\n');

        for (var i = 0; i < STATUS_COUNT; i++)
        {
            var lineIndex = MSG_TBL_FIRST_STATUS_LINE + i;

            if (lineIndex < lines.Length)
                StatusNames[i] = lines[lineIndex]
                    .TrimEnd('\r');
        }
    }

    public event Action<SocialStatus>? OnStatusSelected;

    public void Show()
    {
        UpdateSelectedState();
        DescriptionLabel?.SetText(StatusNames[(int)CurrentStatus]);
        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Visible = false;

            return;
        }

        base.Update(gameTime, input);

        // Track hover for description label
        var previousHovered = HoveredIndex;
        HoveredIndex = -1;

        for (var i = 0; i < STATUS_COUNT; i++)
            if (StatusButtons[i] is { IsHovered: true })
            {
                HoveredIndex = i;

                break;
            }

        if (HoveredIndex != previousHovered)
            DescriptionLabel?.SetText(HoveredIndex >= 0 ? StatusNames[HoveredIndex] : StatusNames[(int)CurrentStatus]);

        if (input.WasLeftButtonPressed && !ContainsPoint(input.MouseX, input.MouseY))
            Visible = false;
    }

    private void UpdateSelectedState()
    {
        for (var i = 0; i < STATUS_COUNT; i++)
            if (StatusButtons[i] is { } btn)
                btn.IsSelected = i == (int)CurrentStatus;
    }

    private void WireButton(UIButton btn, int index)
        => btn.OnClick += () =>
        {
            CurrentStatus = (SocialStatus)index;
            UpdateSelectedState();
            OnStatusSelected?.Invoke(CurrentStatus);
            Visible = false;
        };
}