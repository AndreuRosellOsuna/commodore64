namespace C64Console.Core;

/// <summary>Which part of a line or of the screen an erase covers (the VT "Ps" parameter).</summary>
public enum EraseMode
{
    ToEnd = 0,
    ToStart = 1,
    All = 2,
}

/// <summary>
/// A grid of character cells with a cursor, behaving like a VT terminal screen.
/// <list type="bullet">
/// <item>Writing in the last column sets a "wrap pending" flag instead of moving to the next line straight away;
/// the wrap happens only if another character follows. ConPTY relies on this.</item>
/// <item>Line feeds on the bottom margin scroll the scroll region (the whole screen by default) up.
/// Lines that scroll off the top of the main screen are kept in the scrollback.</item>
/// <item>There is a second, alternate screen without scrollback, used by full-screen programs such as vim and less.</item>
/// </list>
/// Lines are also numbered absolutely (see <see cref="GetCell(int, long)"/>): screen row <c>r</c> is line
/// <c>LinesScrolledOff + r</c> and the scrollback holds the lines just before that. Numbers stay stable as
/// output scrolls, which is what a selection or a scrolled-back view needs.
/// </summary>
public sealed class ScreenBuffer
{
    public const int DefaultScrollbackLimit = 2000;

    private Cell[] _cells;
    private Cell[] _mainCells;
    private Cell[]? _alternateCells;

    // Scrollback ring buffer; each line keeps the width it had when it scrolled off.
    private readonly Cell[][] _scrollback;
    private int _scrollbackStart;

    public ScreenBuffer(int columns, int rows, byte foreground, int scrollbackLimit = DefaultScrollbackLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(scrollbackLimit);

        Columns = columns;
        Rows = rows;
        Foreground = foreground;
        ScrollBottom = rows - 1;
        _mainCells = _cells = new Cell[columns * rows];
        _scrollback = new Cell[scrollbackLimit][];
        Clear();
    }

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int CursorColumn { get; private set; }
    public int CursorRow { get; private set; }

    /// <summary>True after writing in the last column: the next character goes to the start of the next line.</summary>
    public bool WrapPending { get; private set; }

    /// <summary>DECAWM. When off, characters written at the right margin overwrite the last column.</summary>
    public bool AutoWrap { get; set; } = true;

    /// <summary>Colour used for characters written from now on.</summary>
    public byte Foreground { get; set; }

    /// <summary>Background colour for characters written and cells erased from now on (<see cref="Cell.DefaultBackground"/> = theme colour).</summary>
    public byte Background { get; set; } = Cell.DefaultBackground;

    /// <summary>Whether characters written from now on are shown in reverse video.</summary>
    public bool Reverse { get; set; }

    /// <summary>Top row of the scroll region (0-based, inclusive).</summary>
    public int ScrollTop { get; private set; }

    /// <summary>Bottom row of the scroll region (0-based, inclusive).</summary>
    public int ScrollBottom { get; private set; }

    public bool IsAlternateScreen { get; private set; }

    /// <summary>Number of lines currently held in the scrollback.</summary>
    public int ScrollbackCount { get; private set; }

    public int ScrollbackLimit => _scrollback.Length;

    /// <summary>How many lines have ever scrolled off the top of the main screen. Never decreases.</summary>
    public long LinesScrolledOff { get; private set; }

    /// <summary>Absolute number of the oldest line still available (the first scrollback line, or the top screen row).</summary>
    public long FirstLine => LinesScrolledOff - ScrollbackCount;

    /// <summary>Incremented on every change, so a renderer can skip unchanged frames.</summary>
    public int Version { get; private set; }

    public Cell this[int column, int row] => _cells[row * Columns + column];

    private Cell BlankCell => Cell.Blank(Foreground, Background);

    /// <summary>Blanks the whole screen and moves the cursor home.</summary>
    public void Clear()
    {
        Array.Fill(_cells, BlankCell);
        MoveCursorTo(0, 0);
    }

    public void Write(string text)
    {
        foreach (var c in text)
        {
            Write(c);
        }
    }

    public void WriteLine(string text = "")
    {
        Write(text);
        NewLine();
    }

