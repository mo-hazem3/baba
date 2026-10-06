namespace Baba.Application.Abstractions;

/// <summary>
/// Native "open" and "save" dialogs. Only the desktop host provides this (a browser cannot give the app a real file
/// path); in the cloud edition it is simply not registered and the UI hides the buttons.
/// </summary>
public interface IFileDialogs
{
    /// <summary>Returns the chosen path, or null if the user cancelled.</summary>
    Task<string?> PickCompanyFileToOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the chosen path, or null if the user cancelled.</summary>
    Task<string?> PickCompanyFileToSaveAsync(string suggestedFileName, CancellationToken cancellationToken = default);
}
