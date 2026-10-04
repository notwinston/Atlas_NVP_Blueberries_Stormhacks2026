using System;
using System.Text;
using Atlas.World;

namespace Atlas.Blades.Playground
{
    /// <summary>
    /// The equation card of the exhibit the walker is in (Master Plan section 10.1.3, task P2-C06): the formula,
    /// then the rows of live numbers. The core has no engine type: it asks its <see cref="IEquationSource"/> for
    /// the rows at <see cref="RefreshHz"/> and builds the text into one reused buffer, without allocating, so the
    /// view only copies the text when <see cref="Revision"/> moved. Main thread only.
    /// </summary>
    public sealed class EquationCard
    {
        /// <summary>The card reads its source this often. The plan asks for at least 10 Hz; 15 Hz leaves room for a
        /// frame loop at 30 Hz, where it reads every second frame.</summary>
        public const float RefreshHz = 15f;
        public const float RefreshInterval = 1f / RefreshHz;
        /// <summary>Significant figures of a number on the card; lane A's equality test (P2-A03) uses the same.</summary>
        public const int SignificantFigures = 3;

        readonly StringBuilder _text = new StringBuilder(256);
        readonly StringBuilder _previous = new StringBuilder(256);
        EquationRow[] _rows = new EquationRow[8];
        IEquationSource _source;
        int _rowCount;
        float _sinceRead;

        /// <summary>The space the card shows, or null while unbound (Home and the menu show none).</summary>
        public bool IsBound { get { return _source != null; } }
        public SpaceId Space { get { return _source != null ? _source.Space : SpaceId.Home; } }
        /// <summary>Times the source was read since Bind. The acceptance count of at least 10 per second.</summary>
        public long Reads { get; private set; }
        /// <summary>Moves only when the drawn text changed, so a still card costs the view nothing.</summary>
        public int Revision { get; private set; }
        /// <summary>The card as text: the formula, then one "label  value unit" line per row.</summary>
        public StringBuilder Text { get { return _text; } }
        public int RowCount { get { return _rowCount; } }
        /// <summary>The rows of the last read. Valid up to RowCount.</summary>
        public EquationRow[] Rows { get { return _rows; } }

        /// <summary>Shows this source's card from the next Tick, which reads it at once.</summary>
        public void Bind(IEquationSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _source = source;
            if (_rows.Length < source.MaxRows) _rows = new EquationRow[source.MaxRows];
            _sinceRead = RefreshInterval;
            Rebuild();
        }

        /// <summary>Binds the registered source of the space. False (and unbound) when lane A's source for it is not there yet.</summary>
        public bool BindSpace(SpaceId space)
        {
            if (EquationSources.TryGet(space, out IEquationSource source))
            {
                Bind(source);
                return true;
            }
            Unbind();
            return false;
        }

        public void Unbind()
        {
            _source = null;
            _rowCount = 0;
            _sinceRead = 0f;
            if (_text.Length != 0)
            {
                _text.Length = 0;
                Revision++;
            }
        }

        /// <summary>Advances the clock. Returns true when the source was read in this call.</summary>
        public bool Tick(float deltaTime)
        {
            if (_source == null) return false;
            _sinceRead += deltaTime > 0f ? deltaTime : 0f;
            // Slightly under the interval, so a frame that lands on it exactly (60 Hz into 15 Hz) is not lost
            // to rounding and the card never runs below its rate.
            if (_sinceRead < RefreshInterval - 1e-4f) return false;
            _sinceRead = _sinceRead >= 2f * RefreshInterval ? 0f : _sinceRead - RefreshInterval;
            Rebuild();
            return true;
        }

        void Rebuild()
        {
            _rowCount = Math.Min(_source.Read(_rows), _rows.Length);
            Reads++;

            _previous.Length = 0;
            _previous.Append(_text);
            _text.Length = 0;
            _text.Append(_source.Template);
            for (int i = 0; i < _rowCount; i++)
            {
                _text.Append('\n');
                AppendRow(_text, in _rows[i]);
            }
            if (!SameText(_text, _previous)) Revision++;
        }

        /// <summary>"label  value unit", the value to <see cref="SignificantFigures"/> figures, a dash when unknown.</summary>
        public static void AppendRow(StringBuilder into, in EquationRow row)
        {
            into.Append(row.Label).Append("  ");
            if (row.Text != null) into.Append(row.Text);
            else if (!row.Available) into.Append('-');
            else
            {
                AppendSignificant(into, row.Value);
                if (!string.IsNullOrEmpty(row.Unit)) into.Append(' ').Append(row.Unit);
            }
        }

        /// <summary>
        /// Appends the value rounded to three significant figures (1.14, 22.5, 270, 0.0314), with no allocation. A
        /// value of 1000 or more keeps its integer digits. The same rounding as <c>value.ToString("G3")</c>
        /// without the exponent form.
        /// </summary>
        public static void AppendSignificant(StringBuilder into, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                into.Append('-');
                return;
            }
            if (value == 0.0)
            {
                into.Append('0');
                return;
            }
            double a = Math.Abs(value);
            int magnitude = (int)Math.Floor(Math.Log10(a));
            int decimals = SignificantFigures - 1 - magnitude;
            if (decimals < 0) decimals = 0;
            if (decimals > 9) decimals = 9;
            double scale = Pow10(decimals);
            double scaled = Math.Round(a * scale, MidpointRounding.AwayFromZero);
            // 9.996 rounds up to 10.0: one decimal fewer keeps three figures.
            if (decimals > 0 && scaled >= Pow10(SignificantFigures))
            {
                decimals--;
                scale = Pow10(decimals);
                scaled = Math.Round(a * scale, MidpointRounding.AwayFromZero);
            }
            if (scaled == 0.0)
            {
                into.Append('0');
                return;
            }
            if (value < 0) into.Append('-');
            double whole = Math.Floor(scaled / scale);
            AppendInteger(into, (long)whole);
            if (decimals == 0) return;
            long fraction = (long)(scaled - whole * scale);
            into.Append('.');
            for (int d = decimals - 1; d >= 0; d--)
            {
                long digit = (fraction / (long)Pow10(d)) % 10;
                into.Append((char)('0' + digit));
            }
        }

        static void AppendInteger(StringBuilder into, long n)
        {
            if (n == 0)
            {
                into.Append('0');
                return;
            }
            int start = into.Length;
            while (n > 0)
            {
                into.Append((char)('0' + n % 10));
                n /= 10;
            }
            for (int i = start, j = into.Length - 1; i < j; i++, j--)
            {
                char t = into[i];
                into[i] = into[j];
                into[j] = t;
            }
        }

        static double Pow10(int n)
        {
            double p = 1.0;
            for (int i = 0; i < n; i++) p *= 10.0;
            return p;
        }

        static bool SameText(StringBuilder a, StringBuilder b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