    /// <summary>Puts a printable character at the cursor and advances it.</summary>
    public void Write(char c)
    {
        if (WrapPending)
        {
            NewLine();
        }

        _cells[CursorRow * Columns + CursorColumn] = new Cell(c, Foreground, Reverse, Background);
        if (CursorColumn < Columns - 1)
        {
            CursorColumn++;
        }
        else if (AutoWrap)
        {
            WrapPending = true;
        }

        Version++;
    }

    public void CarriageReturn() => MoveCursorTo(0, CursorRow);

    /// <summary>
    /// Moves down one row. On the bottom margin the scroll region scrolls up instead;
    /// below the region the cursor just stops at the last row. The column is unchanged.
    /// </summary>
    public void LineFeed()
    {
        if (CursorRow == ScrollBottom)
        {
            ScrollRegionUp(ScrollTop, ScrollBottom, 1, keepHistory: true);
            MoveCursorTo(CursorColumn, CursorRow);
        }
        else
        {
            MoveCursorTo(CursorColumn, CursorRow + 1);
        }
    }

    /// <summary>Moves up one row, scrolling the scroll region down when on the top margin.</summary>
    public void ReverseIndex()
    {
        if (CursorRow == ScrollTop)
        {
            ScrollRegionDown(ScrollTop, ScrollBottom, 1);
            MoveCursorTo(CursorColumn, CursorRow);
        }
        else
        {
            MoveCursorTo(CursorColumn, CursorRow - 1);
        }
    }

    /// <summary>Carriage return plus line feed.</summary>
    public void NewLine()
    {
        CarriageReturn();
        LineFeed();
    }

    /// <summary>Moves the cursor to a cell (0-based), clamped to the screen. Clears a pending wrap.</summary>
    public void MoveCursorTo(int column, int row)
    {
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        WrapPending = false;
        Version++;
    }

    public void MoveCursorBy(int columns, int rows) => MoveCursorTo(CursorColumn + columns, CursorRow + rows);

    /// <summary>
    /// Sets the scroll region (DECSTBM), 0-based and inclusive. Invalid regions (fewer than two rows) are ignored.
    /// Like a VT100, the cursor moves home.
    /// </summary>
    public void SetScrollRegion(int top, int bottom)
    {
        top = Math.Clamp(top, 0, Rows - 1);
        bottom = Math.Clamp(bottom, 0, Rows - 1);
        if (bottom <= top)
        {
            return;
        }

        ScrollTop = top;
        ScrollBottom = bottom;
        MoveCursorTo(0, 0);
    }

    public void ResetScrollRegion()
    {
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
    }

    public void EraseInLine(EraseMode mode)
    {
        var (from, to) = mode switch
        {
            EraseMode.ToEnd => (CursorColumn, Columns),
            EraseMode.ToStart => (0, CursorColumn + 1),
            _ => (0, Columns),
        };
        FillBlank(CursorRow * Columns + from, to - from);
    }

    public void EraseInDisplay(EraseMode mode)
    {
        var cursor = CursorRow * Columns + CursorColumn;
        switch (mode)
        {
            case EraseMode.ToEnd:
                FillBlank(cursor, _cells.Length - cursor);
                break;
            case EraseMode.ToStart:
                FillBlank(0, cursor + 1);
                break;
            default:
                FillBlank(0, _cells.Length);
                break;
        }
    }

    /// <summary>Forgets all scrollback lines (ED 3).</summary>
    public void ClearScrollback()
    {
        Array.Clear(_scrollback);
        _scrollbackStart = 0;
        ScrollbackCount = 0;
        Version++;
    }

    /// <summary>Blanks <paramref name="count"/> cells from the cursor, stopping at the end of the line.</summary>
    public void EraseCharacters(int count) =>
        FillBlank(CursorRow * Columns + CursorColumn, Math.Clamp(count, 0, Columns - CursorColumn));

    /// <summary>Inserts blanks at the cursor, pushing the rest of the line right (characters falling off the end are lost).</summary>
    public void InsertCharacters(int count)
    {
        count = Math.Clamp(count, 0, Columns - CursorColumn);
        var start = CursorRow * Columns + CursorColumn;
        Array.Copy(_cells, start, _cells, start + count, Columns - CursorColumn - count);
        FillBlank(start, count);
    }

    /// <summary>Deletes characters at the cursor, pulling the rest of the line left and blanking the end.</summary>
    public void DeleteCharacters(int count)
    {
        count = Math.Clamp(count, 0, Columns - CursorColumn);
        var start = CursorRow * Columns + CursorColumn;
        Array.Copy(_cells, start + count, _cells, start, Columns - CursorColumn - count);
        FillBlank((CursorRow + 1) * Columns - count, count);
    }

