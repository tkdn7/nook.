using System.Globalization;

namespace Nook.Core;

public static class CalculatorEngine
{
    public static double Evaluate(string expression, bool degrees = true, double answer = 0)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 512) throw new FormatException("식을 입력해 주세요.");
        return new Parser(expression, degrees, answer).Run();
    }
    public static string Display(double value) => value.ToString("G12", CultureInfo.InvariantCulture);
    // Bounded parsing. Expressions never execute application code.
    private sealed class Parser(string source, bool degrees, double answer)
    {
        private int _position, _depth;
        private void Space() { while (_position < source.Length && char.IsWhiteSpace(source[_position])) _position++; }
        private bool Take(char c) { Space(); if (_position < source.Length && source[_position] == c) { _position++; return true; } return false; }
        private static double Finite(double value) => double.IsFinite(value) ? value : throw new FormatException("계산 범위를 확인해 주세요.");
        public double Run() { double value = Sum(); Space(); if (_position != source.Length) throw new FormatException("식을 확인해 주세요."); return Finite(value); }
        private double Sum() { double value = Product(); while (true) { if (Take('+')) value = Finite(value + Product()); else if (Take('-')) value = Finite(value - Product()); else return value; } }
        private double Product() { double value = Unary(); while (true) { if (Take('*') || Take('×')) value = Finite(value * Unary()); else if (Take('/') || Take('÷')) { double next = Unary(); if (next == 0) throw new FormatException("0으로 나눌 수 없습니다."); value = Finite(value / next); } else return value; } }
        private double Unary()
        {
            if (++_depth > 32) throw new FormatException("식이 너무 복잡합니다.");
            try { return Take('+') ? Unary() : Take('-') ? -Unary() : Power(); } finally { _depth--; }
        }
        private double Power() { double value = Postfix(); return Take('^') ? Finite(Math.Pow(value, Unary())) : value; }
        private double Postfix()
        {
            double value = Primary();
            while (true)
            {
                if (Take('%')) value /= 100;
                else if (Take('!')) { if (value < 0 || value > 170 || value != Math.Truncate(value)) throw new FormatException("팩토리얼은 0~170의 정수만 가능합니다."); double n = value; value = 1; for (int i = 2; i <= n; i++) value *= i; }
                else return value;
            }
        }
        private double Primary()
        {
            Space();
            if (Take('(')) { double value = Sum(); if (!Take(')')) throw new FormatException("괄호를 닫아 주세요."); return value; }
            int start = _position;
            if (_position < source.Length && char.IsLetter(source[_position]))
            {
                while (_position < source.Length && char.IsLetter(source[_position])) _position++;
                string name = source[start.._position].ToLowerInvariant();
                if (name is "pi" or "π") return Math.PI;
                if (name == "e") return Math.E;
                if (name == "ans") return answer;
                if (!Take('(')) throw new FormatException("함수 뒤에 괄호를 입력해 주세요.");
                double value = Sum(); if (!Take(')')) throw new FormatException("괄호를 닫아 주세요.");
                double angle = degrees ? value * Math.PI / 180 : value;
                double ToAngle(double radians) => degrees ? radians * 180 / Math.PI : radians;
                return Finite(name switch
                {
                    "sin" => Math.Sin(angle), "cos" => Math.Cos(angle), "tan" => Math.Abs(Math.Cos(angle)) < 1e-14 ? double.NaN : Math.Tan(angle),
                    "asin" => ToAngle(Math.Asin(value)), "acos" => ToAngle(Math.Acos(value)), "atan" => ToAngle(Math.Atan(value)),
                    "sqrt" => Math.Sqrt(value), "log" => Math.Log10(value), "ln" => Math.Log(value), "abs" => Math.Abs(value), "exp" => Math.Exp(value),
                    _ => throw new FormatException("지원하지 않는 함수입니다.")
                });
            }
            while (_position < source.Length && (char.IsDigit(source[_position]) || source[_position] == '.')) _position++;
            if (_position > start && _position < source.Length && source[_position] is 'e' or 'E')
            { _position++; if (_position < source.Length && source[_position] is '+' or '-') _position++; while (_position < source.Length && char.IsDigit(source[_position])) _position++; }
            if (_position == start || !double.TryParse(source[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) throw new FormatException("식을 확인해 주세요.");
            return Finite(number);
        }
    }
}

public sealed class CalculatorState
{
    public string Expression { get; set; } = "";
    public double Answer { get; set; }
    public bool Degrees { get; set; } = true;
    public string? CompletedExpression { get; set; }
    public double? CompletedResult { get; set; }
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
}
