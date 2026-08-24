namespace FreeP.App.Compositor;

public static class PresentationAnimationPreviewCatalog
{
    public static string GlyphFor(string commandId) => commandId switch
    {
        "freep.anim.none" => "—",
        "freep.anim.entrance.appear" => "✦",
        "freep.anim.entrance.fade" => "✧",
        "freep.anim.entrance.fly-in" => "➜",
        "freep.anim.entrance.wipe" => "▐",
        _ => "✹",
    };

    public static bool IsNone(string commandId) => commandId == "freep.anim.none";
}
