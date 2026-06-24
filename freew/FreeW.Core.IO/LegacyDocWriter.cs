using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Writes a <see cref="TextDocument"/> to the Word 97-2003 binary <c>.doc</c> format
/// (OLE2 Compound File Binary container + minimal FIB + Unicode text stream + CLX piece table),
/// producing a file parseable by real binary-Word readers including DocSharp (the round-trip
/// verification used in <see cref="LegacyDocFileAdapter"/>).
///
/// <para>
/// Format references:
/// <list type="bullet">
/// <item>[MS-CFB] -- Compound File Binary File Format (OLE2 container)</item>
/// <item>[MS-DOC] 2.2 -- File Information Block (FIB)</item>
/// <item>[MS-DOC] 2.3 -- CLX / Piece Table (text storage)</item>
/// <item>[MS-DOC] 2.9 -- StyleSheet (STSH)</item>
/// <item>[MS-DOC] 2.5.4 -- Font Table (STTBF of FFN)</item>
/// </list>
/// </para>
///
/// <para>
/// Verified against DocSharp.Binary.Doc 0.20.0 (manfromarce/DocSharp). The FIB FcLcb pair
/// indices used here match what DocSharp reads sequentially from FibRgFcLcb97:
///   Pair 0  = fcStshfOrig / lcbStshfOrig  (the "orig" copy -- NOT the real stylesheet)
///   Pair 1  = fcStshf     / lcbStshf      (the actual StyleSheet)
///   Pair 15 = fcSttbfFfn  / lcbSttbfFfn   (Font table)
///   Pair 33 = fcClx       / lcbClx        (CLX piece table)
/// DocSharp.StyleSheetMapping.Apply() accesses sheet.Styles[11] unconditionally, so cstd must
/// be >= 12. It also calls writeRunDefaults() which accesses FontTable.Data[rgftcStandardChpStsh[i]]
/// for i in 0..3; all four are set to 0 so FontTable.Data[0] must exist.
/// </para>
/// </summary>
internal static class LegacyDocWriter
{
    // -----------------------------------------------------------------------
    // Public entry point
    // -----------------------------------------------------------------------

    public static void Write(TextDocument document, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        string text = CollectText(document);

        byte[] wordDocBytes = BuildWordDocumentStream(text);
        byte[] tableBytes   = BuildTableStream(text);

        byte[] wordDocStream = PadTo(wordDocBytes, MiniStreamCutoff);
        byte[] tableStream   = PadTo(tableBytes,   MiniStreamCutoff);

        // Patch FIB FibRgFcLcb97 entries (DocSharp pair indices):
        //   Pair 1  = fcStshf/lcbStshf
        //   Pair 15 = fcSttbfFfn/lcbSttbfFfn
        //   Pair 33 = fcClx/lcbClx
        PatchFibFcLcb(wordDocStream, StshfFcLcbIdx,    fc: (uint)s_lastStshOffset, lcb: (uint)s_lastStshSize);
        PatchFibFcLcb(wordDocStream, SttbfFfnFcLcbIdx, fc: (uint)s_lastFfnOffset,  lcb: (uint)s_lastFfnSize);
        PatchFibFcLcb(wordDocStream, FcLcbClxIdx,      fc: (uint)s_lastClxOffset,  lcb: (uint)s_lastClxSize);

        WriteCfb(destination, wordDocStream, tableStream,
            wdLogicalSize:  (uint)MiniStreamCutoff,
            tblLogicalSize: (uint)MiniStreamCutoff);
    }

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    private const int SectorSize       = 512;
    private const int MiniStreamCutoff = 4096;

    private const uint FREESECT   = 0xFFFFFFFF;
    private const uint ENDOFCHAIN = 0xFFFFFFFE;
    private const uint FATSECT    = 0xFFFFFFFD;

    private const byte StgRoot   = 5;
    private const byte StgStream = 2;
    private const byte StgUnused = 0;

    // FIB size: 32 + (2+28) + (2+88) + (2+1488) + 2 = 1644
    private const int FibSize      = 32 + 2 + 28 + 2 + 88 + 2 + 1488 + 2; // 1644
    private const int FibFcLcbBase = 32 + 2 + 28 + 2 + 88 + 2;            // 154

    private const int StshfFcLcbIdx    = 1;   // fcStshf / lcbStshf
    private const int SttbfFfnFcLcbIdx = 15;  // fcSttbfFfn / lcbSttbfFfn
    private const int FcLcbClxIdx      = 33;  // fcClx / lcbClx

