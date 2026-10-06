using MindrayMiddleware;

namespace MindrayMiddleware.Tests;

public class MindrayProtocolParserTests
{
    private readonly MindrayMessage _message = MindrayProtocolParser.Parse(MindraySampleCapture.MessageOnly);

    [Fact]
    public void Handshake_prefix_is_dle_and_is_not_part_of_the_parsed_message()
    {
        Assert.Equal(0x10, MindraySampleCapture.RawWithHandshake[0]);
        Assert.Equal(MindraySampleCapture.MessageOnly, MindraySampleCapture.RawWithHandshake[1..]);
        Assert.Equal(0x05, MindraySampleCapture.MessageOnly[0]);
    }

    [Fact]
    public void Header_is_ctr()
    {
        Assert.Equal("CTR", _message.Header);
    }

    [Fact]
    public void SampleInfo_matches_the_capture()
    {
        var sample = _message.FindBlock("SampleInfo");
        Assert.NotNull(sample);
        Assert.Equal("1669", sample.Fields["SampleID"]);
        Assert.Equal("0", sample.Fields["Mode"]);
        Assert.Equal("2026-10-04 15:47:33", sample.Fields["TestTime"]);
        Assert.Equal("", sample.Fields["Name"]);
        Assert.Equal("0", sample.Fields["Gender"]);
        Assert.Equal("", sample.Fields["Dept"]);
    }

    [Theory]
    [InlineData("WBC", "15.1", "4.0", "10.0", "10^9/L")]
    [InlineData("Lymph#", "3.2", "0.8", "4.0", "10^9/L")]
    [InlineData("Gran%", "71.2", "50.0", "70.0", "%")]
    [InlineData("HGB", "125", "115", "180", "g/L")]
    [InlineData("RBC", "4.57", "3.50", "5.50", "10^12/L")]
    [InlineData("HCT", "39.0", "36.0", "52.0", "%")]
    [InlineData("PLT", "326", "100", "300", "10^9/L")]
    [InlineData("PDW", "15.2", "9.0", "17.0", " ")]
    public void Parameter_blocks_expose_value_range_and_unit(
        string name, string val, string low, string high, string unit)
    {
        var block = _message.FindBlock(name);
        Assert.NotNull(block);
        Assert.Equal(val, block.Fields["Val"]);
        Assert.Equal(low, block.Fields["Low"]);
        Assert.Equal(high, block.Fields["High"]);
        Assert.Equal(unit, block.Fields["Unit"]);
    }

    [Fact]
    public void AlarmFlag_matches_the_capture()
    {
        var alarm = _message.FindBlock("AlarmFlag");
        Assert.NotNull(alarm);
        Assert.Equal("0", alarm.Fields["Rm"]);
        Assert.Equal("0", alarm.Fields["R1"]);
        Assert.Equal("0", alarm.Fields["R2"]);
        Assert.Equal("1", alarm.Fields["R3"]);
        Assert.Equal("0", alarm.Fields["R4"]);
        Assert.Equal("0", alarm.Fields["Pm"]);
        Assert.Equal("1", alarm.Fields["Pl"]);
        Assert.Equal("0", alarm.Fields["Ps"]);
    }

    [Theory]
    [InlineData("WBCHisto", "WHistoData")]
    [InlineData("RBCHisto", "RHistoData")]
    [InlineData("PLTHisto", "PHistoData")]
    public void Histogram_binary_is_read_by_declared_length(string blockName, string dataKey)
    {
        var block = _message.FindBlock(blockName);
        Assert.NotNull(block);
        Assert.Equal("256", block.Fields["DataLen"]);
        Assert.Equal("1", block.Fields["MetaDataLen"]);

        byte[] data = block.BinaryFields[dataKey];
        Assert.Equal(256, data.Length);
        Assert.Contains((byte)0x08, data);
        Assert.Contains((byte)0x04, data);
    }

    [Fact]
    public void Trailing_bytes_are_kept_as_footer()
    {
        Assert.Equal(new byte[] { 0x0A, 0x0F }, _message.FooterBytes);
        Assert.Equal(26, _message.Blocks.Count);
    }
}
