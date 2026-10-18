using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Minimal SVG path "d" parser producing an Avalonia <see cref="StreamGeometry"/>.
/// Supports the subset used by the official WaveDrom skin: absolute/relative
/// move (M/m), line (L/l), horizontal/vertical line (H/h/V/v), cubic bezier
/// (C/c), and close (Z/z), including repeated coordinate pairs and implicit
/// line-to after a move.
/// </summary>
internal static class SvgPathParser
{
    public static StreamGeometry Parse(string data)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var reader = new Tokenizer(data);

        var current = new Point(0, 0);
        var start = new Point(0, 0);
        var figureOpen = false;
        char command = '\0';

        while (reader.TryReadCommand(out var next))
        {
            if (next != '\0')
            {
                command = next;
            }

            switch (command)
            {
                case 'M':
                case 'm':
                {
                    var p = reader.ReadPoint(command == 'm', current);
                    if (figureOpen)
                    {
                        ctx.EndFigure(false);
                    }

                    // isFilled must be true so skin brick fills (s7..s14, s5, s6)
                    // are painted; stroke-only bricks are unaffected (null fill).
                    ctx.BeginFigure(p, true);
                    current = p;
                    start = p;
                    figureOpen = true;
                    // Subsequent coordinate pairs are implicit lineto.
                    command = command == 'm' ? 'l' : 'L';
                    break;
                }
                case 'L':
                case 'l':
                {
                    var p = reader.ReadPoint(command == 'l', current);
                    ctx.LineTo(p);
                    current = p;
                    break;
                }
                case 'H':
                {
                    current = new Point(reader.ReadNumber(), current.Y);
                    ctx.LineTo(current);
                    break;
                }
                case 'h':
                {
                    current = new Point(current.X + reader.ReadNumber(), current.Y);
                    ctx.LineTo(current);
                    break;
                }
                case 'V':
                {
                    current = new Point(current.X, reader.ReadNumber());
                    ctx.LineTo(current);
                    break;
                }
                case 'v':
                {
                    current = new Point(current.X, current.Y + reader.ReadNumber());
                    ctx.LineTo(current);
                    break;
                }
                case 'C':
                case 'c':
                {
                    var relative = command == 'c';
                    var c1 = reader.ReadPoint(relative, current);
                    var c2 = reader.ReadPoint(relative, current);
                    var end = reader.ReadPoint(relative, current);
                    ctx.CubicBezierTo(c1, c2, end);
                    current = end;
                    break;
                }
                case 'Z':
                case 'z':
                {
                    if (figureOpen)
                    {
                        ctx.EndFigure(true);
                        figureOpen = false;
                    }

                    current = start;
                    break;
                }
                default:
                    // Unsupported/unknown command: stop to avoid infinite loops.
                    return geometry;
            }
        }

        if (figureOpen)
        {
            ctx.EndFigure(false);
        }

        return geometry;
    }

    private struct Tokenizer(string data)
    {
        private readonly string _data = data;
        private int _index;

        public bool TryReadCommand(out char command)
        {
            SkipSeparators();
            if (_index >= _data.Length)
            {
                command = '\0';
                return false;
            }

            var c = _data[_index];
            if (char.IsLetter(c))
            {
                _index++;
                command = c;
                return true;
            }

            // No new command: continuation of the previous one.
            command = '\0';
            return true;
        }

        public double ReadNumber()
        {
            SkipSeparators();
            var start = _index;
            if (_index < _data.Length && (_data[_index] is '-' or '+'))
            {
                _index++;
            }

            while (_index < _data.Length && (char.IsDigit(_data[_index]) || _data[_index] == '.'))
            {
                _index++;
            }

            // Scientific notation (not used by WaveDrom skins but harmless).
            if (_index < _data.Length && (_data[_index] is 'e' or 'E'))
            {
                _index++;
                if (_index < _data.Length && (_data[_index] is '-' or '+'))
                {
                    _index++;
                }

                while (_index < _data.Length && char.IsDigit(_data[_index]))
                {
                    _index++;
                }
            }

            var span = _data.AsSpan(start, _index - start);
            return double.Parse(span, CultureInfo.InvariantCulture);
        }

        public Point ReadPoint(bool relative, Point origin)
        {
            var x = ReadNumber();
            var y = ReadNumber();
            return relative ? new Point(origin.X + x, origin.Y + y) : new Point(x, y);
        }

        private void SkipSeparators()
        {
            while (_index < _data.Length &&
                   (_data[_index] is ' ' or ',' or '\t' or '\r' or '\n'))
            {
                _index++;
            }
        }
    }
}
