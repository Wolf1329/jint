using System;
using System.Collections.Generic;
using System.Globalization;
using Esprima;
using Esprima.Ast;
using Jint.Native.Object;
using Jint.Runtime;

namespace Jint.Native.Json
{
    public class JsonParser
    {
        private readonly Engine _engine;

        public JsonParser(Engine engine)
        {
            _engine = engine;
        }

        private Extra _extra;

        private int _index; // position in the stream
        private int _length; // length of the stream
        private int _lineNumber;
        private int _lineStart;
        private Location _location;
        private Token _lookahead;
        private string _source;

        private State _state;

        private static bool IsDecimalDigit(char ch)
        {
            return (ch >= '0' && ch <= '9');
        }

        private static bool IsHexDigit(char ch)
        {
            return
                ch >= '0' && ch <= '9' ||
                ch >= 'a' && ch <= 'f' ||
                ch >= 'A' && ch <= 'F'
                ;
        }

        private static bool IsOctalDigit(char ch)
        {
            return ch >= '0' && ch <= '7';
        }

        private static bool IsWhiteSpace(char ch)
        {
            return (ch == ' ')  ||
                   (ch == '\t') ||
                   (ch == '\n') ||
                   (ch == '\r');
        }

        private static bool IsLineTerminator(char ch)
        {
            return (ch == 10) || (ch == 13) || (ch == 0x2028) || (ch == 0x2029);
        }

        private static bool IsNullChar(char ch)
        {
            return ch == 'n'
                || ch == 'u'
                || ch == 'l'
                || ch == 'l'
                ;
        }

        private static bool IsTrueOrFalseChar(char ch)
        {
            return ch == 't'
                || ch == 'f'
                || ch == 'r'
                || ch == 'a'
                || ch == 'u'
                || ch == 'l'
                || ch == 'e'
                || ch == 's'
                ;
        }

        private char ScanHexEscape(char prefix)
        {
            int code = char.MinValue;

            int len = (prefix == 'u') ? 4 : 2;
            for (int i = 0; i < len; ++i)
            {
                if (_index < _length && IsHexDigit(_source.CharCodeAt(_index)))
                {
                    char ch = _source.CharCodeAt(_index++);
                    code = code * 16 + "0123456789abcdef".IndexOf(ch.ToString(), StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    ThrowError(_index, Messages.ExpectedHexadecimalDigit);
                }
            }
            return (char)code;
        }

        private void SkipWhiteSpace()
        {
            while (_index < _length)
            {
                char ch = _source.CharCodeAt(_index);

                if (IsWhiteSpace(ch))
                {
                    ++_index;
                }
                else
                {
                    break;
                }
            }
        }

        private Token ScanPunctuator()
        {
            int start = _index;
            char code = _source.CharCodeAt(_index);

            switch ((int) code)
            {
                    // Check for most common single-character punctuators.
                case 46: // . dot
                case 40: // ( open bracket
                case 41: // ) close bracket
                case 59: // ; semicolon
                case 44: // , comma
                case 123: // { open curly brace
                case 125: // } close curly brace
                case 91: // [
                case 93: // ]
                case 58: // :
                case 63: // ?
                case 126: // ~
                    ++_index;

                    string value = TypeConverter.ToString(code);
                    return new Token
                    {
                        Type = Tokens.Punctuator,
                        Text = value,
                        Value = value,
                        LineNumber = _lineNumber,
                        LineStart = _lineStart,
                        Range = new[] { start, _index }
                    };
            }

            ThrowError(start, Messages.UnexpectedToken, code);
            return null;
        }

        private Token ScanNumericLiteral()
        {
            char ch = _source.CharCodeAt(_index);

            int start = _index;
            string number = "";

            // Number start with a -
            if (ch == '-')
            {
                number += _source.CharCodeAt(_index++).ToString();
                ch = _source.CharCodeAt(_index);
            }

            if (ch != '.')
            {
                number += _source.CharCodeAt(_index++).ToString();
                ch = _source.CharCodeAt(_index);

                // Hex number starts with '0x'.
                // Octal number starts with '0'.
                if (number == "0")
                {
                    // decimal number starts with '0' such as '09' is illegal.
                    if (ch > 0 && IsDecimalDigit(ch))
                    {
                        ThrowError(_index, Messages.UnexpectedToken, ch);
                    }
                }

                while (IsDecimalDigit(_source.CharCodeAt(_index)))
                {
                    number += _source.CharCodeAt(_index++).ToString();
                }
                ch = _source.CharCodeAt(_index);
            }

            if (ch == '.')
            {
                number += _source.CharCodeAt(_index++).ToString();
                while (IsDecimalDigit(_source.CharCodeAt(_index)))
                {
                    number += _source.CharCodeAt(_index++).ToString();
                }
                ch = _source.CharCodeAt(_index);
            }

            if (ch == 'e' || ch == 'E')
            {
                number += _source.CharCodeAt(_index++).ToString();

                ch = _source.CharCodeAt(_index);
                if (ch == '+' || ch == '-')
                {
                    number += _source.CharCodeAt(_index++).ToString();
                }
                if (IsDecimalDigit(_source.CharCodeAt(_index)))
                {
                    while (IsDecimalDigit(_source.CharCodeAt(_index)))
                    {
                        number += _source.CharCodeAt(_index++).ToString();
                    }
                }
                else
                {
                    ThrowError(_index, Messages.UnexpectedToken, _source.CharCodeAt(_index));
                }
            }

            return new Token
                {
                    Type = Tokens.Number,
                    Text = number,
                    Value = Double.Parse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture),
                    LineNumber = _lineNumber,
                    LineStart = _lineStart,
                    Range = new[] {start, _index}
                };
        }

