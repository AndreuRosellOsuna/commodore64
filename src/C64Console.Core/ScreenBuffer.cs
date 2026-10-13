namespace C64Console.Core;

/// <summary>
/// A fixed grid of character cells with a cursor. Writing past the last column wraps to the
/// next line, and writing past the last row scrolls the screen up, like the C64 screen editor.
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

    /// <summary>Colour used for characters written from now on.</summary>
    public byte Foreground { get; set; }

    /// <summary>Whether characters written from now on are shown in reverse video.</summary>
    public bool Reverse { get; set; }

    /// <summary>Incremented on every change, so a renderer can skip unchanged frames.</summary>
    public int Version { get; private set; }

    public Cell this[int column, int row] => _cells[row * Columns + column];

    public void Clear()
    {
        Array.Fill(_cells, Cell.Blank(Foreground));
        CursorColumn = 0;
        CursorRow = 0;
        Version++;
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

    public void Write(char c)
    {
        switch (c)
        {
            case '\n':
                NewLine();
                return;
            case '\r':
                CursorColumn = 0;
                Version++;
                return;
            case '\b':
                Backspace();
                return;
        }

        _cells[CursorRow * Columns + CursorColumn] = new Cell(c, Foreground, Reverse);
        CursorColumn++;
        if (CursorColumn == Columns)
        {
            NewLine();
        }

        Version++;
    }

    /// <summary>Moves the cursor back one cell (to the end of the previous line if needed) and blanks it.</summary>
    public void Backspace()
    {
        if (CursorColumn > 0)
        {
            CursorColumn--;
        }
        else if (CursorRow > 0)
        {
            CursorRow--;
            CursorColumn = Columns - 1;
        }
        else
        {
            return;
        }

        _cells[CursorRow * Columns + CursorColumn] = Cell.Blank(Foreground);
        Version++;
    }

    public void NewLine()
    {
        CursorColumn = 0;
        CursorRow++;
        if (CursorRow == Rows)
        {
            ScrollUp();
            CursorRow = Rows - 1;
        }

        Version++;
    }

    private void ScrollUp()
    {
        Array.Copy(_cells, Columns, _cells, 0, Columns * (Rows - 1));
        Array.Fill(_cells, Cell.Blank(Foreground), Columns * (Rows - 1), Columns);
    }
}
