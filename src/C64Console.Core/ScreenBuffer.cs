namespace C64Console.Core;

/// <summary>Which part of a line or of the screen an erase covers (the VT "Ps" parameter).</summary>
public enum EraseMode
{
    ToEnd = 0,
    ToStart = 1,
    All = 2,
}

/// <summary>
/// A fixed grid of character cells with a cursor, behaving like a VT terminal screen.
/// Writing in the last column sets a "wrap pending" flag instead of moving to the next line straight away;
/// the wrap happens only if another character follows. ConPTY relies on this.
/// Line feeds on the last row scroll the screen up.
/// </summary>
public sealed class ScreenBuffer
{
    private readonly Cell[] _cells;

    public ScreenBuffer(int columns, int rows, byte foreground)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);

        Columns = columns;
        Rows = rows;
        Foreground = foreground;
        _cells = new Cell[columns * rows];
        Clear();
    }

    public int Columns { get; }
    public int Rows { get; }
    public int CursorColumn { get; private set; }
    public int CursorRow { get; private set; }

    /// <summary>True after writing in the last column: the next character goes to the start of the next line.</summary>
    public bool WrapPending { get; private set; }

    /// <summary>Colour used for characters written from now on.</summary>
    public byte Foreground { get; set; }

    /// <summary>Whether characters written from now on are shown in reverse video.</summary>
    public bool Reverse { get; set; }

    /// <summary>Incremented on every change, so a renderer can skip unchanged frames.</summary>
    public int Version { get; private set; }

    public Cell this[int column, int row] => _cells[row * Columns + column];

    private Cell BlankCell => Cell.Blank(Foreground);

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

        _cells[CursorRow * Columns + CursorColumn] = new Cell(c, Foreground, Reverse);
        if (CursorColumn == Columns - 1)
        {
            WrapPending = true;
        }
        else
        {
            CursorColumn++;
        }

        Version++;
    }

    public void CarriageReturn() => MoveCursorTo(0, CursorRow);

    /// <summary>Moves down one row, scrolling up when already on the last row. The column is unchanged.</summary>
    public void LineFeed()
    {
        if (CursorRow == Rows - 1)
        {
            ScrollUp(1);
            MoveCursorTo(CursorColumn, CursorRow);
        }
        else
        {
            MoveCursorTo(CursorColumn, CursorRow + 1);
        }
    }

    /// <summary>Moves up one row, scrolling down when already on the first row.</summary>
    public void ReverseIndex()
    {
        if (CursorRow == 0)
        {
            ScrollDown(1);
            MoveCursorTo(CursorColumn, 0);
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

    /// <summary>Inserts blank lines at the cursor row, pushing lines below it down.</summary>
    public void InsertLines(int count) => ScrollDown(CursorRow, count);

    /// <summary>Deletes lines at the cursor row, pulling lines below it up.</summary>
    public void DeleteLines(int count) => ScrollUp(CursorRow, count);

    /// <summary>Scrolls the whole screen up; new blank lines appear at the bottom.</summary>
    public void ScrollUp(int count) => ScrollUp(0, count);

    /// <summary>Scrolls the whole screen down; new blank lines appear at the top.</summary>
    public void ScrollDown(int count) => ScrollDown(0, count);

    private void ScrollUp(int top, int count)
    {
        count = Math.Clamp(count, 0, Rows - top);
        Array.Copy(_cells, (top + count) * Columns, _cells, top * Columns, (Rows - top - count) * Columns);
        FillBlank((Rows - count) * Columns, count * Columns);
    }

    private void ScrollDown(int top, int count)
    {
        count = Math.Clamp(count, 0, Rows - top);
        Array.Copy(_cells, top * Columns, _cells, (top + count) * Columns, (Rows - top - count) * Columns);
        FillBlank(top * Columns, count * Columns);
    }

    private void FillBlank(int start, int length)
    {
        Array.Fill(_cells, BlankCell, start, length);
        Version++;
    }
}
