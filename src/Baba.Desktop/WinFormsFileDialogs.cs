using Baba.Application.Abstractions;

namespace Baba.Desktop;

/// <summary>Shows the Windows open and save dialogs on the UI thread, for the API running on another thread.</summary>
internal sealed class WinFormsFileDialogs(Form owner) : IFileDialogs
{
    private const string Filter = "Baba company (*.baba)|*.baba|All files (*.*)|*.*";

    public Task<string?> PickCompanyFileToOpenAsync(CancellationToken cancellationToken = default) =>
        OnUiThread(() =>
        {
            using var dialog = new OpenFileDialog { Filter = Filter, CheckFileExists = true, DefaultExt = "baba" };
            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        });

    public Task<string?> PickCompanyFileToSaveAsync(string suggestedFileName, CancellationToken cancellationToken = default) =>
        OnUiThread(() =>
        {
            using var dialog = new SaveFileDialog
            {
                Filter = Filter,
                DefaultExt = "baba",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = suggestedFileName,
            };
            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        });

    private Task<string?> OnUiThread(Func<string?> show)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.BeginInvoke(() =>
        {
            try
            {
                completion.SetResult(show());
            }
            catch (Exception e)
            {
                completion.SetException(e);
            }
        });
        return completion.Task;
    }
}
