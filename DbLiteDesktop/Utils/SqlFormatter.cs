using System.Text;
using System.Text.RegularExpressions;

namespace DbLiteDesktop.Utils;

public static class SqlFormatter
{
    private static readonly Regex TokenRegex = new(
        @"('(?:[^']|'')*')|" +
        @"(--[^\r\n]*)|" +
        @"(/\*[\s\S]*?\*/)|" +
        @"([(),;])|" +
        @"([^\s(),;']+)",
        RegexOptions.Compiled
    );

    private static readonly HashSet<string> MajorClauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "DISTINCT", "FROM", "WHERE", "GROUP", "ORDER", "HAVING",
        "LIMIT", "OFFSET", "UNION", "INTERSECT", "EXCEPT", "RETURNING",
        "WITH", "INSERT", "UPDATE", "DELETE", "VALUES", "SET"
    };

    private static readonly HashSet<string> JoinKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "NATURAL"
    };

    public static string Format(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return string.Empty;
        }

        var tokens = Tokenize(sql);
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var indent = 0;
        var expectColumnList = false;
        var inWhereClause = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var upper = token.ToUpperInvariant();
            var next = i + 1 < tokens.Count ? tokens[i + 1].ToUpperInvariant() : string.Empty;
            var prev = i > 0 ? tokens[i - 1].ToUpperInvariant() : string.Empty;

            if (IsStringOrComment(token))
            {
                sb.Append(' ').Append(token);
                continue;
            }

            if (upper == "(")
            {
                sb.Append(" (");
                indent++;
                continue;
            }

            if (upper == ")")
            {
                indent = Math.Max(0, indent - 1);
                sb.Append(" )");
                continue;
            }

            if (upper == ",")
            {
                if (expectColumnList)
                {
                    sb.Append(",\n").Append(new string(' ', indent * 4 + 7));
                }
                else
                {
                    sb.Append(", ");
                }
                continue;
            }

            if (upper == ";")
            {
                sb.Append(";\n");
                continue;
            }

            if (IsClauseBoundary(upper, next, prev))
            {
                var clauseText = upper == "GROUP" && next == "BY" ? "GROUP BY" :
                                 upper == "ORDER" && next == "BY" ? "ORDER BY" :
                                 upper;

                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                indent = 0;
                inWhereClause = upper == "WHERE";

                sb.Append(clauseText).Append(' ');

                if (upper == "GROUP" || upper == "ORDER")
                {
                    i++;
                }

                if (upper == "SELECT")
                {
                    expectColumnList = true;
                }
                else
                {
                    expectColumnList = false;
                }
                continue;
            }

            if (JoinKeywords.Contains(upper))
            {
                var (joinPhrase, consumed) = ReadJoinPhrase(tokens, i);
                if (sb.Length > 0 && !EndsWithNewline(sb))
                {
                    sb.AppendLine();
                }
                indent = 0;
                sb.Append(joinPhrase).Append(' ');
                i += consumed;
                continue;
            }

            if ((upper == "AND" || upper == "OR") && inWhereClause)
            {
                sb.AppendLine();
                sb.Append("  ").Append(upper).Append(' ');
                continue;
            }

            if (upper == "ON")
            {
                sb.Append("ON ");
                continue;
            }

            if (upper == "BY")
            {
                continue;
            }

            sb.Append(upper).Append(' ');
        }

        var result = sb.ToString().Trim();
        result = Regex.Replace(result, @" +", " ");
        result = result.Replace(" \n", "\n").Replace("( ", "(").Trim();
        if (result.Length > 0 && !result.EndsWith(";"))
        {
            result += ";";
        }
        return result;
    }

    private static bool IsClauseBoundary(string upper, string next, string prev)
    {
        if (!MajorClauses.Contains(upper))
        {
            return false;
        }

        if (upper == "BY" && (prev == "GROUP" || prev == "ORDER"))
        {
            return false;
        }

        if (upper == "GROUP" || upper == "ORDER")
        {
            return next == "BY";
        }

        if (upper == "INSERT" && next == "INTO")
        {
            return true;
        }

        if (upper == "INTO" && prev == "INSERT")
        {
            return false;
        }

        if (upper == "DELETE" && next == "FROM")
        {
            return true;
        }

        return true;
    }

    private static (string phrase, int consumed) ReadJoinPhrase(List<string> tokens, int start)
    {
        var parts = new List<string>();
        var i = start;
        var consumed = 0;
        while (i < tokens.Count && JoinKeywords.Contains(tokens[i].ToUpperInvariant()))
        {
            parts.Add(tokens[i].ToUpperInvariant());
            i++;
            consumed++;
        }
        return (string.Join(' ', parts), consumed - 1);
    }

    private static List<string> Tokenize(string sql)
    {
        var tokens = new List<string>();
        foreach (Match m in TokenRegex.Matches(sql))
        {
            if (!string.IsNullOrEmpty(m.Value))
            {
                tokens.Add(m.Value);
            }
        }
        return tokens;
    }

    private static bool IsStringOrComment(string token)
    {
        if (token.Length == 0) return false;
        return token[0] == '\'' || token.StartsWith("--") || token.StartsWith("/*");
    }

    private static bool EndsWithNewline(StringBuilder sb)
    {
        if (sb.Length == 0) return true;
        var last = sb[sb.Length - 1];
        return last == '\n';
    }
}
