using Chaos.Client.Controls.Components;

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     A single-line <see cref="UITextBox" /> that only accepts digit characters. The digit filter runs through the
///     base <see cref="UITextBox.AcceptsCharacter" /> hook, so it applies to both typed and pasted input. Used by inline
///     numeric fields such as the exchange gold box.
/// </summary>
public sealed class NumericTextBox : UITextBox
{
    protected override bool AcceptsCharacter(char c) => char.IsDigit(c);
}
