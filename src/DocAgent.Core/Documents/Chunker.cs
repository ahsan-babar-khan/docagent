namespace DocAgent.Core.Documents;

public static class Chunker
{
    // ~800 characters per chunk, 100 characters of overlap so a sentence crossing a boundary is not lost
    public static List<string> Split(string text, int size = 800, int overlap = 100)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (overlap < 0 || overlap >= size) throw new ArgumentOutOfRangeException(nameof(overlap));

        var chunks = new List<string>();
        text = (text ?? "").Replace("\r\n", "\n").Trim();
        for (int start = 0; start < text.Length; start += size - overlap)
        {
            var length = Math.Min(size, text.Length - start);
            chunks.Add(text.Substring(start, length));
            if (start + length >= text.Length) break;
        }
        return chunks;
    }
}
