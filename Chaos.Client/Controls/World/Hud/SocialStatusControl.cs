#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Hud;

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

    public SocialStatusControl()
        : base("lemot")
    {
        Visible = false;
        ZIndex = 2;

        // Load status names from msg.tbl
        LoadStatusNames();

        // Description label from prefab
        DescriptionLabel = CreateLabel("Description", TextAlignment.Center);

        // Wire Emot buttons from the prefab — set hover and selected textures from prefab images
        var cache = UiRenderer.Instance!;

        for (var i = 0; i < STATUS_COUNT; i++)
        {
            var btn = CreateButton($"Emot{i}");

            if (btn is not null)
            {
                btn.HoverTexture = btn.PressedTexture;

                // Frame[2] = selected texture (not loaded by CreateButton)
                var prefab = PrefabSet[$"Emot{i}"];

                if (prefab.Images.Count > 2)
                    btn.SelectedTexture = cache.GetPrefabTexture(PrefabSet.Name, $"Emot{i}", 2);

                WireButton(btn, i);
                StatusButtons[i] = btn;
            }
        }

        // Generate missing buttons (Emot2-Emot7) using the same layout pattern
        if (StatusButtons[0] is { } first && StatusButtons[1] is { } second)
        {
            var stride = second.X - first.X;

            for (var i = 2; i < STATUS_COUNT; i++)
            {
                if (StatusButtons[i] is not null)
                    continue;

                var frameBase = i * FRAMES_PER_STATUS;

                var btn = new UIButton
                {
                    Name = $"Emot{i}",
                    X = first.X + i * stride,
                    Y = first.Y,
                    Width = first.Width,
                    Height = first.Height,
                    NormalTexture = cache.GetEpfTexture("emot000.epf", frameBase),
                    PressedTexture = cache.GetEpfTexture("emot000.epf", frameBase + 1),
                    HoverTexture = cache.GetEpfTexture("emot000.epf", frameBase + 1),
                    SelectedTexture = cache.GetEpfTexture("emot000.epf", frameBase + 2)
                };

                WireButton(btn, i);
                AddChild(btn);
                StatusButtons[i] = btn;
            }
        }
    }

    private void LoadStatusNames()
    {
        for (var i = 0; i < STATUS_COUNT; i++)
            StatusNames[i] = ((SocialStatus)i).ToString();

        var lines = DataContext.UserControls.GetMessageTableLines();

        if (lines is null)
            return;

        for (var i = 0; i < STATUS_COUNT; i++)
        {
            var lineIndex = MSG_TBL_FIRST_STATUS_LINE + i;

            if (lineIndex < lines.Length)
                StatusNames[i] = lines[lineIndex]
                    .TrimEnd('\r');
        }
    }

    public event Action<SocialStatus>? OnStatusSelected;

    public new void Show()
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