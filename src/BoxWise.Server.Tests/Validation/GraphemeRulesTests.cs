using BoxWise.Shared.Text;
using BoxWise.Shared.Validation;

namespace BoxWise.Server.Tests.Validation;

public class GraphemeRulesTests
{
    [Fact]
    public void CountGraphemes_EmojiZwjFamily_CountsAsOne()
    {
        Assert.Equal(1, GraphemeRules.CountGraphemes("👨‍👩‍👧‍👦"));
    }

    [Fact]
    public void CountGraphemes_CombiningMarkAndCrLf_CountAsOne()
    {
        Assert.Equal(1, GraphemeRules.CountGraphemes("e\u0301"));
        Assert.Equal(1, GraphemeRules.CountGraphemes("\r\n"));
    }

    [Fact]
    public void TryNormalizeDescription_300Graphemes_Accepted_301Rejected()
    {
        var accepted = string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 300));
        var rejected = string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 301));

        Assert.True(GraphemeRules.TryNormalizeDescription(accepted, out var normalized, out var error));
        Assert.Equal(accepted, normalized);
        Assert.Null(error);

        Assert.False(GraphemeRules.TryNormalizeDescription(rejected, out var rejectedNormalized, out var rejectedError));
        Assert.Equal(rejected, rejectedNormalized);
        Assert.Equal(GraphemeRules.DescriptionTooLongMessage, rejectedError);
    }

    [Fact]
    public void NormalizeDescription_TrimsEdges_KeepsInternalLineBreaks()
    {
        Assert.Equal("第一行\r\n第二行",
            GraphemeRules.NormalizeDescription("\r\n 第一行\r\n第二行 \t\r\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    [InlineData(null)]
    public void NormalizeDescription_Blank_ReturnsNull(string? value)
    {
        Assert.Null(GraphemeRules.NormalizeDescription(value));
    }

    [Fact]
    public void LocationPhotoRules_DetectsMagicAndExtensions()
    {
        Assert.Equal("image/jpeg",
            LocationPhotoRules.DetectContentType([0xFF, 0xD8, 0xFF, 0x00]));
        Assert.Equal("image/png",
            LocationPhotoRules.DetectContentType([0x89, 0x50, 0x4E, 0x47]));
        Assert.Equal("image/webp",
            LocationPhotoRules.DetectContentType(
                [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50]));

        Assert.Null(LocationPhotoRules.DetectContentType([0x47, 0x49, 0x46]));
        Assert.Equal(".jpg", LocationPhotoRules.GetExtension("image/jpeg"));
        Assert.Equal(".png", LocationPhotoRules.GetExtension("image/png"));
        Assert.Equal(".webp", LocationPhotoRules.GetExtension("image/webp"));
        Assert.Null(LocationPhotoRules.GetExtension("image/gif"));

        Assert.True(LocationPhotoRules.IsAllowedContentType("image/jpg"));
        Assert.False(LocationPhotoRules.IsAllowedContentType("image/gif"));
        Assert.Equal(10 * 1024 * 1024, LocationPhotoRules.MaxBytes);
    }
}
