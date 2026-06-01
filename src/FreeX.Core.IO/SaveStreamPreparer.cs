namespace FreeX.Core.IO;

internal static class SaveStreamPreparer
{
    public static void TruncateFromCurrentPosition(Stream stream)
    {
        if (!stream.CanSeek || !stream.CanWrite)
            return;

        stream.SetLength(stream.Position);
    }
}
