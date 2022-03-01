using System.Net;
using System.Text;

using Microsoft.AspNetCore.Components;

namespace SarsaWidgets;

public static class FormatUtils
{

    public static MarkupString FormatLabel(string? label)
    {
        if (label == null)
        {
            return new MarkupString("");
        }

        StringBuilder wholeBuffer = new();
        StringBuilder tokenBuffer = new();

        for (int i = 0; i < label.Length; i++)
        {
            char c = label[i];
            if (c == '_')
            {
                i++;
                if (i < label.Length)
                {
                    if (label[i] == '{')
                    {
                        for (i++; i < label.Length && label[i] != '}'; i++)
                        {
                            tokenBuffer.Append(label[i]);
                        }
                    }
                    else
                    {
                        tokenBuffer.Append(label[i]);
                        for (i++; i < label.Length && !CharEndsSub(label[i]); i++)
                        {
                            tokenBuffer.Append(label[i]);
                        }
                        i--; // For consideration on next pass.
                    }
                }
                wholeBuffer.Append("<sub>" + FormatLabel(tokenBuffer.ToString()) + "</sub> ");
                tokenBuffer.Clear();
            }
            else
            {
                wholeBuffer.Append(WebUtility.HtmlEncode(c.ToString()));
            }
        }

        return new MarkupString(wholeBuffer.ToString());
    }

    private static bool CharEndsSub(char c) => char.IsWhiteSpace(c) || c == '[' || c == ']' || c == '(' || c == ')' || c == ',' || c == '>';

}
