using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PostProcessor.Core.Templating;

/// <summary>
/// 模板渲染器：支持 {{变量}}、{{IF}}/{{ELSE}}/{{ENDIF}}、简单算式与格式化。
/// </summary>
internal static class TemplateRenderer
{
    private static readonly Regex TokenRegex = new(@"\{\{(.*?)\}\}", RegexOptions.Compiled);

    /// <summary>
    /// 渲染指定区块：逐行处理、条件判断、变量替换、空行过滤。
    /// </summary>
    public static void AppendSection(List<string> output, IReadOnlyList<string> lines, Dictionary<string, string> context)
    {
        var conditionStack = new Stack<bool>();
        var falseCount = 0;

        foreach (var rawLine in lines)
        {
            // 1) 处理 IF/ELSE/ENDIF
            if (TryParseDirective(rawLine, out var directive, out var expression))
            {
                switch (directive)
                {
                    case TemplateDirective.If:
                        var result = EvaluateCondition(expression, context);
                        conditionStack.Push(result);
                        if (!result)
                        {
                            falseCount++;
                        }
                        break;
                    case TemplateDirective.Else:
                        if (conditionStack.Count == 0)
                        {
                            break;
                        }
                        var previous = conditionStack.Pop();
                        if (!previous)
                        {
                            falseCount--;
                        }
                        var current = !previous;
                        conditionStack.Push(current);
                        if (!current)
                        {
                            falseCount++;
                        }
                        break;
                    case TemplateDirective.EndIf:
                        if (conditionStack.Count == 0)
                        {
                            break;
                        }
                        var popped = conditionStack.Pop();
                        if (!popped)
                        {
                            falseCount--;
                        }
                        break;
                }
                continue;
            }

            // 当前在 false 分支内，跳过
            if (falseCount > 0)
            {
                continue;
            }

            // 2) 变量替换并清理空白
            var line = ReplaceTokens(rawLine, context);
            line = CollapseSpaces(line).Trim();
            if (line.Length == 0)
            {
                continue;
            }
            output.Add(line);
        }
    }

    private static string ReplaceTokens(string input, Dictionary<string, string> context)
    {
        return TokenRegex.Replace(input, match => EvaluateToken(match.Groups[1].Value, context, match.Value));
    }

    private enum TemplateDirective
    {
        If,
        Else,
        EndIf
    }

    /// <summary>
    /// 解析模板指令：{{IF ...}} / {{ELSE}} / {{ENDIF}}
    /// </summary>
    private static bool TryParseDirective(string rawLine, out TemplateDirective directive, out string expression)
    {
        directive = TemplateDirective.If;
        expression = string.Empty;

        var trimmed = rawLine.Trim();
        if (!trimmed.StartsWith("{{", StringComparison.Ordinal) || !trimmed.EndsWith("}}", StringComparison.Ordinal))
        {
            return false;
        }

        var inner = trimmed[2..^2].Trim();
        if (inner.StartsWith("IF ", StringComparison.OrdinalIgnoreCase))
        {
            directive = TemplateDirective.If;
            expression = inner[3..].Trim();
            return true;
        }

        if (inner.Equals("ELSE", StringComparison.OrdinalIgnoreCase))
        {
            directive = TemplateDirective.Else;
            return true;
        }

        if (inner.Equals("ENDIF", StringComparison.OrdinalIgnoreCase))
        {
            directive = TemplateDirective.EndIf;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 条件表达式求值，支持比较运算与布尔判断。
    /// </summary>
    private static bool EvaluateCondition(string expression, Dictionary<string, string> context)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var expr = expression.Trim();
        var op = FindOperator(expr, out var opIndex);
        if (op != null)
        {
            var left = expr[..opIndex].Trim();
            var right = expr[(opIndex + op.Length)..].Trim();

            var leftValue = ResolveValue(left, context);
            var rightValue = ResolveValue(right, context);

            var leftIsNumber = double.TryParse(leftValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber);
            var rightIsNumber = double.TryParse(rightValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber);

            if (op is ">" or "<" or ">=" or "<=")
            {
                if (!leftIsNumber || !rightIsNumber)
                {
                    return false;
                }

                return op switch
                {
                    ">" => leftNumber > rightNumber,
                    "<" => leftNumber < rightNumber,
                    ">=" => leftNumber >= rightNumber,
                    "<=" => leftNumber <= rightNumber,
                    _ => false
                };
            }

            if (leftIsNumber && rightIsNumber)
            {
                var equals = NearlyEqual(leftNumber, rightNumber);
                return op == "==" ? equals : !equals;
            }

            var equalsText = string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase);
            return op == "==" ? equalsText : !equalsText;
        }

        var value = ResolveValue(expr, context);
        return IsTruthy(value);
    }

