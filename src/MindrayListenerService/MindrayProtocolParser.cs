using System;
using System.Collections.Generic;
using System.Text;

namespace MindrayMiddleware
{
    /// <summary>
    /// Parses the Mindray BC-3000Plus's proprietary key-value serial protocol
    /// (NOT ASTM/HL7). Structure discovered from a live capture:
    ///
    ///   ENQ(0x05) "<MsgType>" ETX(0x03)
    ///   <BlockName> FF(0x0C) ( key SYN(0x16) value BS(0x08) )* EOT(0x04)
    ///   <BlockName> FF(0x0C) ( ... ) EOT(0x04)
    ///   ...
    ///   [trailing binary footer, not block-structured]
    ///
    /// Histogram blocks (WBCHisto/RBCHisto/PLTHisto) declare a "DataLen" field
    /// giving the exact byte count of a following raw binary field
    /// (WHistoData/RHistoData/PHistoData) — that binary data must be read by
    /// length, not scanned for delimiters, since bin-count bytes can coincide
    /// with delimiter values (0x08, 0x16, 0x0C, 0x04) by chance.
    /// </summary>
    public class MindrayBlock
    {
        public string Name { get; set; } = "";
        public Dictionary<string, string> Fields { get; } = new Dictionary<string, string>();
        public Dictionary<string, byte[]> BinaryFields { get; } = new Dictionary<string, byte[]>();
    }

    public class MindrayMessage
    {
        public string Header { get; set; } = "";
        public List<MindrayBlock> Blocks { get; } = new List<MindrayBlock>();
        public byte[] FooterBytes { get; set; } = new byte[0];

        public MindrayBlock? FindBlock(string name) =>
            Blocks.Find(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static class MindrayProtocolParser
    {
        private const byte ENQ = 0x05;
        private const byte ETX = 0x03;
        private const byte BLOCK_START = 0x0C; // FF
        private const byte FIELD_SEP = 0x08;    // BS
        private const byte KV_SEP = 0x16;       // SYN
        private const byte EOT = 0x04;

        // Keys whose value is raw binary data of length given by a prior "DataLen" field
        private static readonly HashSet<string> BinaryFieldKeys = new HashSet<string>
        {
            "WHistoData", "RHistoData", "PHistoData"
        };

        public static MindrayMessage Parse(byte[] data)
        {
            var msg = new MindrayMessage();
            int i = 0;

            // --- Header: ENQ <text> ETX ---
            if (i < data.Length && data[i] == ENQ)
            {
                i++;
                int start = i;
                while (i < data.Length && data[i] != ETX) i++;
                msg.Header = Encoding.ASCII.GetString(data, start, i - start);
                if (i < data.Length) i++; // skip ETX
            }

            // --- Blocks ---
            while (i < data.Length)
            {
                int nameStart = i;
                while (i < data.Length && data[i] != BLOCK_START) i++;

                if (i >= data.Length)
                {
                    // No more blocks — remainder is trailing binary footer
                    int footerLen = data.Length - nameStart;
                    if (footerLen > 0)
                    {
                        msg.FooterBytes = new byte[footerLen];
                        Array.Copy(data, nameStart, msg.FooterBytes, 0, footerLen);
                    }
                    break;
                }

                string blockName = Encoding.ASCII.GetString(data, nameStart, i - nameStart);
                i++; // skip BLOCK_START

                var block = new MindrayBlock { Name = blockName };
                int lastDataLen = -1;

                while (i < data.Length && data[i] != EOT)
                {
                    int keyStart = i;
                    while (i < data.Length && data[i] != KV_SEP) i++;
                    if (i >= data.Length) break; // malformed tail, bail out of this block

                    string key = Encoding.ASCII.GetString(data, keyStart, i - keyStart);
                    i++; // skip KV_SEP

                    if (BinaryFieldKeys.Contains(key) && lastDataLen >= 0 && i + lastDataLen <= data.Length)
                    {
                        byte[] raw = new byte[lastDataLen];
                        Array.Copy(data, i, raw, 0, lastDataLen);
                        block.BinaryFields[key] = raw;
                        i += lastDataLen;
                    }
                    else
                    {
                        int valStart = i;
                        while (i < data.Length && data[i] != FIELD_SEP && data[i] != EOT) i++;
                        string value = Encoding.ASCII.GetString(data, valStart, i - valStart);
                        block.Fields[key] = value;

                        if (key == "DataLen")
                            int.TryParse(value, out lastDataLen);
                    }

                    if (i < data.Length && data[i] == FIELD_SEP) i++; // skip BS
                }

                if (i < data.Length && data[i] == EOT) i++; // skip EOT
                msg.Blocks.Add(block);
            }

            return msg;
        }

        /// <summary>Quick console dump for debugging a parsed message.</summary>
        public static void Print(MindrayMessage msg)
        {
            Console.WriteLine($"Header: {msg.Header}");
            foreach (var block in msg.Blocks)
            {
                Console.WriteLine($"-- {block.Name} --");
                foreach (var kv in block.Fields)
                    Console.WriteLine($"   {kv.Key} = {kv.Value}");
                foreach (var bin in block.BinaryFields)
                    Console.WriteLine($"   {bin.Key} = <{bin.Value.Length} raw bytes>");
            }
            if (msg.FooterBytes.Length > 0)
                Console.WriteLine($"Footer: {BitConverter.ToString(msg.FooterBytes).Replace("-", " ")}");
        }
    }
}