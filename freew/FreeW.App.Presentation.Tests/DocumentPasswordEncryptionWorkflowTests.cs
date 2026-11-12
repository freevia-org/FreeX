using System.Text;
using Free.Shared.AppServices;
using Free.Shared.Opc;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// "Encrypt with Password" above the file adapter: the open prompt/retry/cancel loop, saves that
/// stay encrypted, encrypted autosave recovery copies, and the Backstage action and dialog rules.
/// </summary>
public sealed class DocumentPasswordEncryptionWorkflowTests : IDisposable
{
    private const string Password = "öpen-sesame";
    private const string SecretText = "Top secret paragraph";
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private readonly TestTemporaryDirectory _temporaryDirectory = new(nameof(DocumentPasswordEncryptionWorkflowTests));

    public void Dispose() => _temporaryDirectory.Dispose();

    [Theory]
    [InlineData(".docx")]
    [InlineData(".docm")]
    [InlineData(".dotx")]
    [InlineData(".dotm")]
    public void Open_PromptsForThePassword_AndKeepsItOnTheDocument(string extension)
    {
        var path = WriteEncrypted("secret" + extension);
        var requests = new List<DocumentPasswordRequest>();
        var workflow = new DocumentPersistenceWorkflow
        {
            RequestPassword = request =>
            {
                requests.Add(request);
                return Password;
            },
        };

        var result = workflow.Open(path, DocumentTemplateOpenMode.EditTemplate);

        Text(result.Document).Should().Contain(SecretText);
        result.Document.EncryptionPassword.Should().Be(Password);
        requests.Should().ContainSingle().Which.Should().Be(
            new DocumentPasswordRequest("secret" + extension, Attempt: 1, PreviousPasswordWasIncorrect: false));
    }

    [Fact]
    public void Open_RetriesAfterAWrongPassword_TellingTheHostWhy()
    {
        var path = WriteEncrypted("retry.docx");
        var requests = new List<DocumentPasswordRequest>();
        var answers = new Queue<string>(["nope", "still wrong", Password]);
        var workflow = new DocumentPersistenceWorkflow
        {
            RequestPassword = request =>
            {
                requests.Add(request);
                return answers.Dequeue();
            },
        };

        var result = workflow.Open(path);

        Text(result.Document).Should().Contain(SecretText);
        requests.Select(request => request.Attempt).Should().Equal(1, 2, 3);
        requests.Select(request => request.PreviousPasswordWasIncorrect).Should().Equal(false, true, true);
    }

    [Fact]
    public async Task Open_CancellingThePrompt_IsACancelNotAnError()
    {
        var path = WriteEncrypted("cancel.docx");
        var loaded = false;
        var persistence = new DocumentPersistenceWorkflow { RequestPassword = _ => null };
        var coordinator = new DocumentFileExecutionCoordinator(persistence);

        var execution = await coordinator.OpenAsync(new DocumentOpenExecutionRequest(
            path,
            SuppressRecentFiles: false,
            LoadDocumentAsync: (_, _) =>
            {
                loaded = true;
                return ValueTask.CompletedTask;
            },
            CompleteOpenAsync: (_, _, _) => ValueTask.CompletedTask));

        execution.Outcome.Should().Be(DocumentFileExecutionOutcome.Canceled);
        execution.Exception.Should().BeOfType<DocumentPasswordCanceledException>();
        loaded.Should().BeFalse();
        var feedback = FreeWDocumentFileFeedbackPlanner.PlanOpen(
            new DocumentOpenWorkflowResult(execution.Outcome, execution.OpenResult, execution.Exception), path);
        feedback.Succeeded.Should().BeFalse();
        feedback.ShouldShowError.Should().BeFalse();
    }

    [Fact]
    public void Open_WithNoPromptWired_SaysPasswordProtected_NotLegacyDoc()
    {
        var path = WriteEncrypted("unprompted.docx");

        Action act = () => new DocumentPersistenceWorkflow().Open(path);

        act.Should().Throw<DocumentPasswordRequiredException>()
            .WithMessage("*password-protected*not supported in this build*");
    }

    [Fact]
    public void Open_NeverAsksForAPassword_ForAnOrdinaryDocument()
    {
        var path = Path.Combine(_temporaryDirectory.Path, "plain.docx");
        DocxWriter.Write(Document(), path);
        var workflow = new DocumentPersistenceWorkflow
        {
            RequestPassword = _ => throw new InvalidOperationException("must not prompt"),
        };

        workflow.Open(path).Document.EncryptionPassword.Should().BeNull();
    }