    // -----------------------------------------------------------------------
    // Text collection
    // -----------------------------------------------------------------------

    private static string CollectText(TextDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var para in doc.Paragraphs)
        {
            foreach (var run in para.Runs)
                sb.Append(run.Text);
            sb.Append('\r');
        }
        if (sb.Length == 0)
            sb.Append('\r');
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // WordDocument stream
    // -----------------------------------------------------------------------

    private static byte[] BuildWordDocumentStream(string text)
    {
        byte[] textBytes = Encoding.Unicode.GetBytes(text);
        int fcMin = FibSize;
        int fcMax = fcMin + textBytes.Length;

        using var ms = new MemoryStream(FibSize + textBytes.Length);
        using var w  = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);

        // FibBase (32 bytes)
        w.Write((ushort)0xA5EC);
        w.Write((ushort)0x00C1);   // nFib = 193 (Word97)
        w.Write((ushort)0x0000);
        w.Write((ushort)0x0409);   // lid en-US
        w.Write((ushort)0x0000);
        w.Write((ushort)((1 << 9) | (1 << 12))); // fWhichTblStm=1 ("1Table"), fExtChar=1
        w.Write((ushort)0x00BF);
        w.Write((uint)0);
        w.Write((byte)0);
        w.Write((byte)0x08);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((uint)fcMin);
        w.Write((uint)fcMax);

        // csw + rgW97 (2 + 28 = 30 bytes)
        w.Write((ushort)14);
        for (int i = 0; i < 14; i++) w.Write((ushort)0);

        // clw + rgLw97 (2 + 88 = 90 bytes): rgLw97[0] = cbMac
        w.Write((ushort)22);
        w.Write((uint)(FibSize + textBytes.Length)); // cbMac
        for (int i = 1; i < 22; i++) w.Write((uint)0);

        // cfclcb + FibRgFcLcb97 (2 + 1488 = 1490 bytes) -- patched later
        w.Write((ushort)186);
        for (int i = 0; i < 186; i++) { w.Write((uint)0); w.Write((uint)0); }

        w.Write((ushort)0); // cswNew