        private Token ScanBooleanLiteral()
        {
            int start = _index;
            string s = "";

            while (IsTrueOrFalseChar(_source.CharCodeAt(_index)))
            {
                s += _source.CharCodeAt(_index++).ToString();
            }

            if (s == "true" || s == "false")
            {
                return new Token
                {
                    Type = Tokens.BooleanLiteral,
                    Text = s,
                    Value = s == "true",
                    LineNumber = _lineNumber,
                    LineStart = _lineStart,
                    Range = new[] { start, _index }
                };
            }

            ThrowError(start, Messages.UnexpectedToken, s);
            return null;
        }

        private Token ScanNullLiteral()
        {
            int start = _index;
            string s = "";

            while (IsNullChar(_source.CharCodeAt(_index)))
            {
                s += _source.CharCodeAt(_index++).ToString();
            }

            if (s == Null.Text)
            {
                return new Token
                {
                    Type = Tokens.NullLiteral,
                    Text = s,
                    Value = Null.Instance,
                    LineNumber = _lineNumber,
                    LineStart = _lineStart,
                    Range = new[] { start, _index }
                };
            }

            ThrowError(start, Messages.UnexpectedToken, s);
            return null;
        }

        private Token ScanStringLiteral()
        {
            var sb = new System.Text.StringBuilder();

            char quote = _source.CharCodeAt(_index);

            int start = _index;
            ++_index;

            while (_index < _length)
            {
                char ch = _source.CharCodeAt(_index++);

                if (ch == quote)
                {
                    quote = char.MinValue;
                    break;
                }

                if (ch <= 31)
                {
                    ThrowError(_index - 1, Messages.InvalidCharacter);
                }

                if (ch == '\\')
                {
                    ch = _source.CharCodeAt(_index++);

                    if (ch > 0 || !IsLineTerminator(ch))
                    {
                        switch (ch)
                        {
                            case 'n':
                                sb.Append('\n');
                                break;
                            case 'r':
                                sb.Append('\r');
                                break;
                            case 't':
                                sb.Append('\t');
                                break;
                            case 'u':
                            case 'x':
                                int restore = _index;
                                char unescaped = ScanHexEscape(ch);
                                if (unescaped > 0)
                                {
                                    sb.Append(unescaped.ToString());
                                }
                                else
                                {
                                    _index = restore;
                                    sb.Append(ch.ToString());
                                }
                                break;
                            case 'b':
                                sb.Append("\b");
                                break;
                            case 'f':
                                sb.Append("\f");
                                break;
                            case 'v':
                                sb.Append("\x0B");
                                break;

                            default:
                                if (IsOctalDigit(ch))
                                {
                                    int code = "01234567".IndexOf(ch);

                                    if (_index < _length && IsOctalDigit(_source.CharCodeAt(_index)))
                                    {
                                        code = code * 8 + "01234567".IndexOf(_source.CharCodeAt(_index++));

                                        // 3 digits are only allowed when string starts
                                        // with 0, 1, 2, 3
                                        if ("0123".IndexOf(ch) >= 0 &&
                                            _index < _length &&
                                            IsOctalDigit(_source.CharCodeAt(_index)))
                                        {
                                            code = code * 8 + "01234567".IndexOf(_source.CharCodeAt(_index++));
                                        }
                                    }
                                    sb.Append(((char)code).ToString());
                                }
                                else
                                {
                                    sb.Append(ch.ToString());
                                }
                                break;
                        }
                    }
                    else
                    {
                        ++_lineNumber;
                        if (ch == '\r' && _source.CharCodeAt(_index) == '\n')
                        {
                            ++_index;
                        }
                    }
                }
                else if (IsLineTerminator(ch))
                {
                    break;
                }
                else
                {
                    sb.Append(ch.ToString());
                }
            }

            if (quote != 0)
            {
                // unterminated string literal
                ThrowError(_index, Messages.UnexpectedEOS);
            }

            string value = sb.ToString();
            return new Token
            {
                    Type = Tokens.String,
                    Text = value,
                    Value = value,
                    LineNumber = _lineNumber,
                    LineStart = _lineStart,
                    Range = new[] { start, _index }
                };
        }

