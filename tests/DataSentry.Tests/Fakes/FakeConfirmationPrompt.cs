using System.Collections.Generic;
using System.Threading.Tasks;
using DataSentry.UI.Dialogs;

namespace DataSentry.Tests.Fakes;

/// <summary>
/// The confirmation, without the message box. Answers however the test tells it to, and remembers what
/// it was asked — because the question itself is a requirement here: the user is owed the real number,
/// in words, before anything is destroyed.
/// </summary>
internal sealed class FakeConfirmationPrompt : IConfirmationPrompt
{
    private readonly bool _answer;

    /// <param name="answer">What the user says. False is the interesting one: nothing may be deleted.</param>
    public FakeConfirmationPrompt(bool answer)
    {
        _answer = answer;
    }

    /// <summary>Every question the user was asked. Empty means they were never asked at all.</summary>
    public List<string> Questions { get; } = [];

    public string? LastQuestion => Questions.Count > 0 ? Questions[^1] : null;

    public Task<bool> ConfirmAsync(string question, string detail)
    {
        Questions.Add(question);

        return Task.FromResult(_answer);
    }
}
