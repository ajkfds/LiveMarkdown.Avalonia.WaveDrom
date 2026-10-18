using Avalonia.Controls;

namespace LiveMarkdown.Avalonia;

public class WaveDromBlockNode : BlockNode<WaveDromCodeBlock>
{
    public override Control Control { get; }

    private readonly MarkdownTextBlock _textBlock;
    private readonly WaveDromPresenter _presenter;

    public WaveDromBlockNode()
    {
        Control = new Panel
        {
            Classes = { "WaveDromBlock" },
            Children =
            {
                (_textBlock = new MarkdownTextBlock
                {
                    Classes = { "WaveDromBlock" }
                }),
                (_presenter = new WaveDromPresenter
                {
                    Classes = { "WaveDromBlock" }
                })
            }
        };
    }

    protected override bool UpdateCore(
        DocumentNode documentNode,
        WaveDromCodeBlock wavedrom,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        if (wavedrom.IsOpen)
        {
            _presenter.IsVisible = false;
            _textBlock.Text = wavedrom.Lines.ToString();
            _textBlock.IsVisible = true;
        }
        else
        {
            _textBlock.IsVisible = false;
            _presenter.Text = wavedrom.Lines.ToString();
            _presenter.IsVisible = true;
        }

        return true;
    }
}
