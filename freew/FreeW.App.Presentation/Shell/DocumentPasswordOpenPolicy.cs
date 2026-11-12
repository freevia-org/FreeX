using Free.Shared.Opc;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

/// <summary>
/// What a host is asked when a password-protected document is being opened.
/// <paramref name="Attempt"/> is 1 for the first prompt; <paramref name="PreviousPasswordWasIncorrect"/>
/// is true on every retry so the prompt can say why it is being shown again.
/// </summary>
public sealed record DocumentPasswordRequest(
    string FileName,
    int Attempt,
    bool PreviousPasswordWasIncorrect);

/// <summary>
/// Thrown when the user dismisses the password prompt. An <see cref="OperationCanceledException"/>,
/// so the open is reported as canceled rather than as a failure with an error dialog.
/// </summary>
public sealed class DocumentPasswordCanceledException : OperationCanceledException
{
    public DocumentPasswordCanceledException()
        : base("The password prompt was canceled.")
    {
    }
}

/// <summary>
/// Renderer-neutral open loop for "Encrypt with Password" documents: detect, ask the host for a
/// password, retry on a wrong one, stop on cancel. Passwords only ever travel from the host
/// callback into the adapter and onto the loaded <see cref="TextDocument"/>; nothing here logs or
/// stores them.
/// </summary>
public static class DocumentPasswordOpenPolicy
{
    /// <summary>Upper bound on prompts for one open, so a host callback that never cancels cannot loop forever.</summary>
    public const int MaxAttempts = 100;

    public static TextDocument Load(
        IDocumentFileAdapter adapter,
        Stream stream,
        string fileName,
        Func<DocumentPasswordRequest, string?>? requestPassword)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(stream);

        if (adapter is not IPasswordProtectedDocumentFileAdapter passwordAdapter
            || !passwordAdapter.IsPasswordProtected(stream))
        {
            return adapter.Load(stream);
        }

        // No way to ask (a host that has not wired a prompt): fail with the truthful message.
        if (requestPassword is null)
            throw new DocumentPasswordRequiredException();

        var start = stream.Position;
        var previousWasIncorrect = false;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var password = requestPassword(new DocumentPasswordRequest(fileName, attempt, previousWasIncorrect));
            if (password is null)
                throw new DocumentPasswordCanceledException();

            try
            {
                stream.Position = start;
                return passwordAdapter.Load(stream, password);
            }
            catch (OoxmlPackageEncryptionException ex) when (ex.Failure == OoxmlPackageEncryptionFailure.InvalidPassword)
            {
                previousWasIncorrect = true;
            }
        }

        throw new OoxmlPackageEncryptionException(
            OoxmlPackageEncryptionFailure.InvalidPassword,
            "The password is incorrect.");
    }
}
