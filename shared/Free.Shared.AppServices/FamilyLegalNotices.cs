namespace Free.Shared.AppServices;

/// <summary>Shared trademark and compatibility language for every Free-family desktop host.</summary>
public static class FamilyLegalNotices
{
    public const string IndependenceNotice =
        "FreeX, FreeW, and FreeP are independent projects. They are not affiliated with, authorized, sponsored, endorsed, or approved by Microsoft Corporation.";

    public const string MicrosoftTrademarkNotice =
        "Microsoft, Excel, Microsoft 365, Microsoft Office, OneDrive, PowerPoint, SharePoint, Visual Basic, Windows, and Word are trademarks of the Microsoft group of companies. All other trademarks are the property of their respective owners.";

    public const string ReferentialUseNotice =
        "Microsoft product names are used only in plain text to identify compatible file formats, interoperability, unsupported services, or reference behavior. No Microsoft logos, product icons, sounds, or trade dress are used as Free-family branding, and no Microsoft font files are redistributed with the branding assets.";

    public const string CombinedTrademarkNotice =
        IndependenceNotice + " " + MicrosoftTrademarkNotice + " " + ReferentialUseNotice;

    public const string FreeXTrademarkNotice =
        IndependenceNotice + " Microsoft and Excel are trademarks of the Microsoft group of companies. All other trademarks are the property of their respective owners. " + ReferentialUseNotice;

    public const string FreeWTrademarkNotice =
        IndependenceNotice + " Microsoft and Word are trademarks of the Microsoft group of companies. All other trademarks are the property of their respective owners. " + ReferentialUseNotice;

    public const string FreePTrademarkNotice =
        IndependenceNotice + " Microsoft and PowerPoint are trademarks of the Microsoft group of companies. All other trademarks are the property of their respective owners. " + ReferentialUseNotice;
}
