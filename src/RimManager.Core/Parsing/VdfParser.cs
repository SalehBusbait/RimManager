namespace RimManager.Core.Parsing;

/// <summary>
/// A node in a Valve KeyValues (VDF) document. Either a leaf with a
/// <see cref="Value"/> or a container with <see cref="Children"/>.
/// </summary>
public sealed class VdfNode
{
    public string? Value { get; init; }
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLeaf => Value is not null;

    public VdfNode? this[string key] => Children.TryGetValue(key, out var n) ? n : null;
}

/// <summary>
/// Minimal recursive parser for Valve's text KeyValues format, enough to read
/// <c>libraryfolders.vdf</c>. Handles quoted keys/values, nested <c>{ }</c>
/// blocks, escaped <c>\\</c> and <c>\"</c>, and <c>//</c> line comments.
/// </summary>
public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        int pos = 0;
        var root = new VdfNode();
        ParseBlockBody(text, ref pos, root);
        return root;
    }

    private static void ParseBlockBody(string s, ref int pos, VdfNode container)
    {
        while (true)
        {
            SkipTrivia(s, ref pos);
            if (pos >= s.Length) return;

            if (s[pos] == '}')
            {
                pos++; // consume the closing brace of this block
                return;
            }

            var key = ReadToken(s, ref pos);
            SkipTrivia(s, ref pos);
            if (pos >= s.Length)
            {
                // key with no value at EOF — ignore.
                return;
            }

            if (s[pos] == '{')
            {
                pos++; // consume '{'
                var child = new VdfNode();
                ParseBlockBody(s, ref pos, child);
                container.Children[key] = child;
            }
            else
            {
                var value = ReadToken(s, ref pos);
                container.Children[key] = new VdfNode { Value = value };
            }
        }
    }

    private static void SkipTrivia(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            char c = s[pos];
            if (char.IsWhiteSpace(c))
            {
                pos++;
            }
            else if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
            }
            else
            {
                return;
            }
        }
    }

    private static string ReadToken(string s, ref int pos)
    {
        if (pos < s.Length && s[pos] == '"')
        {
            return ReadQuoted(s, ref pos);
        }

        // Unquoted token: read until whitespace or a brace.
        int start = pos;
        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '{' && s[pos] != '}')
        {
            pos++;
        }

        return s[start..pos];
    }

    private static string ReadQuoted(string s, ref int pos)
    {
        pos++; // consume opening quote
        var sb = new System.Text.StringBuilder();
        while (pos < s.Length)
        {
            char c = s[pos++];
            if (c == '\\' && pos < s.Length)
            {
                char next = s[pos++];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    _ => next, // covers \\ and \"
                });
            }
            else if (c == '"')
            {
                break;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
