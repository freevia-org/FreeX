using System;
using System.IO;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Writes a <see cref="TextDocument"/> to the Word 97-2003 binary <c>.doc</c> format
/// (OLE2 Compound File Binary container + FIB + STSH + STTBF/FFN + CLX + FKP/BTE + SED).
/// The output is verified to round-trip through DocSharp.Binary.Doc 0.20.0.
///
/// <para>
/// Format references:
/// <list type="bullet">
/// <item>[MS-CFB] -- Compound File Binary File Format (OLE2 container)</item>
/// <item>[MS-DOC] -- Word (.doc) Binary File Format</item>
/// </list>
/// </para>
///
/// <para>
/// Layout of the WordDocument stream we generate:
///   [0 .. FibSize-1]         FIB (File Information Block)
///   [FibSize .. fcMac-1]     Unicode text (UTF-16LE, 2 bytes/char)
///   [SepxOffset .. SepxOffset+1] SEPX (2 bytes: cbSepx=2, no sprms)
///   [FkpBase .. FkpBase+511] PAPX FKP page (512 bytes)
///   [FkpBase+512 .. FkpBase+1023] CHPX FKP page (512 bytes)
/// Total >= 4096 bytes so stream lives in the regular FAT (no mini-stream).
///
/// Layout of the 1Table stream:
///   [0 .. stshEnd)           STSH (stylesheet)
///   [stshEnd .. ffnEnd)      STTBF/FFN (font table, one entry)
///   [ffnEnd .. clxEnd)       CLX (piece table)
///   [clxEnd .. papBteEnd)    PlcBtePapx (BTE paragraph FKP table)
///   [papBteEnd .. chpBteEnd) PlcBteChpx (BTE character FKP table)
///   [chpBteEnd .. sedEnd)    PlcfSed (section plex)
/// </para>
/// </summary>
internal sealed class LegacyDocWriter
{
    // -----------------------------------------------------------------------
    // Public entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="document"/> to <paramref name="destination"/> as a Word 97-2003 (.doc) file.
    /// </summary>
    /// <remarks>
    /// Each call gets its OWN writer instance. The offsets below are handed between the build
    /// steps as instance state, so two documents saved at the same time cannot overwrite each
    /// other's stream positions -- when they could, the loser wrote a FIB whose fcMac sat before
    /// its fcMin and the resulting file would not open at all.
    /// </remarks>
    public static void Write(TextDocument document, Stream destination) =>
        new LegacyDocWriter().WriteDocument(document, destination);

    private void WriteDocument(TextDocument document, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        string text = CollectText(document);

        // Build all stream content
        byte[] wordDocBytes = BuildWordDocumentStream(text);
        byte[] tableBytes   = BuildTableStream(text, wordDocBytes.Length);

        // Pad to >= 4096 so they live in regular FAT sectors rather than the mini stream, and
        // round up to a whole number of sectors so the declared size and the FAT chain agree.
        byte[] wordDocStream = PadToSector(wordDocBytes, MiniStreamCutoff);
        byte[] tableStream   = PadToSector(tableBytes,   MiniStreamCutoff);

        // Patch FIB FibRgFcLcb97 with the table-stream offsets computed during Build:
        PatchFibFcLcb(wordDocStream, StshfFcLcbIdx,      s_stshOffset,    s_stshSize);
        PatchFibFcLcb(wordDocStream, SttbfFfnFcLcbIdx,   s_ffnOffset,     s_ffnSize);
        PatchFibFcLcb(wordDocStream, ClxFcLcbIdx,        s_clxOffset,     s_clxSize);
        PatchFibFcLcb(wordDocStream, PlcfBtePapxFcLcbIdx,s_papBteOffset,  s_papBteSize);
        PatchFibFcLcb(wordDocStream, PlcfBteChpxFcLcbIdx,s_chpBteOffset,  s_chpBteSize);
        PatchFibFcLcb(wordDocStream, PlcfSedFcLcbIdx,    s_sedOffset,     s_sedSize);

        // Declare each stream's REAL size. This used to hardcode MiniStreamCutoff, so every
        // document whose WordDocument stream exceeded 4096 bytes -- roughly anything past 500
        // characters, i.e. nearly every real document -- shipped a directory entry claiming 8
        // sectors while the FAT chain ran the full length. Word and every other reader rejects
        // that outright, so .doc export produced unopenable files and only the tiny documents
        // in the test suite padded up to exactly 4096 and stayed self-consistent.
        WriteCfb(destination, wordDocStream, tableStream,
            (uint)wordDocStream.Length, (uint)tableStream.Length);
    }

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    private const int SectorSize       = 512;
    private const int MiniStreamCutoff = 4096;
    private const int FatEntriesPerSector = SectorSize / 4;
    private const int HeaderDifatEntries  = 109;