        private Token Advance()
        {
            SkipWhiteSpace();

            if (_index >= _length)
            {
                return new Token
                    {
                        Type = Tokens.EOF,
                        LineNumber = _lineNumber,
                        LineStart = _lineStart,
                        Range = new[] {_index, _index}
                    };
            }

            char ch = _source.CharCodeAt(_index);

            // Very common: ( and ) and ;
            if (ch == 40 || ch == 41 || ch == 58)
            {
                return ScanPunctuator();
            }

            // String literal starts with double quote (#34).
            // Single quote (#39) are not allowed in JSON.
            if (ch == 34)
            {
                return ScanStringLiteral();
            }

            // Dot (.) char #46 can also start a floating-point number, hence the need
            // to check the next character.
            if (ch == 46)
            {
                if (IsDecimalDigit(_source.CharCodeAt(_index + 1)))
                {
                    return ScanNumericLiteral();
                }
                return ScanPunctuator();
            }

            if (ch == '-') // Negative Number
            {
                if (IsDecimalDigit(_source.CharCodeAt(_index + 1)))
                {
                    return ScanNumericLiteral();
                }
                return ScanPunctuator();
            }

            if (IsDecimalDigit(ch))
            {
                return ScanNumericLiteral();
            }

            if (ch == 't' || ch == 'f')
            {
                return ScanBooleanLiteral();
            }

            if (ch == 'n')
            {
                return ScanNullLiteral();
            }

            return ScanPunctuator();
        }

        private Token CollectToken()
        {
            var start = new Position(
                line: _lineNumber,
                column: _index - _lineStart);

            Token token = Advance();

            var end = new Position(
                line: _lineNumber,
                column: _index - _lineStart);

            _location = new Location(start, end, _source);

            if (token.Type != Tokens.EOF)
            {
                var range = new[] {token.Range[0], token.Range[1]};
                string value = _source.Slice(token.Range[0], token.Range[1]);
                _extra.Tokens.Add(new Token
                    {
                        Type = token.Type,
                        Text = value,
                        Value = value,
                        Range = range,
                    });
            }

            return token;
        }

        private Token Lex()
        {
            Token token = _lookahead;
            _index = token.Range[1];
            _lineNumber = token.LineNumber.HasValue ? token.LineNumber.Value : 0;
            _lineStart = token.LineStart;

            _lookahead = (_extra.Tokens != null) ? CollectToken() : Advance();

            _index = token.Range[1];
            _lineNumber = token.LineNumber.HasValue ? token.LineNumber.Value : 0;
            _lineStart = token.LineStart;

            return token;
        }

