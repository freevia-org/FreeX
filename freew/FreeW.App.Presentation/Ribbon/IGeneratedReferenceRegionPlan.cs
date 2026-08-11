using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>Toolkit-neutral delete/insert plan for a generated document region.</summary>
public interface IGeneratedReferenceRegionPlan
{
    IReadOnlyList<int> DeleteIndicesDescending { get; }
    int InsertIndex { get; }
    IReadOnlyList<Paragraph> Paragraphs { get; }
}
