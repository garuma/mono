// created by jay 0.7 (c) 1998 Axel.Schreiner@informatik.uni-osnabrueck.de

#line 2 "Monkeydoc.Ecma/EcmaUrlParser.jay"
using System.Text;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Monkeydoc.Ecma
{
	public class EcmaUrlParser
	{
        int yacc_verbose_flag = 0;

        public void IsValid (string input)
        {
            var reader = new StringReader (input);
			var lexer = new EcmaUrlTokenizer (reader);
			this.yyparse (lexer);
        }

        public EcmaDesc Parse (string input)
        {
            var reader = new StringReader (input);
			var lexer = new EcmaUrlTokenizer (reader);
			return (EcmaDesc)this.yyparse (lexer);
        }

        public bool TryParse (string input, out EcmaDesc desc)
        {
            desc = null;
            try {
                desc = Parse (input);
            } catch {
                return false;
            }
            return true;
        }

        EcmaDesc CopyFromEcmaDesc (EcmaDesc dest, EcmaDesc orig)
        {
            if (string.IsNullOrEmpty (dest.Namespace))
               dest.Namespace = orig.Namespace;
            if (string.IsNullOrEmpty (dest.TypeName))
               dest.TypeName = orig.TypeName;
            if (dest.GenericTypeArguments == null)
               dest.GenericTypeArguments = orig.GenericTypeArguments;
            if (dest.NestedType == null)
               dest.NestedType = orig.NestedType;
            if (dest.ArrayDimension == 0)
               dest.ArrayDimension = orig.ArrayDimension;
            if (string.IsNullOrEmpty (dest.MemberName))
               dest.MemberName = orig.MemberName;
            if (dest.GenericMemberArguments == null)
               dest.GenericMemberArguments = orig.GenericMemberArguments;
            if (dest.MemberArguments == null)
               dest.MemberArguments = orig.MemberArguments;
            if (orig.IsEtc)
               dest.Etc = orig.Etc;

            return dest;
        }

        List<T> SafeReverse<T> (List<T> input)
        {
            if (input == null)
               return null;
            input.Reverse ();
            return input;
        }
#line default

  /** error output stream.
      It should be changeable.
    */
  public System.IO.TextWriter ErrorOutput = System.Console.Out;

  /** simplified error message.
      @see <a href="#yyerror(java.lang.String, java.lang.String[])">yyerror</a>
    */
  public void yyerror (string message) {
    yyerror(message, null);
  }

  /* An EOF token */
  public int eof_token;

  /** (syntax) error message.
      Can be overwritten to control message format.
      @param message text to be displayed.
      @param expected vector of acceptable tokens, if available.
    */
  public void yyerror (string message, string[] expected) {
    if ((yacc_verbose_flag > 0) && (expected != null) && (expected.Length  > 0)) {
      ErrorOutput.Write (message+", expecting");
      for (int n = 0; n < expected.Length; ++ n)
        ErrorOutput.Write (" "+expected[n]);
        ErrorOutput.WriteLine ();
    } else
      ErrorOutput.WriteLine (message);
  }

  /** debugging support, requires the package jay.yydebug.
      Set to null to suppress debugging messages.
    */
  internal yydebug.yyDebug debug;

  protected const int yyFinal = 8;
 // Put this array into a separate class so it is only initialized if debugging is actually used
 // Use MarshalByRefObject to disable inlining
 class YYRules : MarshalByRefObject {
  public static readonly string [] yyRule = {
    "$accept : expression",
    "expression : 'T' COLON type_expression",
    "expression : 'N' COLON namespace_expression",
    "expression : 'M' COLON method_expression",
    "expression : 'F' COLON simple_member_expression",
    "expression : 'C' COLON constructor_expression",
    "expression : 'P' COLON simple_member_expression",
    "expression : 'E' COLON simple_member_expression",
    "dot_expression : IDENTIFIER",
    "dot_expression : IDENTIFIER DOT dot_expression",
    "namespace_expression : dot_expression",
    "type_expression : dot_expression type_expression_suffix",
    "type_expression_suffix : opt_generic_type_suffix opt_inner_type_description opt_array_definition opt_etc",
    "opt_inner_type_description :",
    "opt_inner_type_description : INNER_TYPE_SEPARATOR type_expression",
    "opt_generic_type_suffix :",
    "opt_generic_type_suffix : OP_GENERICS_BACKTICK DIGIT",
    "opt_generic_type_suffix : OP_GENERICS_LT generic_type_arg_list OP_GENERICS_GT",
    "generic_type_arg_list : type_expression",
    "generic_type_arg_list : generic_type_arg_list COMMA type_expression",
    "opt_array_definition :",
    "opt_array_definition : OP_ARRAY_OPEN opt_array_definition_list OP_ARRAY_CLOSE",
    "opt_array_definition_list :",
    "opt_array_definition_list : COMMA opt_array_definition_list",
    "opt_etc :",
    "opt_etc : SLASH_SEPARATOR IDENTIFIER",
    "method_expression : type_expression DOT IDENTIFIER opt_generic_type_suffix opt_arg_list_suffix",
    "method_expression : dot_expression opt_generic_type_suffix opt_arg_list_suffix",
    "type_expression_list :",
    "type_expression_list : type_expression",
    "type_expression_list : type_expression COMMA type_expression_list",
    "simple_member_expression : dot_expression",
    "constructor_expression : method_expression",
    "opt_arg_list_suffix :",
    "opt_arg_list_suffix : OP_OPEN_PAREN type_expression_list OP_CLOSE_PAREN",
  };
 public static string getRule (int index) {
    return yyRule [index];
 }
}
  protected static readonly string [] yyNames = {    
    "end-of-file",null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,
    "'C'",null,"'E'","'F'",null,null,null,null,null,null,"'M'","'N'",null,
    "'P'",null,null,null,"'T'",null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,"ERROR",
    "IDENTIFIER","DIGIT","DOT","COMMA","COLON","INNER_TYPE_SEPARATOR",
    "OP_GENERICS_LT","OP_GENERICS_GT","OP_GENERICS_BACKTICK",
    "OP_OPEN_PAREN","OP_CLOSE_PAREN","OP_ARRAY_OPEN","OP_ARRAY_CLOSE",
    "SLASH_SEPARATOR",
  };

  /** index-checked interface to yyNames[].
      @param token single character or %token value.
      @return token name or [illegal] or [unknown].
    */
  public static string yyname (int token) {
    if ((token < 0) || (token > yyNames.Length)) return "[illegal]";
    string name;
    if ((name = yyNames[token]) != null) return name;
    return "[unknown]";
  }

  int yyExpectingState;
  /** computes list of expected tokens on error by tracing the tables.
      @param state for which to compute the list.
      @return list of token names.
    */
  protected int [] yyExpectingTokens (int state){
    int token, n, len = 0;
    bool[] ok = new bool[yyNames.Length];
    if ((n = yySindex[state]) != 0)
      for (token = n < 0 ? -n : 0;
           (token < yyNames.Length) && (n+token < yyTable.Length); ++ token)
        if (yyCheck[n+token] == token && !ok[token] && yyNames[token] != null) {
          ++ len;
          ok[token] = true;
        }
    if ((n = yyRindex[state]) != 0)
      for (token = n < 0 ? -n : 0;
           (token < yyNames.Length) && (n+token < yyTable.Length); ++ token)
        if (yyCheck[n+token] == token && !ok[token] && yyNames[token] != null) {
          ++ len;
          ok[token] = true;
        }
    int [] result = new int [len];
    for (n = token = 0; n < len;  ++ token)
      if (ok[token]) result[n++] = token;
    return result;
  }
  protected string[] yyExpecting (int state) {
    int [] tokens = yyExpectingTokens (state);
    string [] result = new string[tokens.Length];
    for (int n = 0; n < tokens.Length;  n++)
      result[n++] = yyNames[tokens [n]];
    return result;
  }

  /** the generated parser, with debugging messages.
      Maintains a state and a value stack, currently with fixed maximum size.
      @param yyLex scanner.
      @param yydebug debug message writer implementing yyDebug, or null.
      @return result of the last reduction, if any.
      @throws yyException on irrecoverable parse error.
    */
  internal Object yyparse (yyParser.yyInput yyLex, Object yyd)
				 {
    this.debug = (yydebug.yyDebug)yyd;
    return yyparse(yyLex);
  }

  /** initial size and increment of the state/value stack [default 256].
      This is not final so that it can be overwritten outside of invocations
      of yyparse().
    */
  protected int yyMax;

  /** executed at the beginning of a reduce action.
      Used as $$ = yyDefault($1), prior to the user-specified action, if any.
      Can be overwritten to provide deep copy, etc.
      @param first value for $1, or null.
      @return first.
    */
  protected Object yyDefault (Object first) {
    return first;
  }

	static int[] global_yyStates;
	static object[] global_yyVals;
	protected bool use_global_stacks;
	object[] yyVals;					// value stack
	object yyVal;						// value stack ptr
	int yyToken;						// current input
	int yyTop;

  /** the generated parser.
      Maintains a state and a value stack, currently with fixed maximum size.
      @param yyLex scanner.
      @return result of the last reduction, if any.
      @throws yyException on irrecoverable parse error.
    */
  internal Object yyparse (yyParser.yyInput yyLex)
  {
    if (yyMax <= 0) yyMax = 256;		// initial size
    int yyState = 0;                   // state stack ptr
    int [] yyStates;               	// state stack 
    yyVal = null;
    yyToken = -1;
    int yyErrorFlag = 0;				// #tks to shift
	if (use_global_stacks && global_yyStates != null) {
		yyVals = global_yyVals;
		yyStates = global_yyStates;
   } else {
		yyVals = new object [yyMax];
		yyStates = new int [yyMax];
		if (use_global_stacks) {
			global_yyVals = yyVals;
			global_yyStates = yyStates;
		}
	}

    /*yyLoop:*/ for (yyTop = 0;; ++ yyTop) {
      if (yyTop >= yyStates.Length) {			// dynamically increase
        global::System.Array.Resize (ref yyStates, yyStates.Length+yyMax);
        global::System.Array.Resize (ref yyVals, yyVals.Length+yyMax);
      }
      yyStates[yyTop] = yyState;
      yyVals[yyTop] = yyVal;
      if (debug != null) debug.push(yyState, yyVal);

      /*yyDiscarded:*/ while (true) {	// discarding a token does not change stack
        int yyN;
        if ((yyN = yyDefRed[yyState]) == 0) {	// else [default] reduce (yyN)
          if (yyToken < 0) {
            yyToken = yyLex.advance() ? yyLex.token() : 0;
            if (debug != null)
              debug.lex(yyState, yyToken, yyname(yyToken), yyLex.value());
          }
          if ((yyN = yySindex[yyState]) != 0 && ((yyN += yyToken) >= 0)
              && (yyN < yyTable.Length) && (yyCheck[yyN] == yyToken)) {
            if (debug != null)
              debug.shift(yyState, yyTable[yyN], yyErrorFlag-1);
            yyState = yyTable[yyN];		// shift to yyN
            yyVal = yyLex.value();
            yyToken = -1;
            if (yyErrorFlag > 0) -- yyErrorFlag;
            goto continue_yyLoop;
          }
          if ((yyN = yyRindex[yyState]) != 0 && (yyN += yyToken) >= 0
              && yyN < yyTable.Length && yyCheck[yyN] == yyToken)
            yyN = yyTable[yyN];			// reduce (yyN)
          else
            switch (yyErrorFlag) {
  
            case 0:
              yyExpectingState = yyState;
              // yyerror(String.Format ("syntax error, got token `{0}'", yyname (yyToken)), yyExpecting(yyState));
              if (debug != null) debug.error("syntax error");
              if (yyToken == 0 /*eof*/ || yyToken == eof_token) throw new yyParser.yyUnexpectedEof ();
              goto case 1;
            case 1: case 2:
              yyErrorFlag = 3;
              do {
                if ((yyN = yySindex[yyStates[yyTop]]) != 0
                    && (yyN += Token.yyErrorCode) >= 0 && yyN < yyTable.Length
                    && yyCheck[yyN] == Token.yyErrorCode) {
                  if (debug != null)
                    debug.shift(yyStates[yyTop], yyTable[yyN], 3);
                  yyState = yyTable[yyN];
                  yyVal = yyLex.value();
                  goto continue_yyLoop;
                }
                if (debug != null) debug.pop(yyStates[yyTop]);
              } while (-- yyTop >= 0);
              if (debug != null) debug.reject();
              throw new yyParser.yyException("irrecoverable syntax error");
  
            case 3:
              if (yyToken == 0) {
                if (debug != null) debug.reject();
                throw new yyParser.yyException("irrecoverable syntax error at end-of-file");
              }
              if (debug != null)
                debug.discard(yyState, yyToken, yyname(yyToken),
  							yyLex.value());
              yyToken = -1;
              goto continue_yyDiscarded;		// leave stack alone
            }
        }
        int yyV = yyTop + 1-yyLen[yyN];
        if (debug != null)
          debug.reduce(yyState, yyStates[yyV-1], yyN, YYRules.getRule (yyN), yyLen[yyN]);
        yyVal = yyV > yyTop ? null : yyVals[yyV]; // yyVal = yyDefault(yyV > yyTop ? null : yyVals[yyV]);
        switch (yyN) {
case 1:
#line 93 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = CopyFromEcmaDesc (new EcmaDesc { DescKind = EcmaDesc.Kind.Type }, (EcmaDesc)yyVals[0+yyTop]); }
  break;
case 2:
#line 94 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = CopyFromEcmaDesc (new EcmaDesc { DescKind = EcmaDesc.Kind.Namespace }, (EcmaDesc)yyVals[0+yyTop]); }
  break;
case 3:
#line 95 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = CopyFromEcmaDesc (new EcmaDesc { DescKind = EcmaDesc.Kind.Method }, (EcmaDesc)yyVals[0+yyTop]); }
  break;
case 4:
#line 96 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = CopyFromEcmaDesc (new EcmaDesc { DescKind = EcmaDesc.Kind.Field }, (EcmaDesc)yyVals[0+yyTop]); }
  break;
case 5:
#line 97 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = CopyFromEcmaDesc (new EcmaDesc { DescKind = EcmaDesc.Kind.Constructor }, (EcmaDesc)yyVals[0+yyTop]); }
  break;
case 6:
#line 98 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = CopyFromEcmaDesc (new EcmaDesc { DescKind = EcmaDesc.Kind.Property }, (EcmaDesc)yyVals[0+yyTop]); }
  break;
case 7:
#line 99 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = CopyFromEcmaDesc (new EcmaDesc { DescKind = EcmaDesc.Kind.Event }, (EcmaDesc)yyVals[0+yyTop]); }
  break;
case 8:
#line 103 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = new List<string> { (string)yyVals[0+yyTop] }; }
  break;
case 9:
#line 104 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { ((ICollection<string>)yyVals[0+yyTop]).Add ((string)yyVals[-2+yyTop]); yyVal = yyVals[0+yyTop]; }
  break;
case 10:
#line 107 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = new EcmaDesc { Namespace = string.Join (".", ((IEnumerable<string>)yyVals[0+yyTop]).Reverse ()) }; }
  break;
case 11:
  case_11();
  break;
case 12:
  case_12();
  break;
case 13:
#line 131 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = null; }
  break;
case 14:
#line 132 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = yyVals[0+yyTop]; }
  break;
case 15:
#line 135 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = null; }
  break;
case 16:
#line 136 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = Enumerable.Repeat<string> (null, (int)yyVals[0+yyTop]).ToList (); }
  break;
case 17:
#line 137 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = yyVals[-1+yyTop]; }
  break;
case 18:
#line 140 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = new List<EcmaDesc> () { (EcmaDesc)yyVals[0+yyTop] }; }
  break;
case 19:
#line 141 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { ((List<EcmaDesc>)yyVals[-2+yyTop]).Add ((EcmaDesc)yyVals[0+yyTop]); yyVal = yyVals[-2+yyTop]; }
  break;
case 20:
#line 144 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = 0; }
  break;
case 21:
#line 145 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = yyVals[-1+yyTop]; }
  break;
case 22:
#line 148 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = 1; }
  break;
case 23:
#line 149 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = ((int)yyVals[0+yyTop]) + 1; }
  break;
case 24:
#line 152 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = null; }
  break;
case 25:
#line 153 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = yyVals[0+yyTop]; }
  break;
case 26:
  case_26();
  break;
case 27:
  case_27();
  break;
case 28:
#line 174 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = null; }
  break;
case 29:
#line 175 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = new List<EcmaDesc> () { (EcmaDesc)yyVals[0+yyTop] }; }
  break;
case 30:
#line 176 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { ((List<EcmaDesc>)yyVals[0+yyTop]).Add ((EcmaDesc)yyVals[-2+yyTop]); yyVal = yyVals[0+yyTop]; }
  break;
case 31:
  case_31();
  break;
case 32:
#line 191 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = yyVals[0+yyTop]; }
  break;
case 33:
#line 199 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = null; }
  break;
case 34:
#line 200 "Monkeydoc.Ecma/EcmaUrlParser.jay"
  { yyVal = yyVals[-1+yyTop]; }
  break;
#line default
        }
        yyTop -= yyLen[yyN];
        yyState = yyStates[yyTop];
        int yyM = yyLhs[yyN];
        if (yyState == 0 && yyM == 0) {
          if (debug != null) debug.shift(0, yyFinal);
          yyState = yyFinal;
          if (yyToken < 0) {
            yyToken = yyLex.advance() ? yyLex.token() : 0;
            if (debug != null)
               debug.lex(yyState, yyToken,yyname(yyToken), yyLex.value());
          }
          if (yyToken == 0) {
            if (debug != null) debug.accept(yyVal);
            return yyVal;
          }
          goto continue_yyLoop;
        }
        if (((yyN = yyGindex[yyM]) != 0) && ((yyN += yyState) >= 0)
            && (yyN < yyTable.Length) && (yyCheck[yyN] == yyState))
          yyState = yyTable[yyN];
        else
          yyState = yyDgoto[yyM];
        if (debug != null) debug.shift(yyStates[yyTop], yyState);
	 goto continue_yyLoop;
      continue_yyDiscarded: ;	// implements the named-loop continue: 'continue yyDiscarded'
      }
    continue_yyLoop: ;		// implements the named-loop continue: 'continue yyLoop'
    }
  }

/*
 All more than 3 lines long rules are wrapped into a method
*/
void case_11()
#line 110 "Monkeydoc.Ecma/EcmaUrlParser.jay"
{
                         var dotExpr = ((List<string>)yyVals[-1+yyTop]);
                         dotExpr.Reverse ();
                         yyVal = CopyFromEcmaDesc (new EcmaDesc {
                            DescKind = EcmaDesc.Kind.Type,
                            Namespace = string.Join (".", dotExpr.Take (dotExpr.Count - 1)),
                            TypeName = dotExpr.Last ()
                         }, (EcmaDesc)yyVals[0+yyTop]);
                     }

void case_12()
#line 121 "Monkeydoc.Ecma/EcmaUrlParser.jay"
{
                         yyVal = new EcmaDesc {
                            GenericTypeArguments = yyVals[-3+yyTop] as List<EcmaDesc>,
                            NestedType = yyVals[-2+yyTop] as EcmaDesc,
                            ArrayDimension = yyVals[-1+yyTop] == null ? 0 : (int)yyVals[-1+yyTop],
                            Etc = yyVals[0+yyTop] != null ? ((string)yyVals[0+yyTop])[0] : (char)0
                         };
                     }

void case_26()
#line 156 "Monkeydoc.Ecma/EcmaUrlParser.jay"
{ yyVal = CopyFromEcmaDesc (new EcmaDesc {
                           MemberName = yyVals[-2+yyTop] as string,
                           GenericMemberArguments = yyVals[-1+yyTop] as List<EcmaDesc>,
                           MemberArguments = SafeReverse (yyVals[0+yyTop] as List<EcmaDesc>)
                      }, (EcmaDesc)yyVals[-4+yyTop]);
                }

void case_27()
#line 162 "Monkeydoc.Ecma/EcmaUrlParser.jay"
{
                      var dotExpr = ((List<string>)yyVals[-2+yyTop]);
                      yyVal = new EcmaDesc {
                           Namespace = string.Join (".", dotExpr.Skip (2).DefaultIfEmpty (string.Empty).Reverse ()),
                           TypeName = dotExpr.Skip (1).First (),
                           MemberName = dotExpr.First (),
                           GenericMemberArguments = yyVals[-1+yyTop] as List<EcmaDesc>,
                           MemberArguments = SafeReverse (yyVals[0+yyTop] as List<EcmaDesc>)
                      };
                }

void case_31()
#line 179 "Monkeydoc.Ecma/EcmaUrlParser.jay"
{
            var dotExpr = ((List<string>)yyVals[0+yyTop]);
            dotExpr.Reverse ();

            yyVal = new EcmaDesc {
               Namespace = string.Join (".", dotExpr.Take (dotExpr.Count - 2)),
               TypeName = dotExpr[dotExpr.Count - 2],
               MemberName = dotExpr[dotExpr.Count - 1]
            };
        }

#line default
   static readonly short [] yyLhs  = {              -1,
    0,    0,    0,    0,    0,    0,    0,    6,    6,    2,
    1,    7,    9,    9,    8,    8,    8,   12,   12,   10,
   10,   13,   13,   11,   11,    3,    3,   15,   15,   15,
    4,    5,   14,   14,
  };
   static readonly short [] yyLen = {           2,
    3,    3,    3,    3,    3,    3,    3,    1,    3,    1,
    2,    4,    0,    2,    0,    2,    3,    1,    3,    0,
    3,    0,    2,    0,    2,    5,    3,    0,    1,    3,
    1,    1,    0,    3,
  };
   static readonly short [] yyDefRed = {            0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    1,    0,    2,   10,
    0,    3,    0,    4,   31,   32,    5,    6,    7,    0,
    0,    0,   11,    0,    0,    0,    9,   18,    0,   16,
    0,    0,    0,    0,   27,    0,   17,   14,    0,    0,
    0,    0,    0,   19,    0,    0,    0,   12,   26,    0,
   34,   23,   21,   25,   30,
  };
  protected static readonly short [] yyDgoto  = {             8,
   21,   19,   22,   24,   27,   18,   33,   34,   42,   50,
   58,   39,   56,   45,   53,
  };
  protected static readonly short [] yySindex = {          -51,
 -262, -251, -248, -247, -245, -241, -232,    0, -250, -250,
 -250, -250, -250, -250, -250, -229,    0, -254,    0,    0,
 -226,    0, -254,    0,    0,    0,    0,    0,    0, -250,
 -250, -224,    0, -227, -219, -260,    0,    0, -256,    0,
 -250, -225, -254, -250,    0, -250,    0,    0, -221, -217,
 -222, -214, -213,    0, -221, -228, -202,    0,    0, -250,
    0,    0,    0,    0,    0,
  };
  protected static readonly short [] yyRindex = {            0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    1,    0,   13,    0,    0,
    0,    0,   20,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,   25,    0,   32,    0,    0,    0,    0,
    0,   37,    4, -210,    0,    0,    0,    0, -211,   46,
   60, -207,    0,    0, -211,    0,    0,    0,    0, -210,
    0,    0,    0,    0,    0,
  };
  protected static readonly short [] yyGindex = {            0,
   -3,    0,   49,    9,    0,   38,    0,  -21,    0,    0,
    0,    0,    8,   14,    6,
  };
  protected static readonly short [] yyTable = {             9,
    8,   36,   41,   15,   46,   17,   44,   16,   47,   31,
   10,   32,   15,   11,   12,    5,   13,    7,    4,   15,
   14,   51,   28,   29,   13,    3,    2,   38,    6,   15,
   30,   33,    1,   35,   40,   41,   20,   48,   43,   55,
   52,   63,   54,   49,   44,   24,   60,   20,   23,   25,
   23,   25,   25,   57,   61,   64,   52,   28,   22,   33,
   29,   26,   62,    0,   59,   65,    0,   37,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    8,    0,    8,    8,    8,    8,    8,    8,    8,
   15,    8,   15,   15,    0,   15,    0,   15,    0,   15,
   15,   15,   15,   15,   13,   13,   15,    0,   15,   13,
   15,   13,   13,   13,    0,   13,   20,   20,    0,    0,
   13,   20,   13,    0,   20,   24,   24,   20,    0,    0,
   24,    0,    0,   24,   24,
  };
  protected static readonly short [] yyCheck = {           262,
    0,   23,  263,    0,  261,    9,  267,  258,  265,  264,
  262,  266,    0,  262,  262,   67,  262,   69,   70,    0,
  262,   43,   14,   15,    0,   77,   78,   31,   80,  262,
  260,    0,   84,  260,  259,  263,    0,   41,  258,  261,
   44,  270,   46,  269,  267,    0,  261,   10,   11,   12,
   13,   14,   15,  271,  268,  258,   60,  268,  270,    0,
  268,   13,   55,   -1,   51,   60,   -1,   30,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,  261,   -1,  263,  264,  265,  266,  267,  268,  269,
  267,  271,  260,  261,   -1,  263,   -1,  265,   -1,  260,
  268,  269,  263,  271,  260,  261,  267,   -1,  269,  265,
  271,  260,  268,  269,   -1,  271,  260,  261,   -1,   -1,
  269,  265,  271,   -1,  268,  260,  261,  271,   -1,   -1,
  265,   -1,   -1,  268,  269,
  };

#line 203 "Monkeydoc.Ecma/EcmaUrlParser.jay"

}
#line default
namespace yydebug {
        using System;
	 internal interface yyDebug {
		 void push (int state, Object value);
		 void lex (int state, int token, string name, Object value);
		 void shift (int from, int to, int errorFlag);
		 void pop (int state);
		 void discard (int state, int token, string name, Object value);
		 void reduce (int from, int to, int rule, string text, int len);
		 void shift (int from, int to);
		 void accept (Object value);
		 void error (string message);
		 void reject ();
	 }
	 
	 class yyDebugSimple : yyDebug {
		 void println (string s){
			 Console.Error.WriteLine (s);
		 }
		 
		 public void push (int state, Object value) {
			 println ("push\tstate "+state+"\tvalue "+value);
		 }
		 
		 public void lex (int state, int token, string name, Object value) {
			 println("lex\tstate "+state+"\treading "+name+"\tvalue "+value);
		 }
		 
		 public void shift (int from, int to, int errorFlag) {
			 switch (errorFlag) {
			 default:				// normally
				 println("shift\tfrom state "+from+" to "+to);
				 break;
			 case 0: case 1: case 2:		// in error recovery
				 println("shift\tfrom state "+from+" to "+to
					     +"\t"+errorFlag+" left to recover");
				 break;
			 case 3:				// normally
				 println("shift\tfrom state "+from+" to "+to+"\ton error");
				 break;
			 }
		 }
		 
		 public void pop (int state) {
			 println("pop\tstate "+state+"\ton error");
		 }
		 
		 public void discard (int state, int token, string name, Object value) {
			 println("discard\tstate "+state+"\ttoken "+name+"\tvalue "+value);
		 }
		 
		 public void reduce (int from, int to, int rule, string text, int len) {
			 println("reduce\tstate "+from+"\tuncover "+to
				     +"\trule ("+rule+") "+text);
		 }
		 
		 public void shift (int from, int to) {
			 println("goto\tfrom state "+from+" to "+to);
		 }
		 
		 public void accept (Object value) {
			 println("accept\tvalue "+value);
		 }
		 
		 public void error (string message) {
			 println("error\t"+message);
		 }
		 
		 public void reject () {
			 println("reject");
		 }
		 
	 }
}
// %token constants
 class Token {
  public const int ERROR = 257;
  public const int IDENTIFIER = 258;
  public const int DIGIT = 259;
  public const int DOT = 260;
  public const int COMMA = 261;
  public const int COLON = 262;
  public const int INNER_TYPE_SEPARATOR = 263;
  public const int OP_GENERICS_LT = 264;
  public const int OP_GENERICS_GT = 265;
  public const int OP_GENERICS_BACKTICK = 266;
  public const int OP_OPEN_PAREN = 267;
  public const int OP_CLOSE_PAREN = 268;
  public const int OP_ARRAY_OPEN = 269;
  public const int OP_ARRAY_CLOSE = 270;
  public const int SLASH_SEPARATOR = 271;
  public const int yyErrorCode = 256;
 }
 namespace yyParser {
  using System;
  /** thrown for irrecoverable syntax errors and stack overflow.
    */
  internal class yyException : System.Exception {
    public yyException (string message) : base (message) {
    }
  }
  internal class yyUnexpectedEof : yyException {
    public yyUnexpectedEof (string message) : base (message) {
    }
    public yyUnexpectedEof () : base ("") {
    }
  }

  /** must be implemented by a scanner object to supply input to the parser.
    */
  internal interface yyInput {
    /** move on to next token.
        @return false if positioned beyond tokens.
        @throws IOException on input error.
      */
    bool advance (); // throws java.io.IOException;
    /** classifies current token.
        Should not be called if advance() returned false.
        @return current %token or single character.
      */
    int token ();
    /** associated with current token.
        Should not be called if advance() returned false.
        @return value for token().
      */
    Object value ();
  }
 }
} // close outermost namespace, that MUST HAVE BEEN opened in the prolog
