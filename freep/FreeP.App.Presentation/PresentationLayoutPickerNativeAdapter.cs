namespace FreeP.App.Compositor;

/// <summary>
/// Native operations required to project a renderer-neutral layout picker. Renderers create and
/// insert toolkit objects; shared code owns group traversal, choice ordering, and action binding.
/// </summary>
public sealed record PresentationLayoutPickerNativeBindings<TRoot, THeading, TGroup, TChoice>(
    Action<TRoot> Clear,
    Func<PresentationLayoutGroup, THeading> CreateHeading,
    Func<PresentationLayoutGroup, TGroup> CreateGroup,
    Func<PresentationLayoutChoice, TChoice> CreateChoice,
    Action<TChoice, Action> BindChoice,
    Action<TGroup, TChoice> AddChoice,
    Action<TRoot, THeading> AddHeading,
    Action<TRoot, TGroup> AddGroup);

public static class PresentationLayoutPickerNativeAdapter
{
    public static void Populate<TRoot, THeading, TGroup, TChoice>(
        PresentationLayoutPickerPlan plan,
        TRoot root,
        PresentationLayoutPickerNativeBindings<TRoot, THeading, TGroup, TChoice> bindings,
        Action<string> applyLayoutChoice)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(applyLayoutChoice);

        bindings.Clear(root);
        foreach (var groupPlan in plan.Groups)
        {
            var heading = bindings.CreateHeading(groupPlan);
            bindings.AddHeading(root, heading);

            var group = bindings.CreateGroup(groupPlan);
            foreach (var choicePlan in groupPlan.Choices)
            {
                var choice = bindings.CreateChoice(choicePlan);
                bindings.BindChoice(choice, () => applyLayoutChoice(choicePlan.LayoutId));
                bindings.AddChoice(group, choice);
            }

            bindings.AddGroup(root, group);
        }
    }
}