    private static string? FindOperator(string expression, out int index)
    {
        var ops = new[] { "==", "!=", ">=", "<=", ">", "<" };
        foreach (var op in ops)
        {
            var idx = expression.IndexOf(op, StringComparison.Ordinal);
            if (idx >= 0)
            {
                index = idx;
                return op;
            }
        }

        index = -1;
        return null;
    }

    private static string ResolveValue(string token, Dictionary<string, string> context)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var trimmed = token.Trim();
        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal) && trimmed.Length >= 2) ||
            (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal) && trimmed.Length >= 2))
        {
            return trimmed[1..^1];
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return trimmed;
        }

        return context.TryGetValue(trimmed, out var value) ? value : string.Empty;
    }

    private static bool IsTruthy(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return Math.Abs(number) > 1e-9;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !value.Equals("0", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 支持模板中带前缀数值的格式化（如 X12.3）。
    /// </summary>
    private static bool TryParsePrefixedNumber(string text, out string prefix, out double number)
    {
        prefix = string.Empty;
        number = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var i = 0;
        while (i < text.Length && !char.IsDigit(text[i]) && text[i] != '-' && text[i] != '+' && text[i] != '.')
        {
            prefix += text[i];
            i++;
        }

        if (i >= text.Length)
        {
            return false;
        }

        var numberText = text[i..];
        return double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static string EvaluateToken(string token, Dictionary<string, string> context, string original)
    {
        var trimmed = token.Trim();
        if (trimmed.Length == 0)
        {
            return original;
        }

        // 公式计算：{{=表达式}}
        if (trimmed.StartsWith("=", StringComparison.Ordinal))
        {
            var expr = trimmed[1..].Trim();
            expr = SplitExpressionAndFormat(expr, out var format);
            if (EvaluateExpression(expr, context, out var value))
            {
                return format == null ? FormatDoubleValue(value) : value.ToString(format, CultureInfo.InvariantCulture);
            }
            return string.Empty;
        }

        var name = trimmed;
        string? formatSpec = null;
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex > 0)
        {
            name = trimmed[..colonIndex].Trim();
            formatSpec = trimmed[(colonIndex + 1)..].Trim();
        }

        if (!context.TryGetValue(name, out var valueText))
        {
            return original;
        }

        if (!string.IsNullOrWhiteSpace(formatSpec))
        {
            if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return number.ToString(formatSpec, CultureInfo.InvariantCulture);
            }

            if (TryParsePrefixedNumber(valueText, out var prefix, out var prefixedNumber))
            {
                return prefix + prefixedNumber.ToString(formatSpec, CultureInfo.InvariantCulture);
            }
        }

        return valueText;
    }

    private static string SplitExpressionAndFormat(string expression, out string? format)
    {
        format = null;
        var depth = 0;
        for (var i = expression.Length - 1; i >= 0; i--)
        {
            var ch = expression[i];
            if (ch == ')')
            {
                depth++;
                continue;
            }

            if (ch == '(')
            {
                depth--;
                continue;
            }

            if (ch == ':' && depth == 0)
            {
                format = expression[(i + 1)..].Trim();
                return expression[..i].Trim();
            }
        }

        return expression;
    }

    /// <summary>
    /// 简易表达式求值：支持 + - * / 与括号。
    /// </summary>
    private static bool EvaluateExpression(string expression, Dictionary<string, string> context, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        if (!TryTokenizeExpression(expression, context, out var tokens))
        {
            return false;
        }

        if (!TryToRpn(tokens, out var rpn))
        {
            return false;
        }

        return TryEvalRpn(rpn, out value);
    }

    private enum ExprTokenType
    {
        Number,
        Operator,
        LParen,
        RParen
    }

    private readonly struct ExprToken
    {
        public ExprTokenType Type { get; }
        public double Number { get; }
        public string Operator { get; }

        private ExprToken(ExprTokenType type, double number, string op)
        {
            Type = type;
            Number = number;
            Operator = op;
        }

        public static ExprToken Num(double value) => new(ExprTokenType.Number, value, string.Empty);
        public static ExprToken Op(string op) => new(ExprTokenType.Operator, 0, op);
        public static ExprToken LParenToken() => new(ExprTokenType.LParen, 0, string.Empty);
        public static ExprToken RParenToken() => new(ExprTokenType.RParen, 0, string.Empty);
    }

    private static bool TryTokenizeExpression(string expression, Dictionary<string, string> context, out List<ExprToken> tokens)
    {
        tokens = new List<ExprToken>();
        var i = 0;
        ExprTokenType? prevType = null;

        while (i < expression.Length)
        {
            var ch = expression[i];
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            var isUnary = prevType == null || prevType == ExprTokenType.Operator || prevType == ExprTokenType.LParen;

            if ((ch == '+' || ch == '-') && isUnary)
            {
                var next = i + 1 < expression.Length ? expression[i + 1] : '\0';
                if (char.IsDigit(next) || next == '.')
                {
                    var start = i;
                    i++;
                    while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                    {
                        i++;
                    }
                    var numberText = expression[start..i];
                    if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        return false;
                    }
                    tokens.Add(ExprToken.Num(number));
                    prevType = ExprTokenType.Number;
                    continue;
                }
                if (ch == '-')
                {
                    tokens.Add(ExprToken.Op("u-"));
                    prevType = ExprTokenType.Operator;
                }
                i++;
                continue;
            }

            if (char.IsDigit(ch) || ch == '.')
            {
                var start = i;
                i++;
                while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                {
                    i++;
                }
                var numberText = expression[start..i];
                if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return false;
                }
                tokens.Add(ExprToken.Num(number));
                prevType = ExprTokenType.Number;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = i;
                i++;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
                {
                    i++;
                }
                var name = expression[start..i];
                if (!context.TryGetValue(name, out var valueText))
                {
                    return false;
                }
                if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return false;
                }
                tokens.Add(ExprToken.Num(number));
                prevType = ExprTokenType.Number;
                continue;
            }

            if (ch == '(')
            {
                tokens.Add(ExprToken.LParenToken());
                prevType = ExprTokenType.LParen;
                i++;
                continue;
            }

            if (ch == ')')
            {
                tokens.Add(ExprToken.RParenToken());
                prevType = ExprTokenType.RParen;
                i++;
                continue;
            }

            if (ch is '+' or '-' or '*' or '/')
            {
                tokens.Add(ExprToken.Op(ch.ToString()));
                prevType = ExprTokenType.Operator;
                i++;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryToRpn(List<ExprToken> tokens, out List<ExprToken> output)
    {
        output = new List<ExprToken>();
        var ops = new Stack<ExprToken>();

        foreach (var token in tokens)
        {
            if (token.Type == ExprTokenType.Number)
            {
                output.Add(token);
                continue;
            }

            if (token.Type == ExprTokenType.Operator)
            {
                var prec = GetPrecedence(token.Operator);
                var rightAssoc = token.Operator == "u-";
                while (ops.Count > 0 && ops.Peek().Type == ExprTokenType.Operator)
                {
                    var top = ops.Peek();
                    var topPrec = GetPrecedence(top.Operator);
                    if (topPrec > prec || (!rightAssoc && topPrec == prec))
                    {
                        output.Add(ops.Pop());
                        continue;
                    }
                    break;
                }
                ops.Push(token);
                continue;
            }

            if (token.Type == ExprTokenType.LParen)
            {
                ops.Push(token);
                continue;
            }

            if (token.Type == ExprTokenType.RParen)
            {
                var found = false;
                while (ops.Count > 0)
                {
                    var top = ops.Pop();
                    if (top.Type == ExprTokenType.LParen)
                    {
                        found = true;
                        break;
                    }
                    output.Add(top);
                }
                if (!found)
                {
                    return false;
                }
            }
        }

        while (ops.Count > 0)
        {
            var top = ops.Pop();
            if (top.Type is ExprTokenType.LParen or ExprTokenType.RParen)
            {
                return false;
            }
            output.Add(top);
        }

        return true;
    }

    private static bool TryEvalRpn(List<ExprToken> rpn, out double value)
    {
        value = 0;
        var stack = new Stack<double>();
        foreach (var token in rpn)
        {
            if (token.Type == ExprTokenType.Number)
            {
                stack.Push(token.Number);
                continue;
            }

            if (token.Type == ExprTokenType.Operator)
            {
                if (token.Operator == "u-")
                {
                    if (stack.Count < 1)
                    {
                        return false;
                    }
                    stack.Push(-stack.Pop());
                    continue;
                }

                if (stack.Count < 2)
                {
                    return false;
                }
                var right = stack.Pop();
                var left = stack.Pop();
                var result = token.Operator switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" => right == 0 ? double.NaN : left / right,
                    _ => double.NaN
                };
                stack.Push(result);
            }
        }

        if (stack.Count != 1)
        {
            return false;
        }

        value = stack.Pop();
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static int GetPrecedence(string op)
    {
        return op switch
        {
            "u-" => 3,
            "*" or "/" => 2,
            "+" or "-" => 1,
            _ => 0
        };
    }
    //把一行里连续的空白字符压缩成单个空格，并去掉多余空格。
    private static string CollapseSpaces(string input)
    {
        var builder = new StringBuilder(input.Length);
        var lastWasSpace = false;
        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        return builder.ToString();
    }

    private static bool NearlyEqual(double a, double b)
    {
        return Math.Abs(a - b) <= 1e-6;
    }

    private static string FormatDoubleValue(double value)
        => value.ToString("0.0000", CultureInfo.InvariantCulture);
}