        w.Write(textBytes);
        return ms.ToArray();
    }

    // -----------------------------------------------------------------------
    // 1Table stream: STSH + STTBF/FFN + CLX
    // -----------------------------------------------------------------------

    private static byte[] BuildTableStream(string text)
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);

        // (a) STSH
        // cstd=12 because DocSharp accesses Styles[11] unconditionally.
        // cbStshiBytes=22 so bytes.Length > 18 and rgftcStandardChpStsh[3] is populated.
        // All four font indices = 0 (all point to font entry 0 in the font table).
        const int numStyles    = 12;
        const int cbStshiBytes = 22; // 11 ushorts

        w.Write((ushort)cbStshiBytes);
        w.Write((ushort)numStyles);
        w.Write((ushort)10);           // cbSTDBaseInFile
        w.Write((ushort)1);            // fStdStylenamesWritten
        w.Write((ushort)105);          // stiMaxWhenSaved
        w.Write((ushort)15);           // istdMaxFixedWhenSaved
        w.Write((ushort)0);            // nVerBuiltInNamesWhenSaved
        w.Write((ushort)0);            // rgftcStandardChpStsh[0]
        w.Write((ushort)0);            // rgftcStandardChpStsh[1]
        w.Write((ushort)0);            // rgftcStandardChpStsh[2]
        w.Write((ushort)0);            // rgftcStandardChpStsh[3]

        // STD slot 0 = "Normal"
        const string normalName  = "Normal";
        const int    cbNormalStd = 10 + 2 + 6 * 2 + 2 + 2 + 2; // 30 (Normal = 6 chars)

        w.Write((ushort)cbNormalStd);
        // STDFixed (10 bytes)
        w.Write((ushort)0x0000);  // sti=0
        w.Write((ushort)0xFFF1);  // stk=1 para, istdBase=0xFFF
        w.Write((ushort)0x0002);  // cupx=2, istdNext=0
        w.Write((ushort)0x0000);
        w.Write((ushort)0x0000);
        // xstzName "Normal"
        w.Write((ushort)normalName.Length);
        foreach (char c in normalName) w.Write((ushort)c);
        w.Write((ushort)0);
        // UPX[0] + UPX[1] empty
        w.Write((ushort)0);
        w.Write((ushort)0);

        // Slots 1..11 empty
        for (int i = 1; i < numStyles; i++) w.Write((ushort)0);

        int stshSize = (int)ms.Length;

        // (b) STTBF/FFN -- one font entry
        int ffnStart = stshSize;

        const string fontName     = "Times New Roman";
        byte[]       fontNameUtf16 = Encoding.Unicode.GetBytes(fontName + "\0"); // 32 bytes

        // FFN fixed = 39 bytes; name = 32 bytes; total = 71; pad to even = 72
        int payloadSize   = 1 + 2 + 1 + 1 + 10 + 24 + fontNameUtf16.Length;
        int paddedPayload = (payloadSize + 1) & ~1;
        int cchData       = paddedPayload / 2;

        w.Write((ushort)0xFFFF); // fExtend
        w.Write((ushort)1);      // cData
        w.Write((ushort)0);      // cbExtra

        w.Write((ushort)cchData);          // entry 0 length
        w.Write((byte)0x22);               // ffid
        w.Write((ushort)400);              // wWeight
        w.Write((byte)0);                  // chs
        w.Write((byte)0);                  // iBound
        for (int i = 0; i < 10; i++) w.Write((byte)0); // panose
        for (int i = 0; i < 24; i++) w.Write((byte)0); // FontSig
        w.Write(fontNameUtf16);
        for (int i = payloadSize; i < paddedPayload; i++) w.Write((byte)0);

        int ffnSize = (int)ms.Length - ffnStart;

        // (c) CLX
        int clxStart = (int)ms.Length;
        const int plcPcdSize = 2 * 4 + 1 * 8; // 16

        w.Write((byte)0x02);
        w.Write((uint)plcPcdSize);
        w.Write((uint)0);
        w.Write((uint)text.Length);
        w.Write((ushort)0);
        w.Write((uint)FibSize);  // Pcd.fc -- byte offset of text in WordDocument stream
        w.Write((ushort)0);

        int clxSize = (int)ms.Length - clxStart;

        var result = ms.ToArray();

        s_lastStshOffset = 0;
        s_lastStshSize   = stshSize;
        s_lastFfnOffset  = ffnStart;
        s_lastFfnSize    = ffnSize;
        s_lastClxOffset  = clxStart;
        s_lastClxSize    = clxSize;

        return result;
    }

    private static int s_lastStshOffset;
    private static int s_lastStshSize;
    private static int s_lastFfnOffset;
    private static int s_lastFfnSize;
    private static int s_lastClxOffset;
    private static int s_lastClxSize;

    // -----------------------------------------------------------------------
    // FIB patching
    // -----------------------------------------------------------------------

    private static void PatchFibFcLcb(byte[] buf, int idx, uint fc, uint lcb)
    {
        int off = FibFcLcbBase + idx * 8;
        WriteUInt32LE(buf, off,     fc);
        WriteUInt32LE(buf, off + 4, lcb);
    }

    private static void WriteUInt32LE(byte[] buf, int off, uint v)
    {
        buf[off]     = (byte)( v        & 0xFF);
        buf[off + 1] = (byte)((v >>  8) & 0xFF);
        buf[off + 2] = (byte)((v >> 16) & 0xFF);
        buf[off + 3] = (byte)((v >> 24) & 0xFF);
    }

    private static byte[] PadTo(byte[] src, int minSize)
    {
        if (src.Length >= minSize) return src;
        var r = new byte[minSize];
        src.CopyTo(r, 0);
        return r;
    }

    private static int CeilDiv(int n, int d) => (n + d - 1) / d;

    // -----------------------------------------------------------------------
    // CFB writer
    // -----------------------------------------------------------------------

    private static void WriteCfb(Stream dest,
        byte[] wordDocStream, byte[] tableStream,
        uint wdLogicalSize, uint tblLogicalSize)
    {
        int fatSector  = 0;
        int dirSector  = 1;
        int wdFirst    = 2;
        int wdSectors  = CeilDiv(wordDocStream.Length, SectorSize);
        int tblFirst   = wdFirst + wdSectors;
        int tblSectors = CeilDiv(tableStream.Length, SectorSize);

        uint[] fat = new uint[128];
        for (int i = 0; i < 128; i++) fat[i] = FREESECT;

        fat[fatSector] = FATSECT;
        fat[dirSector] = ENDOFCHAIN;

        for (int i = 0; i < wdSectors - 1; i++) fat[wdFirst + i] = (uint)(wdFirst + i + 1);
        if (wdSectors  > 0) fat[wdFirst  + wdSectors  - 1] = ENDOFCHAIN;

        for (int i = 0; i < tblSectors - 1; i++) fat[tblFirst + i] = (uint)(tblFirst + i + 1);
        if (tblSectors > 0) fat[tblFirst + tblSectors - 1] = ENDOFCHAIN;

        byte[] dirBytes = BuildDirectory(
            wdFirst:  (uint)wdFirst,  wdSize:  wdLogicalSize,
            tblFirst: (uint)tblFirst, tblSize: tblLogicalSize);

        using var bw = new BinaryWriter(dest, Encoding.Unicode, leaveOpen: true);
        WriteCfbHeader(bw, fatSector, dirSector);
        WriteFatSector(bw, fat);
        bw.Write(dirBytes);
        WritePadded(bw, wordDocStream);
        WritePadded(bw, tableStream);
    }

    private static void WriteCfbHeader(BinaryWriter bw, int fatSector, int dirSector)
    {
        bw.Write(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });
        bw.Write(new byte[16]);
        bw.Write((ushort)0x003E);
        bw.Write((ushort)0x0003);
        bw.Write((ushort)0xFFFE);
        bw.Write((ushort)9);
        bw.Write((ushort)6);
        bw.Write(new byte[6]);
        bw.Write((uint)0);
        bw.Write((uint)1);
        bw.Write((uint)dirSector);
        bw.Write((uint)0);
        bw.Write((uint)MiniStreamCutoff);
        bw.Write(ENDOFCHAIN);
        bw.Write((uint)0);
        bw.Write(ENDOFCHAIN);
        bw.Write((uint)0);
        bw.Write((uint)fatSector);
        for (int i = 1; i < 109; i++) bw.Write(FREESECT);
    }

    private static void WriteFatSector(BinaryWriter bw, uint[] fat)
    {
        foreach (uint v in fat) bw.Write(v);
    }

    private static byte[] BuildDirectory(uint wdFirst, uint wdSize, uint tblFirst, uint tblSize)
    {
        using var ms = new MemoryStream(SectorSize);
        using var bw = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);

        WriteDirEntry(bw, "Root Entry",   StgRoot,   0, FREESECT, FREESECT, 1,       WordDocClsid(), ENDOFCHAIN, 0);
        WriteDirEntry(bw, "WordDocument", StgStream, 1, FREESECT, 2,        FREESECT, new byte[16], wdFirst,    wdSize);
        WriteDirEntry(bw, "1Table",       StgStream, 1, FREESECT, FREESECT, FREESECT, new byte[16], tblFirst,   tblSize);
        WriteDirEntry(bw, "",             StgUnused, 0, FREESECT, FREESECT, FREESECT, new byte[16], FREESECT,   0);

        return ms.ToArray();
    }

    private static void WriteDirEntry(BinaryWriter bw,
        string name, byte type, byte color,
        uint left, uint right, uint child,
        byte[] clsid, uint start, uint size)
    {
        byte[] nameBytes = name.Length > 0 ? Encoding.Unicode.GetBytes(name) : Array.Empty<byte>();
        if (nameBytes.Length > 62) nameBytes = nameBytes[..62];
        bw.Write(nameBytes);
        for (int i = nameBytes.Length; i < 64; i++) bw.Write((byte)0);
        ushort nameLen = name.Length > 0 ? (ushort)(nameBytes.Length + 2) : (ushort)0;
        bw.Write(nameLen);
        bw.Write(type);
        bw.Write(color);
        bw.Write(left);
        bw.Write(right);
        bw.Write(child);
        bw.Write(clsid);
        for (int i = clsid.Length; i < 16; i++) bw.Write((byte)0);
        bw.Write((uint)0);
        bw.Write((ulong)0);
        bw.Write((ulong)0);
        bw.Write(start);
        bw.Write(size);
        bw.Write((uint)0);
    }

    private static byte[] WordDocClsid() =>
    [
        0x06, 0x09, 0x02, 0x00,
        0x00, 0x00,
        0x00, 0x00,
        0xC0, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x46
    ];

    private static void WritePadded(BinaryWriter bw, byte[] data)
    {
        bw.Write(data);
        int rem = data.Length % SectorSize;
        if (rem != 0)
            for (int i = 0; i < SectorSize - rem; i++) bw.Write((byte)0);
    }
}