        private void Peek()
        {
            int pos = _index;
            int line = _lineNumber;
            int start = _lineStart;
            _lookahead = (_extra.Tokens != null) ? CollectToken() : Advance();
            _index = pos;
            _lineNumber = line;
            _lineStart = start;
        }

        private void MarkStart()
        {
            if (_extra.Loc.HasValue)
            {
                _state.MarkerStack.Push(_index - _lineStart);
                _state.MarkerStack.Push(_lineNumber);
            }
            if (_extra.Range != null)
            {
                _state.MarkerStack.Push(_index);
            }
        }

        private T MarkEnd<T>(T node) where T : Node
        {
            if (_extra.Range != null)
            {
                node.Range = new Esprima.Ast.Range(_state.MarkerStack.Pop(), _index);
            }
            if (_extra.Loc.HasValue)
            {
                node.Location = new Location(
                    start: new Position(
                        line: _state.MarkerStack.Pop(),
                        column: _state.MarkerStack.Pop()),
                    end: new Position(
                        line: _lineNumber,
                        column: _index - _lineStart),
                    source: _source);
                PostProcess(node);
            }
            return node;
        }

        public T MarkEndIf<T>(T node) where T : Node
        {
            if (node.Range != default || node.Location != default)
            {
                if (_extra.Loc.HasValue)
                {
                    _state.MarkerStack.Pop();
                    _state.MarkerStack.Pop();
                }
                if (_extra.Range != null)
                {
                    _state.MarkerStack.Pop();
                }
            }
            else
            {
                MarkEnd(node);
            }
            return node;
        }

        public Node PostProcess(Node node)
        {
            //if (_extra.Source != null)
            //{
            //    node.Location.Source = _extra.Source;
            //}

            return node;
        }

        private void ThrowError(Token token, string messageFormat, params object[] arguments)
        {
            ThrowError(token.Range[0], messageFormat, arguments);
        }

        private void ThrowError(int position, string messageFormat, params object[] arguments)
        {
            string msg = System.String.Format(messageFormat, arguments);
            ExceptionHelper.ThrowSyntaxError(_engine.Realm, $"{msg} at position {position}");
        }

        // Throw an exception because of the token.

        private void ThrowUnexpected(Token token)
        {
            if (token.Type == Tokens.EOF)
            {
                ThrowError(token, Messages.UnexpectedEOS);
            }

            if (token.Type == Tokens.Number)
            {
                ThrowError(token, Messages.UnexpectedNumber);
            }

            if (token.Type == Tokens.String)
            {
                ThrowError(token, Messages.UnexpectedString);
            }

            // BooleanLiteral, NullLiteral, or Punctuator.
            ThrowError(token, Messages.UnexpectedToken, token.Text);
        }

        // Expect the next token to match the specified punctuator.
        // If not, an exception will be thrown.

        private void Expect(string value)
        {
            Token token = Lex();
            if (token.Type != Tokens.Punctuator || !value.Equals(token.Value))
            {
                ThrowUnexpected(token);
            }
        }

        // Return true if the next token matches the specified punctuator.

        private bool Match(string value)
        {
            return _lookahead.Type == Tokens.Punctuator && value.Equals(_lookahead.Value);
        }

        private ObjectInstance ParseJsonArray()
        {
            var elements = new List<JsValue>();

            Expect("[");

            while (!Match("]"))
            {
                if (Match(","))
                {
                    Lex();
                    elements.Add(Null.Instance);
                }
                else
                {
                    elements.Add(ParseJsonValue());

                    if (!Match("]"))
                    {
                        Expect(",");
                    }
                }
            }

            Expect("]");

            return _engine.Realm.Intrinsics.Array.ConstructFast(elements);
        }

