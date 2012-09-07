using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace Monkeydoc.Ecma
{
	/* Some properties might not be filled/meaningful depending on kind
	 * like a namespace EcmaUrl won't have a valid TypeName
	 */
	public class EcmaDesc : IEquatable<EcmaDesc>
	{
		public enum Kind
		{
			Type,
			Constructor,
			Method,
			Namespace,
			Field,
			Property,
			Event,
			Operator
		}

		public enum Mod
		{
			Normal,
			Pointer,
			Ref,
			Out
		}

		public enum Format
		{
			WithArgs,
			WithoutArgs
		}

		public Kind DescKind {
			get;
			set;
		}

		public Mod DescModifier {
			get;
			set;
		}

		public string Namespace {
			get;
			set;
		}

		public string TypeName {
			get;
			set;
		}

		public string MemberName {
			get;
			set;
		}

		public EcmaDesc NestedType {
			get;
			set;
		}

		public int ArrayDimension {
			get;
			set;
		}

		/* Depending on the form of the url, we might not have the type
		 * of the argument but only how many the type/member has i.e.
		 * when such number is specified with a backtick
		 */
		public IList<EcmaDesc> GenericTypeArguments {
			get;
			set;
		}

		public IList<EcmaDesc> GenericMemberArguments {
			get;
			set;
		}

		public IList<EcmaDesc> MemberArguments {
			get;
			set;
		}

		/* This indicates that we actually want an inner part of the ecmadesc
		 * i.e. in case of T: we could want the members (*), ctor (C), methods (M), ...
		 */
		public char Etc {
			get;
			set;
		}

		public bool IsEtc {
			get {
				return Etc != (char)0;
			}
		}

		/* EtcFilter is only valid in some case of IsEtc when the inner part needs
		 * to be further filtered e.g. in case we want a listing of the type overloads
		 * Equals
		 */
		public string EtcFilter {
			get;
			set;
		}

		/* When a member is an explicit implementation of an interface member, we register
		 * the member EcmaDesc with its interface parent here
		 */
		public EcmaDesc ExplicitImplMember {
			get;
			set;
		}

		// Returns the TypeName and the generic/inner type information if existing
		public string ToCompleteTypeName ()
		{
			var result = TypeName;
			if (GenericTypeArguments != null)
				result += FormatGenericArgs (GenericTypeArguments);
			if (NestedType != null)
				result += "." + NestedType.ToCompleteTypeName ();
			if (ArrayDimension > 0)
				result += "[" + new string (',', ArrayDimension - 1) + "]";

			return result;
		}

		// Returns the member name with its generic types if existing
		public string ToCompleteMemberName (Format format)
		{
			/* We special process two cases:
			 *   - Explicit member implementation which append a full type specification
			 *   - Conversion operator which are exposed as normal method but have specific captioning in the end
			 */
			if (ExplicitImplMember != null) {
				var impl = ExplicitImplMember;
				return impl.Namespace + "." + impl.TypeName + "." + impl.ToCompleteMemberName (format);
			} else if (format == Format.WithArgs && DescKind == Kind.Operator && MemberName == "Conversion") {
				var type1 = MemberArguments[0].ToCompleteTypeName ();
				var type2 = MemberArguments[1].ToCompleteTypeName ();
				return string.Format ("{0} to {1}", type1, type2);
			}

			var result = IsEtc && !string.IsNullOrEmpty (EtcFilter) ? EtcFilter : MemberName;

			if (GenericMemberArguments != null)
				result += FormatGenericArgs (GenericMemberArguments);

			if (format == Format.WithArgs) {
				result += '(';
				if (MemberArguments != null && MemberArguments.Count > 0) {
					var args = MemberArguments.Select (a => FormatNamespace (a) + a.ToCompleteTypeName () + ModToString (a));
					result += string.Join (",", args);
				}
				result += ')';
			}
			return result;
		}

		public string ToEcmaCref ()
		{
			var sb = new StringBuilder ();
			// Cref type
			sb.Append (DescKind.ToString ()[0]);
			// Create the rest
			ConstructCRef (sb);

			return sb.ToString ();
		}

		void ConstructCRef (StringBuilder sb)
		{
			sb.Append (Namespace);
			if (DescKind == Kind.Namespace)
				return;

			sb.Append ('.');
			sb.Append (TypeName);
			if (GenericTypeArguments != null) {
				sb.Append ('<');
				foreach (var t in GenericTypeArguments)
					t.ConstructCRef (sb);
				sb.Append ('>');
			}
			if (NestedType != null) {
				sb.Append ('+');
				NestedType.ConstructCRef (sb);
			}
			if (ArrayDimension > 0) {
				sb.Append ('[');
				sb.Append (new string (',', ArrayDimension - 1));
				sb.Append (']');
			}
			if (DescKind == Kind.Type)
				return;

			if (MemberArguments != null) {
				
			}
		}

		public override string ToString ()
		{
			return string.Format ("({8}) {0}::{1}{2}{3}{7} {4}{5}{6} {9}",
			                      Namespace,
			                      TypeName,
			                      FormatGenericArgsFull (GenericTypeArguments),
			                      NestedType != null ? "+" + NestedType.ToString () : string.Empty,
			                      MemberName ?? string.Empty,
			                      FormatGenericArgsFull (GenericMemberArguments),
			                      MemberArguments != null ? "(" + string.Join (",", MemberArguments.Select (m => m.ToString ())) + ")" : string.Empty,
			                      ArrayDimension > 0 ? "[" + new string (',', ArrayDimension - 1) + "]" : string.Empty,
			                      DescKind.ToString ()[0],
			                      Etc != 0 ? '(' + Etc.ToString () + ')' : string.Empty);
			                      
		}

		public override bool Equals (object other)
		{
			var otherDesc = other as EcmaDesc;
			return otherDesc != null && Equals (otherDesc);
		}

		public bool Equals (EcmaDesc other)
		{
			return DescKind == other.DescKind
				&& TypeName == other.TypeName
				&& Namespace == other.Namespace
				&& MemberName == other.MemberName
				&& NestedType == other.NestedType || NestedType.Equals (other.NestedType)
				&& ArrayDimension == other.ArrayDimension
				&& (GenericTypeArguments == null || GenericTypeArguments.SequenceEqual (other.GenericTypeArguments))
				&& (GenericMemberArguments == null || GenericMemberArguments.SequenceEqual (other.GenericMemberArguments))
				&& (MemberArguments == null || MemberArguments.SequenceEqual (other.MemberArguments))
				&& Etc == other.Etc
				&& EtcFilter == other.EtcFilter
				&& (ExplicitImplMember == null || ExplicitImplMember.Equals (other.ExplicitImplMember));
		}

		bool What (bool input)
		{
			if (!input)
				throw new Exception ("Not equal");
			return input;
		}

		string FormatNamespace (EcmaDesc desc)
		{
			return string.IsNullOrEmpty (desc.Namespace) ? string.Empty : desc.Namespace + ".";
		}

		string FormatGenericArgs (IEnumerable<EcmaDesc> genericArgs)
		{
			return genericArgs != null ? "<" + string.Join (",", genericArgs.Select (t => FormatNamespace (t) + t.ToCompleteTypeName ())) + ">" : string.Empty;
		}

		string FormatGenericArgsFull (IEnumerable<EcmaDesc> genericArgs)
		{
			return genericArgs != null ? "<" + string.Join (",", genericArgs.Select (t => t.ToString ())) + ">" : string.Empty;
		}

		string ModToString (EcmaDesc desc)
		{
			switch (desc.DescModifier) {
			case Mod.Pointer:
				return "*";
			case Mod.Ref:
				return "&";
			case Mod.Out:
				return "@";
			default:
				return string.Empty;
			}
		}
	}
}