    [Fact]
    public void Save_OfAnOpenedEncryptedDocument_StaysEncrypted_AndClearingThePasswordWritesPlain()
    {
        var path = WriteEncrypted("resave.docx");
        var workflow = new DocumentPersistenceWorkflow { RequestPassword = _ => Password };
        var document = workflow.Open(path).Document;
        var copyPath = Path.Combine(_temporaryDirectory.Path, "resaved.docx");
        workflow.TryResolveCurrentSaveTarget(copyPath, out var target).Should().BeTrue();

        workflow.Save(document, target);

        AssertEncryptedWithoutPlaintext(copyPath);
        Text(workflow.Open(copyPath).Document).Should().Contain(SecretText);

        EncryptDocumentDialogPlanner.Apply(document, new(EncryptDocumentDecisionKind.RemovePassword)).Should().BeTrue();
        workflow.Save(document, target);

        File.ReadAllBytes(copyPath).AsSpan(0, 2).SequenceEqual("PK"u8).Should().BeTrue();
    }

    [Fact]
    public void Autosave_OfAnEncryptedDocument_WritesAnEncryptedRecoveryCopy_ThatRecoversWithThePassword()
    {
        var document = Document();
        document.EncryptionPassword = Password;
        var store = new AutosaveSnapshotStore(_temporaryDirectory.Path);
        using var session = new FreeWAutosaveSession(
            new FreeWAutosavePorts(
                GetOriginalFilePath: () => "C:\\Docs\\Secret.docx",
                GetDisplayName: () => "Secret.docx",
                GetIsDirty: () => true,
                GetDirtyGeneration: () => 1,
                ExecuteWithDocument: write => write(document)),
            store,
            "encrypted-session");

        session.Snapshot();

        var snapshotPath = store.GetSnapshotPath("encrypted-session");
        AssertEncryptedWithoutPlaintext(snapshotPath);
        File.ReadAllText(store.GetSidecarPath("encrypted-session")).Should().NotContain(Password);

        var recovered = new DocumentPersistenceWorkflow { RequestPassword = _ => Password }
            .OpenSnapshot(snapshotPath, originalPath: null);
        Text(recovered.Document).Should().Contain(SecretText);
        recovered.Document.EncryptionPassword.Should().Be(Password);

        Action withoutPassword = () => new DocumentPersistenceWorkflow { RequestPassword = _ => null }
            .OpenSnapshot(snapshotPath, originalPath: null);
        withoutPassword.Should().Throw<DocumentPasswordCanceledException>();
    }

    [Theory]
    [InlineData("copy.rtf", true)]
    [InlineData("copy.txt", true)]
    [InlineData("copy.odt", true)]
    [InlineData("copy.docx", false)]
    [InlineData("copy.dotm", false)]
    public void Save_ToAFormatThatCannotBeEncrypted_WarnsThatThePasswordIsDropped(string fileName, bool expectWarning)
    {
        var workflow = new DocumentPersistenceWorkflow();
        var document = Document();
        workflow.TryResolveCurrentSaveTarget(Path.Combine(_temporaryDirectory.Path, fileName), out var target).Should().BeTrue();

        var withoutPassword = workflow.BuildSaveCompatibilityPlan(document, target);
        document.EncryptionPassword = Password;
        var withPassword = workflow.BuildSaveCompatibilityPlan(document, target);

        withoutPassword.Warnings.Should().NotContain(warning => warning.Kind == DocumentSaveCompatibilityWarningKind.PasswordEncryption);
        withPassword.Warnings.Any(warning => warning.Kind == DocumentSaveCompatibilityWarningKind.PasswordEncryption)
            .Should().Be(expectWarning);
        if (expectWarning)
        {
            withPassword.RequiresConfirmation.Should().BeTrue();
            withPassword.Message.Should().NotContain(Password);
        }
    }

    [Fact]
    public void NewWindowAndCloneSnapshots_KeepEncryptingTheSameFile()
    {
        var document = Document();
        document.EncryptionPassword = Password;

        new FreeWDocumentWindowPlanner().CreateNext(document, "C:\\Docs\\Secret.docx", isDirty: false)
            .Document.EncryptionPassword.Should().Be(Password);
        FreeWDocumentSnapshot.Clone(document).EncryptionPassword.Should().Be(Password);
    }

    [Fact]
    public void BackstageInfo_OffersEncryptWithPassword_OnlyWhenTheHostCanPrompt()
    {
        var document = Document();

        var withoutHost = BackstagePaneSurfacePlanner.BuildInfoPane([], null, null, null, null, document);
        var invoked = false;
        var withHost = BackstagePaneSurfacePlanner.BuildInfoPane(
            [], null, null, null, null, document, encryptWithPassword: () => invoked = true);

        withoutHost.SafetyGroups.SelectMany(group => group.Actions)
            .Should().NotContain(action => action.AutomationId == "InfoAction_EncryptWithPassword");
        var row = withHost.SafetyGroups[0].Actions.Single(action => action.AutomationId == "InfoAction_EncryptWithPassword");
        row.Label.Should().Be("Encrypt with Password");
        row.Invoke!();
        invoked.Should().BeTrue();
    }

