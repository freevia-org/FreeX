using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeRuleDialogPlannerTests
{
    [Fact]
    public void ConditionOperators_AreExposedInWordDialogOrder()
    {
        var choices = MailMergeRuleDialogPlanner.GetConditionOperators();

        choices.Select(choice => choice.Operator).Should().Equal(
            MergeConditionOperator.Equal,
            MergeConditionOperator.NotEqual,
            MergeConditionOperator.LessThan,
            MergeConditionOperator.LessThanOrEqual,
            MergeConditionOperator.GreaterThan,
            MergeConditionOperator.GreaterThanOrEqual,
            MergeConditionOperator.IsBlank,
            MergeConditionOperator.IsNotBlank,
            MergeConditionOperator.Contains);
        choices.Select(choice => choice.Label).Should().ContainInOrder(
            "Equal to (=)",
            "Not equal to (<>)",
            "Less than (<)",
            "Less than or equal (<=)",
            "Greater than (>)",
            "Greater than or equal (>=)",
            "Is blank",
            "Is not blank",
            "Contains");
    }

    [Theory]
    [InlineData((int)MergeConditionOperator.Equal, true)]
    [InlineData((int)MergeConditionOperator.Contains, true)]
    [InlineData((int)MergeConditionOperator.IsBlank, false)]
    [InlineData((int)MergeConditionOperator.IsNotBlank, false)]
    public void IsComparisonValueEnabled_DisablesBlankOperators(int operatorValue, bool expected)
    {
        MailMergeRuleDialogPlanner.IsComparisonValueEnabled((MergeConditionOperator)operatorValue)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, MergeConditionOperator.Equal)]
    [InlineData(0, MergeConditionOperator.Equal)]
    [InlineData(6, MergeConditionOperator.IsBlank)]
    [InlineData(99, MergeConditionOperator.Equal)]
    public void GetConditionOperator_DefaultsOutOfRangeToEqual(int index, MergeConditionOperator expected)
    {
        MailMergeRuleDialogPlanner.GetConditionOperator(index).Should().Be(expected);
    }

    [Fact]
    public void CreateIfResult_NormalizesNullTextAndUsesSelectedOperator()
    {
        var result = MailMergeRuleDialogPlanner.CreateIfResult(
            fieldName: null,
            selectedOperatorIndex: 8,
            value: null,
            trueText: "yes",
            falseText: null);

        result.FieldName.Should().BeEmpty();
        result.Operator.Should().Be(MergeConditionOperator.Contains);
        result.Value.Should().BeEmpty();
        result.TrueText.Should().Be("yes");
        result.FalseText.Should().BeEmpty();
    }

    [Fact]
    public void CreateConditionResult_UsesSelectedOperator()
    {
        var result = MailMergeRuleDialogPlanner.CreateConditionResult("City", 1, "Paris");

        result.FieldName.Should().Be("City");
        result.Operator.Should().Be(MergeConditionOperator.NotEqual);
        result.Value.Should().Be("Paris");
    }

    [Fact]
    public void CreateNameValueResult_NormalizesNullText()
    {
        var result = MailMergeRuleDialogPlanner.CreateNameValueResult(null, "CustomerName");

        result.Name.Should().BeEmpty();
        result.Value.Should().Be("CustomerName");
    }

    [Fact]
    public void Condition_session_owns_initial_field_operator_state_and_acceptance()
    {
        var session = new MailMergeRuleConditionDialogSession(["City", "Region"]);

        session.InitialFieldName.Should().Be("City");
        session.ConditionOperators.Should().HaveCount(9);
        session.IsComparisonValueEnabled.Should().BeTrue();

        session.SelectOperator(6);

        session.SelectedOperator.Should().Be(MergeConditionOperator.IsBlank);
        session.IsComparisonValueEnabled.Should().BeFalse();
        session.AcceptCondition("Region", "ignored").Should().Be(
            new MailMergeRuleConditionDialogResult(
                "Region",
                MergeConditionOperator.IsBlank,
                "ignored"));
    }

    [Fact]
    public void Condition_session_normalizes_invalid_operator_and_null_acceptance_text()
    {
        var session = new MailMergeRuleConditionDialogSession(null);

        session.SelectOperator(99);
        var result = session.AcceptIf(null, null, "yes", null);

        session.SelectedOperatorIndex.Should().Be(0);
        result.Should().Be(new MailMergeRuleIfDialogResult(
            string.Empty,
            MergeConditionOperator.Equal,
            string.Empty,
            "yes",
            string.Empty));
    }

    [Fact]
    public void Name_value_session_rejects_blank_names_and_normalizes_accepted_names()
    {
        var session = new MailMergeRuleNameValueDialogSession();

        session.Accept("  ", "value").Should().BeNull();
        session.Accept("  CustomerCode  ", null).Should().Be(
            new MailMergeRuleNameValueDialogResult("CustomerCode", string.Empty));
    }

    [Theory]
    [InlineData(MailMergeRuleKind.IfThenElse, typeof(MailMergeRuleIfDialogRequest))]
    [InlineData(MailMergeRuleKind.SkipRecordIf, typeof(MailMergeRuleConditionDialogRequest))]
    [InlineData(MailMergeRuleKind.NextRecordIf, typeof(MailMergeRuleConditionDialogRequest))]
    [InlineData(MailMergeRuleKind.FillIn, typeof(MailMergeRulePromptDialogRequest))]
    [InlineData(MailMergeRuleKind.Ask, typeof(MailMergeRuleNameValueDialogRequest))]
    [InlineData(MailMergeRuleKind.Set, typeof(MailMergeRuleNameValueDialogRequest))]
    [InlineData(MailMergeRuleKind.Ref, typeof(MailMergeRulePromptDialogRequest))]
    public void CreateRequest_ProjectsTypedLocalizedMetadata(MailMergeRuleKind kind, Type requestType)
    {
        var request = MailMergeRuleDialogPlanner.CreateRequest(
            kind,
            ["City", "Region"],
            key => $"localized:{key}");

        request.Should().BeOfType(requestType);
        request.Kind.Should().Be(kind);
        request.Title.Should().StartWith("localized:MailMerge_Rule_");
        if (request is MailMergeRuleIfDialogRequest ifRequest)
            ifRequest.FieldNames.Should().Equal("City", "Region");
        if (request is MailMergeRuleConditionDialogRequest conditionRequest)
            conditionRequest.FieldNames.Should().Equal("City", "Region");
    }

    [Fact]
    public async Task AuthoringWorkflow_CancelDoesNotInsert()
    {
        var inserted = new List<MailMergeFieldInsertionPlan>();
        var request = MailMergeRuleDialogPlanner.CreateRequest(MailMergeRuleKind.FillIn);

        var execution = await MailMergeRuleAuthoringWorkflow.RunAsync(
            request,
            (_, _) => ValueTask.FromResult<MailMergeRuleDialogResponse?>(null),
            (plan, _) =>
            {
                inserted.Add(plan);
                return ValueTask.CompletedTask;
            });

        execution.Should().Be(MailMergeRuleAuthoringExecution.Cancelled);
        inserted.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MailMergeRuleKind.IfThenElse, "IF")]
    [InlineData(MailMergeRuleKind.SkipRecordIf, "SKIPIF")]
    [InlineData(MailMergeRuleKind.NextRecordIf, "NEXTIF")]
    [InlineData(MailMergeRuleKind.FillIn, "FILLIN")]
    [InlineData(MailMergeRuleKind.Ask, "ASK")]
    [InlineData(MailMergeRuleKind.Set, "SET")]
    [InlineData(MailMergeRuleKind.Ref, "REF")]
    public async Task AuthoringWorkflow_AcceptRoutesEveryRuleToOneInsertion(
        MailMergeRuleKind kind,
        string expectedKeyword)
    {
        var inserted = new List<MailMergeFieldInsertionPlan>();
        var request = MailMergeRuleDialogPlanner.CreateRequest(kind, ["City"]);

        var execution = await MailMergeRuleAuthoringWorkflow.RunAsync(
            request,
            (typedRequest, _) => ValueTask.FromResult<MailMergeRuleDialogResponse?>(ResponseFor(typedRequest)),
            (plan, _) =>
            {
                inserted.Add(plan);
                return ValueTask.CompletedTask;
            });

        execution.WasAccepted.Should().BeTrue();
        execution.InsertionPlan.Should().BeSameAs(inserted.Should().ContainSingle().Subject);
        execution.InsertionPlan!.Field.Keyword.Should().Be(expectedKeyword);
    }

    [Fact]
    public async Task AuthoringWorkflow_RejectsResponseForDifferentRequestShape()
    {
        var request = MailMergeRuleDialogPlanner.CreateRequest(MailMergeRuleKind.FillIn);
        var act = async () => await MailMergeRuleAuthoringWorkflow.RunAsync(
            request,
            (_, _) => ValueTask.FromResult<MailMergeRuleDialogResponse?>(
                new MailMergeRuleConditionDialogResponse(
                    new MailMergeRuleConditionDialogResult("City", MergeConditionOperator.Equal, "London"))),
            (_, _) => ValueTask.CompletedTask);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static MailMergeRuleDialogResponse ResponseFor(MailMergeRuleDialogRequest request) => request switch
    {
        MailMergeRuleIfDialogRequest => new MailMergeRuleIfDialogResponse(
            new MailMergeRuleIfDialogResult("City", MergeConditionOperator.Equal, "London", "Local", "Remote")),
        MailMergeRuleConditionDialogRequest => new MailMergeRuleConditionDialogResponse(
            new MailMergeRuleConditionDialogResult("City", MergeConditionOperator.Equal, "London")),
        MailMergeRulePromptDialogRequest => new MailMergeRulePromptDialogResponse("CustomerCode"),
        MailMergeRuleNameValueDialogRequest => new MailMergeRuleNameValueDialogResponse(
            new MailMergeRuleNameValueDialogResult("CustomerCode", "Enter code")),
        _ => throw new ArgumentOutOfRangeException(nameof(request)),
    };
}