        public ObjectInstance ParseJsonObject()
        {
            Expect("{");

            var obj = _engine.Realm.Intrinsics.Object.Construct(Arguments.Empty);

            while (!Match("}"))
            {
                Tokens type = _lookahead.Type;
                if (type != Tokens.String)
                {
                    ThrowUnexpected(Lex());
                }

                var nameToken = Lex();
                var name = nameToken.Value.ToString();
                if (PropertyNameContainsInvalidCharacters(name))
                {
                    ThrowError(nameToken, Messages.InvalidCharacter);
                }

                Expect(":");
                var value = ParseJsonValue();

                obj.FastAddProperty(name, value, true, true, true);

                if (!Match("}"))
                {
                    Expect(",");
                }
            }

            Expect("}");

            return obj;
        }

        private static bool PropertyNameContainsInvalidCharacters(string propertyName)
        {
            const char max = (char) 31;
            foreach (var c in propertyName)
            {
                if (c != '\t' && c <= max)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Optimization.
        /// By calling Lex().Value for each type, we parse the token twice.
        /// It was already parsed by the peek() method.
        /// _lookahead.Value already contain the value.
        /// </summary>
        /// <returns></returns>
        private JsValue ParseJsonValue()
        {
            Tokens type = _lookahead.Type;
            MarkStart();

            switch (type)
            {
                case Tokens.NullLiteral:
                    var v = Lex().Value;
                    return Null.Instance;
                case Tokens.BooleanLiteral:
                    // implicit conversion operator goes through caching
                    return (bool) Lex().Value ? JsBoolean.True : JsBoolean.False;
                case Tokens.String:
                    // implicit conversion operator goes through caching
                    return new JsString((string) Lex().Value);
                case Tokens.Number:
                    return (double) Lex().Value;
            }

            if (Match("["))
            {
                return ParseJsonArray();
            }

            if (Match("{"))
            {
                return ParseJsonObject();
            }

            ThrowUnexpected(Lex());

            // can't be reached
            return Null.Instance;
        }

        public JsValue Parse(string code)
        {
            return Parse(code, null);
        }

        public JsValue Parse(string code, ParserOptions options)
        {
            _source = code;
            _index = 0;
            _lineNumber = 1;
            _lineStart = 0;
            _length = _source.Length;
            _lookahead = null;
            _state = new State
            {
                AllowIn = true,
                LabelSet = new HashSet<string>(),
                InFunctionBody = false,
                InIteration = false,
                InSwitch = false,
                LastCommentStart = -1,
                MarkerStack = new Stack<int>()
            };

            _extra = new Extra
                {
                    Range = new int[0],
                    Loc = 0,

                };

            if (options != null)
            {
                if (options.Tokens)
                {
                    _extra.Tokens = new List<Token>();
                }

            }

            try
            {
                MarkStart();
                Peek();
                JsValue jsv = ParseJsonValue();

                Peek();

                if(_lookahead.Type != Tokens.EOF)
                {
                    ThrowError(_lookahead, Messages.UnexpectedToken, _lookahead.Text);
                }
                return jsv;
            }
            finally
            {
                _extra = new Extra();
            }
        }

        private class Extra
        {
            public int? Loc;
            public int[] Range;

            public List<Token> Tokens;
        }

        private enum Tokens
        {
            NullLiteral,
            BooleanLiteral,
            String,
            Number,
            Punctuator,
            EOF,
        };

        class Token
        {
            public Tokens Type;
            public object Value;
            public string Text;
            public int[] Range;
            public int? LineNumber;
            public int LineStart;
        }

        static class Messages
        {
            public const string InvalidCharacter = "Invalid character in JSON";
            public const string ExpectedHexadecimalDigit = "Expected hexadecimal digit in JSON";
            public const string UnexpectedToken = "Unexpected token '{0}' in JSON";
            public const string UnexpectedNumber = "Unexpected number in JSON";
            public const string UnexpectedString = "Unexpected string in JSON";
            public const string UnexpectedEOS = "Unexpected end of JSON input";
        };

        struct State
        {
            public int LastCommentStart;
            public bool AllowIn;
            public HashSet<string> LabelSet;
            public bool InFunctionBody;
            public bool InIteration;
            public bool InSwitch;
            public Stack<int> MarkerStack;
        }
    }
}