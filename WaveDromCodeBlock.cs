using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

public class WaveDromCodeBlock(BlockParser parser) : FencedCodeBlock(parser);

public class WaveDromBlockParser : FencedBlockParserBase<WaveDromCodeBlock>
{
    public WaveDromBlockParser()
    {
        OpeningCharacters = ['`', '~'];
        InfoPrefix = "wavedrom";
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (!IsWaveDromFenceOpening(processor.Line.AsSpan()))
        {
            return BlockState.None;
        }

        return base.TryOpen(processor);
    }

    protected override WaveDromCodeBlock CreateFencedBlock(BlockProcessor processor)
    {
        return new WaveDromCodeBlock(this);
    }

    private static bool IsWaveDromFenceOpening(ReadOnlySpan<char> line)
    {
        line = line.TrimStart();
        if (line.Length < 4 || line[0] is not ('`' or '~'))
        {
            return false;
        }

        var fence = line[0];
        var index = 0;
        while (index < line.Length && line[index] == fence)
        {
            index++;
        }

        if (index < 3)
        {
            return false;
        }

        var info = line[index..].TrimStart();
        if (info.Length == 0)
        {
            return false;
        }

        var tokenEnd = 0;
        while (tokenEnd < info.Length && !char.IsWhiteSpace(info[tokenEnd]))
        {
            tokenEnd++;
        }

        return info[..tokenEnd].Equals("wavedrom", StringComparison.OrdinalIgnoreCase);
    }
}

public class WaveDromExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<WaveDromBlockParser>())
        {
            pipeline.BlockParsers.Insert(0, new WaveDromBlockParser());
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

public static class WaveDromExtensionBuilder
{
    public static MarkdownPipelineBuilder UseWaveDrom(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<WaveDromExtension>();
        return pipeline;
    }
}
