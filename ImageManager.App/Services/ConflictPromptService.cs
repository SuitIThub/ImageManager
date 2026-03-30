using System.Windows.Forms;
using ImageManager.Core.Interfaces;

namespace ImageManager.App.Services;

public sealed class ConflictPromptService : IConflictPrompt
{
    public ConflictPromptDecision Ask(string sourcePath, string destinationPath, string extension)
    {
        var form = new Form
        {
            Width = 720,
            Height = 220,
            Text = "File conflict",
            StartPosition = FormStartPosition.CenterScreen
        };

        var label = new Label
        {
            AutoSize = false,
            Width = 680,
            Height = 80,
            Left = 10,
            Top = 10,
            Text = $"File already exists at destination.\nSource: {sourcePath}\nDestination: {destinationPath}"
        };
        form.Controls.Add(label);

        ConflictPromptDecision decision = ConflictPromptDecision.Skip;

        void AddButton(string text, int left, ConflictPromptDecision value)
        {
            var button = new Button { Text = text, Left = left, Top = 110, Width = 100, Height = 30 };
            button.Click += (_, _) =>
            {
                decision = value;
                form.DialogResult = DialogResult.OK;
                form.Close();
            };
            form.Controls.Add(button);
        }

        AddButton("Overwrite", 10, ConflictPromptDecision.Overwrite);
        AddButton("Skip", 120, ConflictPromptDecision.Skip);
        AddButton("Overwrite all", 230, ConflictPromptDecision.OverwriteAll);
        AddButton("Skip all", 340, ConflictPromptDecision.SkipAll);
        AddButton("Skip all ext", 450, ConflictPromptDecision.SkipAllForExtension);
        AddButton("Cancel", 560, ConflictPromptDecision.Cancel);

        form.ShowDialog();
        return decision;
    }
}
