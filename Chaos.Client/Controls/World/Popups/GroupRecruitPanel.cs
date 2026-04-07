#region
using Chaos.Client.Controls.Components;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Group recruitment configuration panel using _ngcdlg1 prefab. Three states:
///     Owner-New (create a group box), Owner-Edit (modify/delete existing), Viewer (view + request to join).
/// </summary>
public sealed class GroupRecruitPanel : PrefabPanel
{
    // Class labels follow a 27px vertical spacing starting at Y=206
    private const int CLASS_LABEL_START_Y = 206;
    private const int CLASS_ROW_SPACING = 27;
    private const int CLASS_O_X = 103;
    private const int CLASS_O_WIDTH = 18;
    private const int CLASS_O_HEIGHT = 12;
    private const int CLASS_W_X = 148;
    private const int CLASS_W_WIDTH = 18;
    private const int CLASS_W_HEIGHT = 16;
    private const int NUM_CLASSES = 5;

    private static readonly string[] ClassNames =
    [
        "Warrior",
        "Wizard",
        "Rogue",
        "Priest",
        "Monk"
    ];

    // Viewer mode labels
    private readonly UILabel?[] ClassCurrentLabels = new UILabel?[NUM_CLASSES];

    // Owner mode fields
    private readonly UITextBox?[] ClassMaxFields = new UITextBox?[NUM_CLASSES];
    private readonly UILabel?[] ClassMaxLabels = new UILabel?[NUM_CLASSES];

    private string? ViewerSourceName;

    public UIButton? BeginButton { get; }
    public UIButton? CancelButton { get; }
    public UITextBox? ExtraField { get; }
    public UITextBox? MaxLevelField { get; }
    public UITextBox? MinLevelField { get; }
    public UIButton? ModifyButton { get; }
    public UIButton? QueryJoinButton { get; }
    public UIButton? ResetButton { get; }

    public UITextBox? TitleField { get; }
    public UILabel? TotalOnlineLabel { get; }
    public UILabel? TotalWantedLabel { get; }

