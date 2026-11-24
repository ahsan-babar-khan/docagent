using DocAgent.Core.Documents;
using FluentAssertions;

namespace DocAgent.Tests.Unit;

public class ChunkerTests
{
    [Fact]
    public void Text_of_2000_chars_gives_3_overlapping_chunks()
    {
        var text = string.Concat(Enumerable.Range(0, 2000).Select(i => (char)('a' + i % 26)));

        var chunks = Chunker.Split(text);

        chunks.Should().HaveCount(3);
        chunks[^1][^1].Should().Be(text[^1]);
        chunks[1][..100].Should().Be(chunks[0][^100..], "consecutive chunks overlap by 100 characters");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public void Empty_text_gives_no_chunks(string text) => Chunker.Split(text).Should().BeEmpty();

    [Fact]
    public void Short_text_is_one_chunk_with_normalised_newlines() =>
        Chunker.Split("line one\r\nline two").Should().Equal("line one\nline two");

    [Fact]
    public void Every_character_is_covered()
    {
        var text = new string('x', 5003);
        var chunks = Chunker.Split(text, size: 800, overlap: 100);
        (chunks.Sum(c => c.Length) - 100 * (chunks.Count - 1)).Should().Be(text.Length);
    }

    [Fact]
    public void Overlap_not_smaller_than_size_is_rejected() =>
        FluentActions.Invoking(() => Chunker.Split("abc", size: 100, overlap: 100))
            .Should().Throw<ArgumentOutOfRangeException>();
}
