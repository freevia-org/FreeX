using FreeW.App.Presentation.Documents;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>Supplies the portable starter document to Avalonia startup and packaging smoke tests.</summary>
internal static class SampleDocument
{
    public static TextDocument Create() =>
        FreeWSampleDocumentFactory.Create(FreeWSampleDocumentProfile.FeatureShowcase);
}
