using System;
using System.Collections.Generic;

namespace Cascode.Cli;

internal sealed class CharReadViewState
{
    public CharReadViewState(
        string title,
        string subtitle,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, IReadOnlyList<double>> sparklines,
        string sourcePath
    )
    {
        Title = title;
        Subtitle = subtitle;
        Headers = headers;
        Rows = rows;
        Sparklines = sparklines;
        SourcePath = sourcePath;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<double>> Sparklines { get; }
    public string SourcePath { get; }

    public static readonly CharReadViewState Empty = new(
        string.Empty,
        string.Empty,
        Array.Empty<string>(),
        Array.Empty<IReadOnlyList<string>>(),
        new Dictionary<string, IReadOnlyList<double>>(),
        string.Empty
    );
}