    /// <summary>Inserts blank lines at the cursor row, pushing lines below it down to the bottom margin. Ignored outside the scroll region.</summary>
    public void InsertLines(int count)
    {
        if (CursorRow >= ScrollTop && CursorRow <= ScrollBottom)
        {
            ScrollRegionDown(CursorRow, ScrollBottom, count);
            MoveCursorTo(0, CursorRow);
        }
    }

    /// <summary>Deletes lines at the cursor row, pulling lines up from the bottom margin. Ignored outside the scroll region.</summary>
    public void DeleteLines(int count)
    {
        if (CursorRow >= ScrollTop && CursorRow <= ScrollBottom)
        {
            ScrollRegionUp(CursorRow, ScrollBottom, count, keepHistory: false);
            MoveCursorTo(0, CursorRow);
        }
    }

    /// <summary>Scrolls the scroll region up; new blank lines appear at the bottom margin.</summary>
    public void ScrollUp(int count) => ScrollRegionUp(ScrollTop, ScrollBottom, count, keepHistory: true);

    /// <summary>Scrolls the scroll region down; new blank lines appear at the top margin.</summary>
    public void ScrollDown(int count) => ScrollRegionDown(ScrollTop, ScrollBottom, count);

    /// <summary>
    /// Switches to the alternate screen (blanked) or back to the main screen, whose content was kept.
    /// The cursor position is not changed; VT 1049 saves and restores it around the switch.
    /// </summary>
    public void UseAlternateScreen(bool alternate)
    {
        if (alternate == IsAlternateScreen)
        {
            return;
        }

        IsAlternateScreen = alternate;
        if (alternate)
        {
            _alternateCells ??= new Cell[_mainCells.Length];
            _cells = _alternateCells;
            Array.Fill(_cells, BlankCell);
        }
        else
        {
            _cells = _mainCells;
        }

        ResetScrollRegion();
        MoveCursorTo(CursorColumn, CursorRow);
    }

    /// <summary>
    /// Changes the screen size. Text is not re-wrapped. When the screen gets shorter, lines above the cursor
    /// move into the scrollback so the cursor line stays visible. The scroll region is reset.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        if (columns == Columns && rows == Rows)
        {
            return;
        }

        var drop = Math.Max(0, CursorRow + 1 - rows);
        var mainDrop = IsAlternateScreen ? Math.Max(0, Rows - rows) : drop;

        for (var row = 0; row < mainDrop; row++)
        {
            PushScrollback(_mainCells.AsSpan(row * Columns, Columns));
        }

        _mainCells = ResizeCells(_mainCells, mainDrop, columns, rows);
        if (_alternateCells is not null)
        {
            _alternateCells = ResizeCells(_alternateCells, IsAlternateScreen ? drop : 0, columns, rows);
        }

