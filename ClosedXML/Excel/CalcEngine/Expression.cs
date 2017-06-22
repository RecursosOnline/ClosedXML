using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace ClosedXML.Excel.CalcEngine
{
    /// <summary>
    /// Base class that represents parsed expressions.
    /// </summary>
    /// <remarks>
    /// For example:
    /// <code>
    /// Expression expr = scriptEngine.Parse(strExpression);
    /// object val = expr.Evaluate();
    /// </code>
    /// </remarks>
    internal class Expression : IComparable<Expression>
    {
        //---------------------------------------------------------------------------
        #region ** fields

        internal Token _token;

        #endregion

        //---------------------------------------------------------------------------
        #region ** ctors

        internal Expression()
        {
            _token = new Token(null, TKID.ATOM, TKTYPE.IDENTIFIER);
        }
        internal Expression(object value)
        {
            _token = new Token(value, TKID.ATOM, TKTYPE.LITERAL);
        }
        internal Expression(Token tk)
        {
            _token = tk;
        }

        #endregion

        //---------------------------------------------------------------------------
        #region ** object model

        public virtual object Evaluate()
        {
            if (_token.Type != TKTYPE.LITERAL)
            {
                throw new ArgumentException("Bad expression.");
            }
            return _token.Value;
        }
        public virtual Expression Optimize()
        {
            return this;
        }

        #endregion

        //---------------------------------------------------------------------------
        #region ** implicit converters

        public static implicit operator string(Expression x)
        {
            var v = x.Evaluate();
            return v == null ? string.Empty : v.ToString();
        }
        public static implicit operator double(Expression x)
        {
            // evaluate
            var v = x.Evaluate();

            // handle doubles
            if (v is double)
            {
                return (double)v;
            }

            // handle booleans
            if (v is bool)
            {
                return (bool)v ? 1 : 0;
            }

            // handle dates
            if (v is DateTime)
            {
                return ((DateTime)v).ToOADate();
            }

            // handle nulls
            if (v == null || v is string)
            {
                return 0;
            }

            // handle everything else
            CultureInfo _ci = Thread.CurrentThread.CurrentCulture;
            return (double)Convert.ChangeType(v, typeof(double), _ci);
        }
        public static implicit operator bool(Expression x)
        {
            // evaluate
            var v = x.Evaluate();

            // handle booleans
            if (v is bool)
            {
                return (bool)v;
            }

            // handle nulls
            if (v == null)
            {
                return false;
            }

            // handle doubles
            if (v is double)
            {
                return (double)v == 0 ? false : true;
            }

            // handle everything else
            return (double)x == 0 ? false : true;
        }
        public static implicit operator DateTime(Expression x)
        {
            // evaluate
            var v = x.Evaluate();

            // handle dates
            if (v is DateTime)
            {
                return (DateTime)v;
            }

            // handle doubles
            if (v is double || v is int)
            {
                return DateTime.FromOADate((double)x);
            }

            // handle everything else
            CultureInfo _ci = Thread.CurrentThread.CurrentCulture;
            return (DateTime)Convert.ChangeType(v, typeof(DateTime), _ci);
        }

        #endregion

        //---------------------------------------------------------------------------
        #region ** IComparable<Expression>

        public int CompareTo(Expression other)
        {
            // get both values
            var c1 = this.Evaluate() as IComparable;
            var c2 = other.Evaluate() as IComparable;

            // handle nulls
            if (c1 == null && c2 == null)
            {
                return 0;
            }
            if (c2 == null)
            {
                return -1;
            }
            if (c1 == null)
            {
                return +1;
            }

            // make sure types are the same
            if (c1.GetType() != c2.GetType())
            {
                try
                {
                    if (c1 is DateTime)
                        c2 = ((DateTime)other);
                    else if (c2 is DateTime)
                        c1 = ((DateTime)this);
                    else
                        c2 = Convert.ChangeType(c2, c1.GetType()) as IComparable;
                }
                catch (InvalidCastException) { return -1; }
                catch (FormatException) { return -1; }
                catch (OverflowException) { return -1; }
                catch (ArgumentNullException) { return -1; }
            }

            // compare
            return c1.CompareTo(c2);
        }

        #endregion
    }
    /// <summary>
    /// Unary expression, e.g. +123
    /// </summary>
    class UnaryExpression : Expression
    {
        // ** fields
        Expression	_expr;

        // ** ctor
        public UnaryExpression(Token tk, Expression expr) : base(tk)
        {
            _expr = expr;
        }

        // ** object model
        override public object Evaluate()
        {
            switch (_token.ID)
            {
                case TKID.ADD:
                    return +(double)_expr;
                case TKID.SUB:
                    return -(double)_expr;
            }
            throw new ArgumentException("Bad expression.");
        }
        public override Expression Optimize()
        {
            _expr = _expr.Optimize();
            return _expr._token.Type == TKTYPE.LITERAL
                ? new Expression(this.Evaluate())
                : this;
        }
    }
    /// <summary>
    /// Binary expression, e.g. 1+2
    /// </summary>
    class BinaryExpression : Expression
    {
        // ** fields
        Expression	_lft;
        Expression	_rgt;

        // ** ctor
        public BinaryExpression(Token tk, Expression exprLeft, Expression exprRight) : base(tk)
        {
            _lft  = exprLeft;
            _rgt = exprRight;
        }

        // ** object model
        override public object Evaluate()
        {
            // handle comparisons
            if (_token.Type == TKTYPE.COMPARE)
            {
                var cmp = _lft.CompareTo(_rgt);
                switch (_token.ID)
                {
                    case TKID.GT: return cmp > 0;
                    case TKID.LT: return cmp < 0;
                    case TKID.GE: return cmp >= 0;
                    case TKID.LE: return cmp <= 0;
                    case TKID.EQ: return cmp == 0;
                    case TKID.NE: return cmp != 0;
                }
            }

            // handle everything else
            switch (_token.ID)
            {
                case TKID.CONCAT:
                    return (string)_lft + (string)_rgt;
                case TKID.ADD:
                    return (double)_lft + (double)_rgt;
                case TKID.SUB:
                    return (double)_lft - (double)_rgt;
                case TKID.MUL:
                    return (double)_lft * (double)_rgt;
                case TKID.DIV:
                    return (double)_lft / (double)_rgt;
                case TKID.DIVINT:
                    return (double)(int)((double)_lft / (double)_rgt);
                case TKID.MOD:
                    return (double)(int)((double)_lft % (double)_rgt);
                case TKID.POWER:
                    var a = (double)_lft;
                    var b = (double)_rgt;
                    if (b == 0.0) return 1.0;
                    if (b == 0.5) return Math.Sqrt(a);
                    if (b == 1.0) return a;
                    if (b == 2.0) return a * a;
                    if (b == 3.0) return a * a * a;
                    if (b == 4.0) return a * a * a * a;
                    return Math.Pow((double)_lft, (double)_rgt);
            }
            throw new ArgumentException("Bad expression.");
        }
        public override Expression Optimize()
        {
            _lft = _lft.Optimize();
            _rgt = _rgt.Optimize();
            return _lft._token.Type == TKTYPE.LITERAL && _rgt._token.Type == TKTYPE.LITERAL
                ? new Expression(this.Evaluate())
                : this;
        }
    }
    /// <summary>
    /// Function call expression, e.g. sin(0.5)
    /// </summary>
    class FunctionExpression : Expression
    {
        // ** fields
        FunctionDefinition _fn;
        List<Expression> _parms;

        // ** ctor
        internal FunctionExpression()
        {
        }
        public FunctionExpression(FunctionDefinition function, List<Expression> parms)
        {
            _fn = function;
            _parms = parms;
        }

        // ** object model
        override public object Evaluate()
        {
            return _fn.Function(_parms);
        }
        public override Expression Optimize()
        {
            bool allLits = true;
            if (_parms != null)
            {
                for (int i = 0; i < _parms.Count; i++)
                {
                    var p = _parms[i].Optimize();
                    _parms[i] = p;
                    if (p._token.Type != TKTYPE.LITERAL)
                    {
                        allLits = false;
                    }
                }
            }
            return allLits
                ? new Expression(this.Evaluate())
                : this;
        }
    }
    /// <summary>
    /// Simple variable reference.
    /// </summary>
    class VariableExpression : Expression
    {
        Dictionary<string, object> _dct;
        string _name;

        public VariableExpression(Dictionary<string, object> dct, string name)
        {
            _dct = dct;
            _name = name;
        }
        public override object Evaluate()
        {
            return _dct[_name];
        }
    }
    /// <summary>
    /// Expression that represents an external object.
    /// </summary>
    class XObjectExpression :
        Expression,
        IEnumerable
    {
        object _value;

        // ** ctor
        internal XObjectExpression(object value)
        {
            _value = value;
        }

        public object Value { get { return _value; } }

        // ** object model
        public override object Evaluate()
        {
            // use IValueObject if available
            var iv = _value as IValueObject;
            if (iv != null)
            {
                return iv.GetValue();
            }

            // return raw object
            return _value;
        }
        public IEnumerator GetEnumerator()
        {
            return (_value as IEnumerable).GetEnumerator();
        }
    }

    /// <summary>
    /// Expression that represents an omitted parameter.
    /// </summary>
    class EmptyValueExpression : Expression
    {
        internal EmptyValueExpression() { }
    }

    /// <summary>
    /// Interface supported by external objects that have to return a value
    /// other than themselves (e.g. a cell range object should return the
    /// cell content instead of the range itself).
    /// </summary>
    public interface IValueObject
    {
        object GetValue();
    }
}