    public GroupRecruitPanel(bool center = false)
        : base("_ngcdlg1", center)
    {
        Name = "GroupRecruit";
        Visible = false;
        UsesControlStack = true;

        // Text fields from prefab
        TitleField = CreateTextBox("TITLE", 24);
        ExtraField = CreateTextBox("EXTRA", 60);
        MinLevelField = CreateTextBox("N_LEVEL_MIN", 3);
        MaxLevelField = CreateTextBox("N_LEVEL_MAX", 3);

        if (TitleField is not null)
            TitleField.ForegroundColor = LegendColors.White;

        if (ExtraField is not null)
        {
            ExtraField.ForegroundColor = LegendColors.White;
            ExtraField.IsMultiLine = true;
            ExtraField.ClampToVisibleArea = true;
        }

        if (MinLevelField is not null)
            MinLevelField.ForegroundColor = LegendColors.White;

        if (MaxLevelField is not null)
            MaxLevelField.ForegroundColor = LegendColors.White;

        // Summary labels (viewer mode)
        TotalOnlineLabel = CreateLabel("N_TOTAL_O");
        TotalWantedLabel = CreateLabel("N_TOTAL_W");

        // Class fields — try prefab first, then create manually for missing ones
        for (var i = 0; i < NUM_CLASSES; i++)
        {
            var onlineName = $"N_CLASS{i}_O";
            var wantedName = $"N_CLASS{i}_W";
            var rowY = CLASS_LABEL_START_Y + i * CLASS_ROW_SPACING;

            // Owner mode: wanted (max) count as text box
            ClassMaxFields[i] = CreateTextBox(wantedName, 3);

            if (ClassMaxFields[i] is null)
            {
                ClassMaxFields[i] = new UITextBox
                {
                    Name = wantedName,
                    X = CLASS_W_X,
                    Y = rowY - 1,
                    Width = CLASS_W_WIDTH,
                    Height = CLASS_W_HEIGHT,
                    MaxLength = 3,
                    ForegroundColor = LegendColors.White
                };

                AddChild(ClassMaxFields[i]!);
            } else
                ClassMaxFields[i]!.ForegroundColor = LegendColors.White;

            // Viewer mode: current count label
            ClassCurrentLabels[i] = CreateLabel(onlineName);

            if (ClassCurrentLabels[i] is null)
            {
                ClassCurrentLabels[i] = new UILabel
                {
                    Name = onlineName,
                    X = CLASS_O_X,
                    Y = rowY,
                    Width = CLASS_O_WIDTH,
                    Height = CLASS_O_HEIGHT,
                    ForegroundColor = LegendColors.White
                };

                AddChild(ClassCurrentLabels[i]!);
            }

            // Viewer mode: max count label (overlays the text box position)
            ClassMaxLabels[i] = new UILabel
            {
                Name = $"CLASS{i}_MAX_LABEL",
                X = CLASS_W_X,
                Y = rowY,
                Width = CLASS_W_WIDTH,
                Height = CLASS_O_HEIGHT,
                ForegroundColor = LegendColors.White,
                Visible = false
            };

            AddChild(ClassMaxLabels[i]!);
        }

        // Buttons
        BeginButton = CreateButton("BTN_BEGIN");
        ModifyButton = CreateButton("BTN_MODIFY");
        ResetButton = CreateButton("BTN_RESET");
        CancelButton = CreateButton("BTN_CANCEL");
        QueryJoinButton = CreateButton("BTN_QUERY_JOIN");

        // Default to owner-new state: only Begin + Cancel visible
        if (ModifyButton is not null)
            ModifyButton.Visible = false;

        if (ResetButton is not null)
            ResetButton.Visible = false;

        if (QueryJoinButton is not null)
            QueryJoinButton.Visible = false;

        // Button events
        if (BeginButton is not null)
            BeginButton.Clicked += HandleCreate;

        if (ModifyButton is not null)
            ModifyButton.Clicked += HandleCreate;

        if (ResetButton is not null)
            ResetButton.Clicked += HandleReset;

        if (CancelButton is not null)
            CancelButton.Clicked += HandleCancel;

        if (QueryJoinButton is not null)
            QueryJoinButton.Clicked += HandleQueryJoin;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);
    }

    /// <summary>
    ///     Cancel just closes — does NOT delete the group box.
    /// </summary>
    private void HandleCancel()
    {
        Hide();
        OnClose?.Invoke();
    }

    private void HandleCreate()
    {
        var name = TitleField?.Text ?? string.Empty;
        var note = ExtraField?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
            return;

        OnCreateGroupBox?.Invoke(
            name,
            note,
            ParseByte(MinLevelField, 1),
            ParseByte(MaxLevelField, 99),
            ParseByte(ClassMaxFields[0]),
            ParseByte(ClassMaxFields[1]),
            ParseByte(ClassMaxFields[2]),
            ParseByte(ClassMaxFields[3]),
            ParseByte(ClassMaxFields[4]));

        Hide();
        OnClose?.Invoke();
    }

    private void HandleQueryJoin()
    {
        if (ViewerSourceName is not null)
            OnRequestJoin?.Invoke(ViewerSourceName);

        Hide();
        OnClose?.Invoke();
    }

    /// <summary>
    ///     Reset deletes the group box on the server, then closes.
    /// </summary>
    private void HandleReset()
    {
        OnRemoveGroupBox?.Invoke();
        Hide();
        OnClose?.Invoke();
    }

    public event Action? OnClose;
    public event Action<string, string, byte, byte, byte, byte, byte, byte, byte>? OnCreateGroupBox;
    public event Action? OnRemoveGroupBox;
    public event Action<string>? OnRequestJoin;

    private static byte ParseByte(UITextBox? field, byte defaultValue = 0)
    {
        if (field is null || string.IsNullOrEmpty(field.Text))
            return defaultValue;

        return byte.TryParse(field.Text, out var value) ? value : defaultValue;
    }

    private void PopulateFromGroupBoxInfo(DisplayGroupBoxInfo info)
    {
        if (TitleField is not null)
            TitleField.Text = info.Name;

        if (ExtraField is not null)
            ExtraField.Text = info.Note;

        if (MinLevelField is not null)
            MinLevelField.Text = info.MinLevel.ToString();

        if (MaxLevelField is not null)
            MaxLevelField.Text = info.MaxLevel.ToString();

        byte[] currentCounts =
        [
            info.CurrentWarriors,
            info.CurrentWizards,
            info.CurrentRogues,
            info.CurrentPriests,
            info.CurrentMonks
        ];

        byte[] maxCounts =
        [
            info.MaxWarriors,
            info.MaxWizards,
            info.MaxRogues,
            info.MaxPriests,
            info.MaxMonks
        ];

        var totalCurrent = 0;
        var totalMax = 0;

        for (var i = 0; i < NUM_CLASSES; i++)
        {
            if (ClassCurrentLabels[i] is not null)
                ClassCurrentLabels[i]!.Text = currentCounts[i]
                    .ToString();

            if (ClassMaxLabels[i] is not null)
                ClassMaxLabels[i]!.Text = maxCounts[i]
                    .ToString();

            totalCurrent += currentCounts[i];
            totalMax += maxCounts[i];
        }

        if (TotalOnlineLabel is not null)
            TotalOnlineLabel.Text = totalCurrent.ToString();

        if (TotalWantedLabel is not null)
            TotalWantedLabel.Text = totalMax.ToString();
    }

    private void ResetFields()
    {
        if (TitleField is not null)
            TitleField.Text = string.Empty;

        if (ExtraField is not null)
            ExtraField.Text = string.Empty;

        if (MinLevelField is not null)
            MinLevelField.Text = "1";

        if (MaxLevelField is not null)
            MaxLevelField.Text = "99";

        for (var i = 0; i < NUM_CLASSES; i++)
        {
            if (ClassMaxFields[i] is not null)
                ClassMaxFields[i]!.Text = "0";
        }
    }

    /// <summary>
    ///     Owner-Edit: Modify + Reset + Cancel. Has existing group box.
    /// </summary>
    private void SetOwnerEditMode()
    {
        SetOwnerFields(true);

        if (BeginButton is not null)
            BeginButton.Visible = false;

        if (ModifyButton is not null)
            ModifyButton.Visible = true;

        if (ResetButton is not null)
            ResetButton.Visible = true;

        if (CancelButton is not null)
            CancelButton.Visible = true;

        if (QueryJoinButton is not null)
            QueryJoinButton.Visible = false;
    }

    private void SetOwnerFields(bool enabled)
    {
        if (TitleField is not null)
            TitleField.Enabled = enabled;

        if (ExtraField is not null)
            ExtraField.Enabled = enabled;

        if (MinLevelField is not null)
            MinLevelField.Enabled = enabled;

        if (MaxLevelField is not null)
            MaxLevelField.Enabled = enabled;

        for (var i = 0; i < NUM_CLASSES; i++)
        {
            if (ClassMaxFields[i] is not null)
            {
                ClassMaxFields[i]!.Visible = enabled;
                ClassMaxFields[i]!.Enabled = enabled;
            }

            if (ClassCurrentLabels[i] is not null)
                ClassCurrentLabels[i]!.Visible = !enabled;

            if (ClassMaxLabels[i] is not null)
                ClassMaxLabels[i]!.Visible = !enabled;
        }

        if (TotalOnlineLabel is not null)
            TotalOnlineLabel.Visible = !enabled;

        if (TotalWantedLabel is not null)
            TotalWantedLabel.Visible = !enabled;
    }

    /// <summary>
    ///     Owner-New: Begin + Cancel. No existing group box.
    /// </summary>
    private void SetOwnerNewMode()
    {
        SetOwnerFields(true);

        if (BeginButton is not null)
            BeginButton.Visible = true;

        if (ModifyButton is not null)
            ModifyButton.Visible = false;

        if (ResetButton is not null)
            ResetButton.Visible = false;

        if (CancelButton is not null)
            CancelButton.Visible = true;

        if (QueryJoinButton is not null)
            QueryJoinButton.Visible = false;
    }

    /// <summary>
    ///     Viewer: QueryJoin + Cancel. Read-only fields.
    /// </summary>
    private void SetViewerMode()
    {
        SetOwnerFields(false);

        if (BeginButton is not null)
            BeginButton.Visible = false;

        if (ModifyButton is not null)
            ModifyButton.Visible = false;

        if (ResetButton is not null)
            ResetButton.Visible = false;

        if (CancelButton is not null)
            CancelButton.Visible = true;

        if (QueryJoinButton is not null)
            QueryJoinButton.Visible = true;
    }

    /// <summary>
    ///     Shows the panel in owner mode for creating a new group box.
    /// </summary>
    public void ShowAsOwner()
    {
        ViewerSourceName = null;

        SetOwnerNewMode();
        ResetFields();
        Show();
    }

    /// <summary>
    ///     Shows the panel in owner mode for editing an existing group box.
    /// </summary>
    public void ShowAsOwnerEdit(DisplayGroupBoxInfo info)
    {
        ViewerSourceName = null;

        SetOwnerEditMode();
        PopulateFromGroupBoxInfo(info);
        Show();
    }

    /// <summary>
    ///     Shows the panel in viewer mode populated from server GroupBox data.
    /// </summary>
    public void ShowAsViewer(string sourceName, DisplayGroupBoxInfo info)
    {
        ViewerSourceName = sourceName;

        SetViewerMode();
        PopulateFromGroupBoxInfo(info);
        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Hide();
            OnClose?.Invoke();
            e.Handled = true;
        }
    }
}