    [Fact]
    public void BackstageInfo_DescribesWhetherThePasswordIsSet()
    {
        var document = Document();
        string Description() => BackstageInfoSafetyPanePlanner.Build(document)[0].Actions
            .Single(action => action.Kind == BackstageInfoSafetyActionKind.EncryptWithPassword).Description;

        var before = Description();
        document.EncryptionPassword = Password;
        var after = Description();

        before.Should().StartWith("Require a password");
        after.Should().StartWith("A password is required");
        after.Should().NotContain(Password);
    }

    [Fact]
    public void EncryptDialog_RequiresMatchingReentry_AndTreatsEmptyAsRemove()
    {
        var text = BackstageInfoSafetyPanePlanner.ResolveText();

        EncryptDocumentDialogPlanner.Plan("abc", "abc", text)
            .Should().Be(new EncryptDocumentDecision(EncryptDocumentDecisionKind.SetPassword, "abc"));
        EncryptDocumentDialogPlanner.Plan("abc", "abd", text)
            .Should().Be(new EncryptDocumentDecision(EncryptDocumentDecisionKind.Invalid, Error: text.EncryptDialogMismatch));
        EncryptDocumentDialogPlanner.Plan("abc", "ABC", text).Kind.Should().Be(EncryptDocumentDecisionKind.Invalid);
        EncryptDocumentDialogPlanner.Plan("", "ignored", text).Kind.Should().Be(EncryptDocumentDecisionKind.RemovePassword);
        EncryptDocumentDialogPlanner.Plan(null, null, text).Kind.Should().Be(EncryptDocumentDecisionKind.RemovePassword);
        var tooLong = new string('x', OoxmlPackageEncryption.MaxPasswordLength + 1);
        EncryptDocumentDialogPlanner.Plan(tooLong, tooLong, text).Kind.Should().Be(EncryptDocumentDecisionKind.Invalid);
        var longest = new string('x', OoxmlPackageEncryption.MaxPasswordLength);
        EncryptDocumentDialogPlanner.Plan(longest, longest, text).Kind.Should().Be(EncryptDocumentDecisionKind.SetPassword);
    }

    [Fact]
    public void EncryptDialog_Apply_ReportsWhetherTheDocumentChanged()
    {
        var document = Document();

        EncryptDocumentDialogPlanner.Apply(document, new(EncryptDocumentDecisionKind.RemovePassword)).Should().BeFalse();
        EncryptDocumentDialogPlanner.Apply(document, new(EncryptDocumentDecisionKind.SetPassword, "a")).Should().BeTrue();
        EncryptDocumentDialogPlanner.Apply(document, new(EncryptDocumentDecisionKind.SetPassword, "a")).Should().BeFalse();
        EncryptDocumentDialogPlanner.Apply(document, new(EncryptDocumentDecisionKind.Invalid, Error: "x")).Should().BeFalse();
        document.EncryptionPassword.Should().Be("a");
        EncryptDocumentDialogPlanner.Apply(document, new(EncryptDocumentDecisionKind.RemovePassword)).Should().BeTrue();
        document.EncryptionPassword.Should().BeNull();
    }

    [Fact]
    public void OpenPrompt_NamesTheFile_AndSaysWhenThePreviousPasswordWasWrong()
    {
        var text = BackstageInfoSafetyPanePlanner.ResolveText();

        EncryptDocumentDialogPlanner.FormatOpenPrompt(new("Plan.docx", 1, false), text)
            .Should().Be("Enter the password to open Plan.docx");
        EncryptDocumentDialogPlanner.FormatOpenPrompt(new("Plan.docx", 2, true), text)
            .Should().Be("The password is incorrect. Enter the password to open Plan.docx");
    }

    private string WriteEncrypted(string fileName)
    {
        var document = Document();
        document.EncryptionPassword = Password;
        var path = Path.Combine(_temporaryDirectory.Path, fileName);
        using var stream = File.Create(path);
        new DocxFileAdapter().Save(document, stream);
        return path;
    }

    private static TextDocument Document()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(SecretText));
        return document;
    }

    private static string Text(TextDocument document) =>
        string.Join('\n', document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText));

    private static void AssertEncryptedWithoutPlaintext(string path)
    {
        var bytes = File.ReadAllBytes(path);
        bytes.AsSpan(0, 8).SequenceEqual(CompoundFileSignature).Should().BeTrue();
        Encoding.ASCII.GetString(bytes).Should().NotContain("word/document.xml").And.NotContain(SecretText);
        Encoding.Unicode.GetString(bytes).Should().NotContain(Password);
        Encoding.UTF8.GetString(bytes).Should().NotContain(Password);
    }
}