    private const uint FREESECT   = 0xFFFFFFFF;
    private const uint ENDOFCHAIN = 0xFFFFFFFE;
    private const uint FATSECT    = 0xFFFFFFFD;

    private const byte StgRoot   = 5;
    private const byte StgStream = 2;
    private const byte StgUnused = 0;

    // FIB size [MS-DOC 2.2]:
    //   FibBase(32) + csw(2)+rgW(28) + clw(2)+rgLw(88) + cfclcb(2)+FibRgFcLcb97(186*8) + cswNew(2)
    //   = 32 + 30 + 90 + 1490 + 2 = 1644
    private const int FibSize      = 32 + 2 + 28 + 2 + 88 + 2 + 1488 + 2; // 1644
    private const int FibFcLcbBase = 32 + 2 + 28 + 2 + 88 + 2;            // 154

    // DocSharp-verified FibRgFcLcb97 pair indices (0-based):
    private const int StshfFcLcbIdx       = 1;   // fcStshf / lcbStshf
    private const int PlcfSedFcLcbIdx     = 6;   // fcPlcfSed / lcbPlcfSed
    private const int PlcfBteChpxFcLcbIdx = 12;  // fcPlcfBteChpx / lcbPlcfBteChpx
    private const int PlcfBtePapxFcLcbIdx = 13;  // fcPlcfBtePapx / lcbPlcfBtePapx
    private const int SttbfFfnFcLcbIdx    = 15;  // fcSttbfFfn / lcbSttbfFfn
    private const int ClxFcLcbIdx         = 33;  // fcClx / lcbClx

    // -----------------------------------------------------------------------
    // Per-document state handed between the build steps below. INSTANCE, not static: a static
    // here made two concurrent saves interleave and overwrite each other's offsets.
    // -----------------------------------------------------------------------

    // Table stream offsets (set by BuildTableStream, used by Write to patch FIB)
    private uint s_stshOffset,   s_stshSize;
    private uint s_ffnOffset,    s_ffnSize;
    private uint s_clxOffset,    s_clxSize;
    private uint s_papBteOffset, s_papBteSize;
    private uint s_chpBteOffset, s_chpBteSize;
    private uint s_sedOffset,    s_sedSize;

