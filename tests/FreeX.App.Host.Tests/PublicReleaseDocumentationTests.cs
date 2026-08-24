using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PublicReleaseDocumentationTests
{
    [Fact]
    public void Public_release_docs_are_discoverable_from_repository_indexes()
    {
        var repositoryReadme = WorkspaceFileLocator.ReadAllText("README.md");
        var docsReadme = WorkspaceFileLocator.ReadAllText("docs", "README.md");

        repositoryReadme.Should().Contain(
            "[Public-preview acceptance, rollback, and incident runbook](docs/release/public-preview-operations.md)");
        docsReadme.Should().Contain(
            "[release/public-preview-operations.md](release/public-preview-operations.md)");
        docsReadme.Should().Contain(
            "[release/public-preview-decision-record-template.md](release/public-preview-decision-record-template.md)");
        docsReadme.Should().Contain(
            "[release/public-preview-release-notes-template.md](release/public-preview-release-notes-template.md)");
    }

    [Fact]
    public void Certificate_independent_gate_links_acceptance_and_release_note_procedures()
    {
        var readiness = WorkspaceFileLocator.ReadAllText(
            "docs", "release", "public-preview-readiness.md");

        readiness.Should().Contain("[operations runbook](public-preview-operations.md)");
        readiness.Should().Contain(
            "[public-preview release-notes template](public-preview-release-notes-template.md)");
        readiness.Should().Contain("rollback owner");
        readiness.Should().Contain("triggers, incident contacts");
        readiness.Should().Contain("evidence-retention location");
    }

    [Fact]
    public void Operations_runbook_defines_clean_machine_stop_rollback_and_incident_contracts()
    {
        var operations = WorkspaceFileLocator.ReadAllText(
            "docs", "release", "public-preview-operations.md");

        operations.Should().Contain("## Roles and Stop Authority");
        operations.Should().Contain("## Clean-Machine Acceptance");
        operations.Should().Contain("### Windows matrix");
        operations.Should().Contain("### Linux matrix");
        operations.Should().Contain("### macOS matrix");
        operations.Should().Contain("## Rollback and Replacement");
        operations.Should().Contain("## Incident Procedure");
        operations.Should().Contain("## Evidence Retention");
        operations.Should().Contain("do not silently replace asset bytes");
        operations.Should().Contain("rollback is withdrawal plus a");
        operations.Should().Contain("forward-fix release");
        operations.Should().Contain("Do not disable SmartScreen, Gatekeeper, antivirus");
        operations.Should().Contain("does not define notification deadlines");
    }

    [Fact]
    public void Decision_record_captures_operator_owned_policy_and_correction_metadata()
    {
        var decision = WorkspaceFileLocator.ReadAllText(
            "docs", "release", "public-preview-decision-record-template.md");

        decision.Should().Contain("Release-notes draft location and SHA-256:");
        decision.Should().Contain("Security contact:");
        decision.Should().Contain("Privacy contact:");
        decision.Should().Contain("Dependency/license reviewer:");
        decision.Should().Contain("Evidence location, access owner, and retention policy:");
        decision.Should().Contain("Dependency-alert review result/link:");
        decision.Should().Contain("Private vulnerability-reporting route verification:");
        decision.Should().Contain("Protected public-preview environment, reviewer, and branch-policy evidence:");
        decision.Should().Contain("Sentry region");
        decision.Should().Contain("Rollback triggers and tested action:");
        decision.Should().Contain("Withdrawal/correction status URL:");
        decision.Should().Contain("not a representation that automated");
        decision.Should().Contain("guarantee non-infringement or legal compliance");
    }

    [Fact]
    public void Release_notes_template_requires_unsigned_privacy_support_and_rollback_disclosure()
    {
        var releaseNotes = WorkspaceFileLocator.ReadAllText(
            "docs", "release", "public-preview-release-notes-template.md");

        releaseNotes.Should().Contain("## Trust and Signing Status");
        releaseNotes.Should().Contain("unsigned");
        releaseNotes.Should().Contain("unnotarized");
        releaseNotes.Should().Contain("Verify SHA-256 checksums before launch");
        releaseNotes.Should().Contain("## Install, Update, Uninstall, and Rollback");
        releaseNotes.Should().Contain("## Privacy and Network Behavior");
        releaseNotes.Should().Contain("Responsible operator/contact:");
        releaseNotes.Should().Contain("Crash service region, retention, and privacy link:");
        releaseNotes.Should().Contain("## Feedback, Security, and Incident Status");
        releaseNotes.Should().Contain("Superseded or withdrawn versions:");
        releaseNotes.Should().Contain("no guaranteed support lifetime or response time");
    }

    [Fact]
    public void Public_support_and_security_policies_route_incidents_without_promising_outcomes()
    {
        var support = WorkspaceFileLocator.ReadAllText("docs", "support", "feedback.md");
        var security = WorkspaceFileLocator.ReadAllText("SECURITY.md");

        support.Should().Contain("## Release Corrections And Support Boundaries");
        support.Should().Contain("do not carry a guaranteed response time or support lifetime");
        support.Should().Contain(
            "[acceptance, rollback, and incident runbook](../release/public-preview-operations.md)");
        security.Should().Contain("## Maintainer Handling");
        security.Should().Contain(
            "[public-preview incident procedure](docs/release/public-preview-operations.md#incident-procedure)");
        security.Should().Contain("does not establish a disclosure deadline or legal notification threshold");
    }
}