        _cells = IsAlternateScreen ? _alternateCells! : _mainCells;
        var cursorRow = CursorRow - drop;
        Columns = columns;
        Rows = rows;
        ResetScrollRegion();
        MoveCursorTo(CursorColumn, cursorRow);
    }

    /// <summary>
    /// The cell at <paramref name="column"/> on absolute line <paramref name="line"/>: a scrollback line when
    /// <c>line &lt; LinesScrolledOff</c>, otherwise a screen row. Positions outside the text read as blanks.
    /// </summary>
    public Cell GetCell(int column, long line)
    {
        var row = line - LinesScrolledOff;
        if (row >= 0)
        {
            return row < Rows && column >= 0 && column < Columns ? _cells[row * Columns + column] : Cell.Blank(Foreground);
        }

        var index = ScrollbackCount + row;
        if (index < 0 || column < 0)
        {
            return Cell.Blank(Foreground);
        }

        var cells = _scrollback[(_scrollbackStart + index) % ScrollbackLimit];
        return column < cells.Length ? cells[column] : Cell.Blank(Foreground);
    }

    /// <summary>The cell shown at a screen position when the view is scrolled back by <paramref name="scrollOffset"/> lines.</summary>
    public Cell GetViewCell(int column, int row, int scrollOffset) => GetCell(column, LinesScrolledOff + row - scrollOffset);

    /// <summary>
    /// The text between two positions (both inclusive), one line per row. Trailing spaces are trimmed
    /// from each line and lines are separated by CR LF, which is what Windows programs expect when pasting.
    /// </summary>
    public string GetText(BufferPosition start, BufferPosition end)
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        var lines = new List<string>();
        for (var line = start.Line; line <= end.Line; line++)
        {
            var from = line == start.Line ? start.Column : 0;
            var to = line == end.Line ? end.Column : Math.Max(Columns, LineWidth(line)) - 1;
            var chars = new char[Math.Max(0, to - from + 1)];
            for (var column = from; column <= to; column++)
            {
                chars[column - from] = GetCell(column, line).Char;
            }

            lines.Add(new string(chars).TrimEnd());
        }

        return string.Join("\r\n", lines);
    }

    private int LineWidth(long line)
    {
        var index = ScrollbackCount + (line - LinesScrolledOff);
        return line < LinesScrolledOff && index >= 0 ? _scrollback[(_scrollbackStart + index) % ScrollbackLimit].Length : Columns;
    }

    private Cell[] ResizeCells(Cell[] cells, int dropRows, int columns, int rows)
    {
        var resized = new Cell[columns * rows];
        Array.Fill(resized, Cell.Blank(Foreground));
        var copyColumns = Math.Min(columns, Columns);
        for (var row = 0; row < rows && row + dropRows < Rows; row++)
        {
            Array.Copy(cells, (row + dropRows) * Columns, resized, row * columns, copyColumns);
        }

        return resized;
    }

    private void ScrollRegionUp(int top, int bottom, int count, bool keepHistory)
    {
        var height = bottom - top + 1;
        count = Math.Clamp(count, 0, height);
        if (count == 0)
        {
            return;
        }

        if (keepHistory && top == 0 && !IsAlternateScreen)
        {
            for (var row = 0; row < count; row++)
            {
                PushScrollback(_cells.AsSpan(row * Columns, Columns));
            }
        }

        Array.Copy(_cells, (top + count) * Columns, _cells, top * Columns, (height - count) * Columns);
        FillBlank((bottom + 1 - count) * Columns, count * Columns);
    }

    private void ScrollRegionDown(int top, int bottom, int count)
    {
        var height = bottom - top + 1;
        count = Math.Clamp(count, 0, height);
        Array.Copy(_cells, top * Columns, _cells, (top + count) * Columns, (height - count) * Columns);
        FillBlank(top * Columns, count * Columns);
    }

    private void PushScrollback(ReadOnlySpan<Cell> line)
    {
        LinesScrolledOff++;
        if (ScrollbackLimit == 0)
        {
            return;
        }

        if (ScrollbackCount < ScrollbackLimit)
        {
            _scrollback[(_scrollbackStart + ScrollbackCount) % ScrollbackLimit] = line.ToArray();
            ScrollbackCount++;
        }
        else
        {
            _scrollback[_scrollbackStart] = line.ToArray();
            _scrollbackStart = (_scrollbackStart + 1) % ScrollbackLimit;
        }
    }

    private void FillBlank(int start, int length)
    {
        Array.Fill(_cells, BlankCell, start, length);
        Version++;
    }
}

/// <summary>A cell position using an absolute line number (see <see cref="ScreenBuffer.GetCell(int, long)"/>).</summary>
public readonly record struct BufferPosition(long Line, int Column) : IComparable<BufferPosition>
{
    public int CompareTo(BufferPosition other) => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);

    public static bool operator <(BufferPosition a, BufferPosition b) => a.CompareTo(b) < 0;

    public static bool operator >(BufferPosition a, BufferPosition b) => a.CompareTo(b) > 0;

    public static bool operator <=(BufferPosition a, BufferPosition b) => a.CompareTo(b) <= 0;

    public static bool operator >=(BufferPosition a, BufferPosition b) => a.CompareTo(b) >= 0;
}

/// <summary>A selection from where the mouse went down (<see cref="Anchor"/>) to where it is now (<see cref="Active"/>).</summary>
public readonly record struct TextSelection(BufferPosition Anchor, BufferPosition Active)
{
    public BufferPosition Start => Anchor <= Active ? Anchor : Active;

    public BufferPosition End => Anchor <= Active ? Active : Anchor;

    public bool Contains(int column, long line)
    {
        var position = new BufferPosition(line, column);
        return position >= Start && position <= End;
    }
}