    // WordDocument stream positions (set by BuildWordDocumentStream, read by BuildTableStream)
    private int s_fcMin;     // byte offset of text start
    private int s_fcMac;     // byte offset past last text char
    private int s_sepxFc;    // byte offset of SEPX in WordDocument stream
    private int s_papFkpPn;  // FKP page number for PAPX (page * 512 = byte offset)
    private int s_chpFkpPn;  // FKP page number for CHPX

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
            sb.Append('\r'); // CR = paragraph mark in Word binary
        }
        if (sb.Length == 0)
            sb.Append('\r');
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // WordDocument stream
    // -----------------------------------------------------------------------
    // Layout:
    //   [0..FibSize)           FIB (File Information Block)
    //   [FibSize..fcMac)       Unicode text (UTF-16LE)
    //   [SepxPage*512..+1]     SEPX (2 bytes in same page as text or next page)
    //   [PapFkpPage*512..+511] PAPX FKP (512 bytes)
    //   [ChpFkpPage*512..+511] CHPX FKP (512 bytes)

    private byte[] BuildWordDocumentStream(string text)
    {
        byte[] textBytes = Encoding.Unicode.GetBytes(text);
        int fcMin = FibSize;
        int fcMac = fcMin + textBytes.Length;

        // Place SEPX and FKP pages after the text, aligned to 512-byte page boundaries.
        // SepxOffset: next 2-byte aligned position after text (keep it simple: same page)
        int sepxOffset = fcMac;
        if (sepxOffset % 2 != 0) sepxOffset++;

        // PAPX FKP page: next 512-byte boundary after sepx
        int papFkpOffset = ((sepxOffset + 2 + 511) / 512) * 512;
        int chpFkpOffset = papFkpOffset + 512;
        int totalSize    = chpFkpOffset + 512;

        // Store for table-stream builder
        s_fcMin   = fcMin;
        s_fcMac   = fcMac;
        s_sepxFc  = sepxOffset;
        s_papFkpPn = papFkpOffset / 512;
        s_chpFkpPn = chpFkpOffset / 512;

        byte[] stream = new byte[totalSize];

        // Write FIB (all zeros, patched later)
        // FibBase (32 bytes)
        BinaryWriter16LE(stream, 0x0000, 0xA5EC);  // wIdent
        BinaryWriter16LE(stream, 0x0002, 0x00C1);  // nFib = 193 (Word97)
        BinaryWriter16LE(stream, 0x0004, 0x0000);  // unused
        BinaryWriter16LE(stream, 0x0006, 0x0409);  // lid en-US
        BinaryWriter16LE(stream, 0x0008, 0x0000);  // pnNext
        BinaryWriter16LE(stream, 0x000A, (ushort)((1 << 9) | (1 << 12))); // fWhichTblStm=1, fExtChar=1
        BinaryWriter16LE(stream, 0x000C, 0x00BF);  // nFibBack
        // lKey (4 bytes) = 0 at 0x000E
        // envr = 0 at 0x0012
        stream[0x0013] = 0x08;                     // fWord97Saved
        // chs, chsTables (4 bytes) = 0 at 0x0014
        BinaryWriter32LE(stream, 0x0018, (uint)fcMin);  // fcMin
        BinaryWriter32LE(stream, 0x001C, (uint)fcMac);  // fcMac

        // csw + rgW97 at byte 32
        BinaryWriter16LE(stream, 32, 14); // csw

        // clw + rgLw97 at byte 62
        BinaryWriter16LE(stream, 62, 22); // clw
        BinaryWriter32LE(stream, 64, (uint)totalSize);  // cbMac = rgLw97[0]
        BinaryWriter32LE(stream, 76, (uint)text.Length); // ccpText = rgLw97[3]

        // cfclcb at byte 152
        BinaryWriter16LE(stream, 152, 186); // cfclcb

        // cswNew at byte 154 + 186*8 = 154 + 1488 = 1642
        BinaryWriter16LE(stream, 1642, 0); // cswNew

        // Copy text
        Buffer.BlockCopy(textBytes, 0, stream, fcMin, textBytes.Length);

        // SEPX at sepxOffset: cbSepx=2 (no sprms)
        BinaryWriter16LE(stream, sepxOffset, 2); // cbSepx = 2

        // PAPX FKP at papFkpOffset (512 bytes)
        // crun at byte 511: 1 run
        stream[papFkpOffset + 511] = 1;
        // rgfc[0] = fcMin, rgfc[1] = fcMac
        BinaryWriter32LE(stream, papFkpOffset + 0, (uint)fcMin);
        BinaryWriter32LE(stream, papFkpOffset + 4, (uint)fcMac);
        // rgbx[0]: wordOffset=0 (=> default PAPX, no sprms), PHE=zeros
        // byte 8 = wordOffset = 0, bytes 9-20 = PHE zeros (already zero)

        // CHPX FKP at chpFkpOffset (512 bytes)
        stream[chpFkpOffset + 511] = 1;
        BinaryWriter32LE(stream, chpFkpOffset + 0, (uint)fcMin);
        BinaryWriter32LE(stream, chpFkpOffset + 4, (uint)fcMac);
        // rgbx[0]: wordOffset=0 (default CHPX)

        return stream;
    }

    // -----------------------------------------------------------------------
    // 1Table stream: STSH + STTBF/FFN + CLX + PlcBtePapx + PlcBteChpx + PlcfSed
    // -----------------------------------------------------------------------

    private byte[] BuildTableStream(string text, int wdStreamLength)
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);

        // (a) STSH (StyleSheet)
        // cbStshi = 20 bytes (10 ushorts):
        //   bytes.Length=20 > 18 -> rgftcStandardChpStsh[3] is read ✓
        //   bytes.Length=20 is NOT > 20 -> cbLSD loop NOT entered ✓
        s_stshOffset = 0;

        const int numStyles    = 12;  // DocSharp.StyleSheetMapping accesses Styles[11] unconditionally
        const int cbStshiBytes = 20;  // 10 x ushort

        w.Write((ushort)cbStshiBytes); // cbStshi
        w.Write((ushort)numStyles);    // cstd = 12
        w.Write((ushort)10);           // cbSTDBaseInFile
        w.Write((ushort)1);            // fStdStylenamesWritten (byte[4]=1, byte[5]=0)
        w.Write((ushort)105);          // stiMaxWhenSaved
        w.Write((ushort)15);           // istdMaxFixedWhenSaved
        w.Write((ushort)0);            // nVerBuiltInNamesWhenSaved
        w.Write((ushort)0);            // rgftcStandardChpStsh[0] -> font 0
        w.Write((ushort)0);            // rgftcStandardChpStsh[1] -> font 0
        w.Write((ushort)0);            // rgftcStandardChpStsh[2] -> font 0
        // STSHI body so far = 18 bytes (9 ushorts). cbStshi=20 means DocSharp reads 20 bytes,
        // so bytes.Length=20 > 18 -> rgftcStandardChpStsh[3] is populated from bytes[18..19].
        // bytes.Length=20 is NOT > 20 -> cbLSD/mpstilsd loop is NOT entered.
        w.Write((ushort)0);            // rgftcStandardChpStsh[3] -> font 0 (body bytes 18-19)

        // STD slot 0 = "Normal" style
        // Body layout (cbSTDBaseInFile=10 declared):
        //   [0..9]   STDFixed (10 bytes)
        //   [10]     xstzName.cch = 6 (1 byte)
        //   [11]     xstzName.pad = 0 (1 byte)
        //   [12..23] "Normal" UTF-16LE (12 bytes)
        //   [24..25] xstz null-terminator (2 bytes, consumed by +2 in DocSharp upxOffset formula)
        //   upxOffset = 10 + 1 + 12 + 2 = 25 (odd) -> aligned to 26
        //   [26..27] UPX[0] cbUPX = 0
        //   [28..29] UPX[1] cbUPX = 0
        //   cbStd = 30 bytes
        const string normalName = "Normal";
        const int    cbNormalStd = 30;

        w.Write((ushort)cbNormalStd);
        w.Write((ushort)0x0000);  // STDFixed: sti=0
        w.Write((ushort)0xFFF1);  // stk=1 (para), istdBase=0xFFF
        w.Write((ushort)0x0002);  // cupx=2, istdNext=0
        w.Write((ushort)0x0000);  // bchUpe
        w.Write((ushort)0x0000);  // grLpUpxSw
        w.Write((byte)normalName.Length); // cch = 6
        w.Write((byte)0);                  // pad
        foreach (char c in normalName) w.Write((ushort)c);
        w.Write((ushort)0); // xstz null-terminator
        w.Write((ushort)0); // UPX[0] cbUPX = 0 (at aligned position 26)
        w.Write((ushort)0); // UPX[1] cbUPX = 0 (at position 28)

        // Slots 1..11: empty (cbStd = 0)
        for (int i = 1; i < numStyles; i++) w.Write((ushort)0);

        s_stshSize = (uint)ms.Length;

        // (b) STTBF/FFN -- Font table, one entry "Times New Roman"
        // DocSharp.FontFamilyName: after reading xszFtn it may try to read xszAlt.
        // We add 2 extra zero bytes after the name+null so xszAlt scan immediately finds null.
        s_ffnOffset = s_stshSize;

        byte[] fontNameBytes = Encoding.Unicode.GetBytes("Times New Roman\0"); // 32 bytes
        var    fontWithExtra = new byte[fontNameBytes.Length + 2]; // 34 bytes
        fontNameBytes.CopyTo(fontWithExtra, 0);

        int payloadSize   = 1 + 2 + 1 + 1 + 10 + 24 + fontWithExtra.Length; // 39+34=73
        int paddedPayload = (payloadSize + 1) & ~1;                           // 74
        int cchData       = paddedPayload / 2;                                // 37

        w.Write((ushort)0xFFFF); // fExtend
        w.Write((ushort)1);      // cData = 1
        w.Write((ushort)0);      // cbExtra = 0
        w.Write((ushort)cchData);
        w.Write((byte)0x22);                        // ffid
        w.Write((ushort)400);                       // wWeight
        w.Write((byte)0);                           // chs
        w.Write((byte)0);                           // iBound
        for (int i = 0; i < 10; i++) w.Write((byte)0); // panose
        for (int i = 0; i < 24; i++) w.Write((byte)0); // FontSig
        w.Write(fontWithExtra);
        for (int i = payloadSize; i < paddedPayload; i++) w.Write((byte)0);

        s_ffnSize = (uint)ms.Length - s_ffnOffset;

        // (c) CLX (piece table)
        s_clxOffset = (uint)ms.Length;
        const int plcPcdSize = 2 * 4 + 1 * 8; // 16 bytes

        w.Write((byte)0x02);
        w.Write((uint)plcPcdSize);
        w.Write((uint)0);              // aCP[0] = 0
        w.Write((uint)text.Length);    // aCP[1] = cpCount
        w.Write((ushort)0);            // Pcd.flags
        w.Write((uint)s_fcMin);        // Pcd.fc (byte offset of text in WordDocument)
        w.Write((ushort)0);            // Pcd.prm

        s_clxSize = (uint)ms.Length - s_clxOffset;

        // (d) PlcBtePapx -- paragraph FKP BTE table [MS-DOC 2.8.25]
        // Structure: (n+1) FC values + n FKP page numbers where n = number of FKP pages
        // For 1 FKP: [fcMin(4)][fcMac(4)][fkpPageNo(4)] = 12 bytes
        s_papBteOffset = (uint)ms.Length;

        w.Write((uint)s_fcMin);      // first CP byte offset
        w.Write((uint)s_fcMac);      // limit CP byte offset (exclusive)
        w.Write((uint)s_papFkpPn);   // FKP page number

        s_papBteSize = (uint)ms.Length - s_papBteOffset;

        // (e) PlcBteChpx -- character FKP BTE table
        s_chpBteOffset = (uint)ms.Length;

        w.Write((uint)s_fcMin);
        w.Write((uint)s_fcMac);
        w.Write((uint)s_chpFkpPn);

        s_chpBteSize = (uint)ms.Length - s_chpBteOffset;

        // (f) PlcfSed -- Section plex [MS-DOC 2.8.26]
        // Structure: (n+1) CP values + n SED records (12 bytes each)
        // For 1 section covering whole document:
        //   CP[0] = 0 (section start)
        //   CP[1] = text.Length (section limit)
        //   SED[0]: fn(2) + fcSepx(4) + fnMpr(2) + fcMpr(4) = 12 bytes
        //     fn = 0xFFFF (section props in WordDocument stream at fcSepx)
        //     fcSepx = s_sepxFc
        s_sedOffset = (uint)ms.Length;

        w.Write((uint)0);              // CP[0] = section start
        w.Write((uint)text.Length);    // CP[1] = section end (exclusive)
        w.Write((ushort)0xFFFF);       // SED.fn = 0xFFFF (SEPX location is fcSepx)
        w.Write((uint)s_sepxFc);       // SED.fcSepx = byte offset of SEPX in WordDocument
        w.Write((ushort)0);            // SED.fnMpr
        w.Write((uint)0xFFFFFFFF);     // SED.fcMpr (no master page reference)

        s_sedSize = (uint)ms.Length - s_sedOffset;

        return ms.ToArray();
    }

    // -----------------------------------------------------------------------
    // FIB patching
    // -----------------------------------------------------------------------

    private static void PatchFibFcLcb(byte[] stream, int pairIdx, uint fc, uint lcb)
    {
        int off = FibFcLcbBase + pairIdx * 8;
        BinaryWriter32LE(stream, off,     fc);
        BinaryWriter32LE(stream, off + 4, lcb);
    }

    private static void BinaryWriter32LE(byte[] buf, int off, uint v)
    {
        buf[off]     = (byte)( v        & 0xFF);
        buf[off + 1] = (byte)((v >>  8) & 0xFF);
        buf[off + 2] = (byte)((v >> 16) & 0xFF);
        buf[off + 3] = (byte)((v >> 24) & 0xFF);
    }

    private static void BinaryWriter16LE(byte[] buf, int off, ushort v)
    {
        buf[off]     = (byte)(v & 0xFF);
        buf[off + 1] = (byte)(v >> 8);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Grows <paramref name="src"/> to at least <paramref name="minSize"/> bytes AND to a whole
    /// number of <see cref="SectorSize"/> sectors, so the length written into the CFB directory
    /// entry is exactly the length the FAT chain covers.
    /// </summary>
    private static byte[] PadToSector(byte[] src, int minSize)
    {
        int target = Math.Max(src.Length, minSize);
        target = CeilDiv(target, SectorSize) * SectorSize;
        if (src.Length == target) return src;
        var r = new byte[target];
        Buffer.BlockCopy(src, 0, r, 0, src.Length);
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
        int wdSectors  = CeilDiv(wordDocStream.Length, SectorSize);
        int tblSectors = CeilDiv(tableStream.Length, SectorSize);

        // How many sectors the FAT itself needs. Each FAT sector is also a sector the FAT has to
        // describe, so adding one can push the total past another 128-entry boundary -- iterate to
        // a fixed point. This used to be hardcoded to a single FAT sector, which caps a file at
        // 128 sectors (64 KB): past that the writer indexed off the end of the array and threw,
        // so saving any document over roughly 30,000 characters crashed instead of producing a file.
        int fatSectors = 1;
        while (true)
        {
            int total  = fatSectors + 1 + wdSectors + tblSectors;
            int needed = Math.Max(1, CeilDiv(total, FatEntriesPerSector));
            if (needed == fatSectors) break;
            fatSectors = needed;
        }

        // Beyond 109 FAT sectors the header's DIFAT array is full and the format requires a DIFAT
        // chain, which this writer does not emit. Refuse loudly rather than write a file whose FAT
        // silently stops describing the tail of the document.
        if (fatSectors > HeaderDifatEntries)
        {
            throw new NotSupportedException(
                "This document is too large to save as Word 97-2003 (.doc). Save it as .docx instead.");
        }

        int dirSector = fatSectors;
        int wdFirst   = dirSector + 1;
        int tblFirst  = wdFirst + wdSectors;

        uint[] fat = new uint[fatSectors * FatEntriesPerSector];
        for (int i = 0; i < fat.Length; i++) fat[i] = FREESECT;

        for (int i = 0; i < fatSectors; i++) fat[i] = FATSECT;
        fat[dirSector] = ENDOFCHAIN;

        for (int i = 0; i < wdSectors - 1; i++) fat[wdFirst + i] = (uint)(wdFirst + i + 1);
        if (wdSectors  > 0) fat[wdFirst  + wdSectors  - 1] = ENDOFCHAIN;

        for (int i = 0; i < tblSectors - 1; i++) fat[tblFirst + i] = (uint)(tblFirst + i + 1);
        if (tblSectors > 0) fat[tblFirst + tblSectors - 1] = ENDOFCHAIN;

        byte[] dirBytes = BuildDirectory(
            wdFirst:  (uint)wdFirst,  wdSize:  wdLogicalSize,
            tblFirst: (uint)tblFirst, tblSize: tblLogicalSize);

        using var bw = new BinaryWriter(dest, Encoding.Unicode, leaveOpen: true);
        WriteCfbHeader(bw, fatSectors, dirSector);
        WriteFatSector(bw, fat);
        bw.Write(dirBytes);
        WritePadded(bw, wordDocStream);
        WritePadded(bw, tableStream);
    }

    private static void WriteCfbHeader(BinaryWriter bw, int fatSectors, int dirSector)
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
        bw.Write((uint)fatSectors);
        bw.Write((uint)dirSector);
        bw.Write((uint)0);
        bw.Write((uint)MiniStreamCutoff);
        bw.Write(ENDOFCHAIN);
        bw.Write((uint)0);
        bw.Write(ENDOFCHAIN);
        bw.Write((uint)0);

        // DIFAT: the FAT sectors occupy sectors 0..fatSectors-1, in order.
        for (int i = 0; i < HeaderDifatEntries; i++)
            bw.Write(i < fatSectors ? (uint)i : FREESECT);
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

