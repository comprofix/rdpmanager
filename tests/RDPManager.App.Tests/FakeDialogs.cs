using RDPManager.App.Services;

namespace RDPManager.App.Tests;

/// <summary>Records dialog requests and returns scripted answers.</summary>
internal sealed class FakeDialogs : IDialogService
{
    /// <summary>Answer to Yes/No questions.</summary>
    public bool Answer { get; set; }

    /// <summary>Answer to "Save changes?".</summary>
    public SaveChoice SaveAnswer { get; set; } = SaveChoice.Cancel;

    public List<string> Questions { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> SavePrompts { get; } = [];

    public bool Confirm(string title, string message)
    {
        Questions.Add(message);
        return Answer;
    }

    public void ShowError(string title, string message) => Errors.Add(message);

    public SaveChoice AskSaveChanges(string title, string message)
    {
        SavePrompts.Add(message);
        return SaveAnswer;
    }
}
