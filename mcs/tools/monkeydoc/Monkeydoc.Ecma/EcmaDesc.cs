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

		// Returns the TypeName and the generic/inner type information if existing
		public string ToCompleteTypeName ()
		{
			var result = TypeName;
			if (GenericTypeArguments != null)
				result += "<" + string.Join (",", GenericTypeArguments.Select (t => t.ToCompleteTypeName ())) + ">";
			if (NestedType != null)
				result += "+" + NestedType.ToCompleteTypeName ();
			if (ArrayDimension > 0)
				result += "[" + new string (',', ArrayDimension - 1) + "]";

			return result;
		}

		// Returns the member name with its generic types if existing
		public string ToCompleteMemberName (Format format)
		{
			var result = MemberName;
			if (GenericMemberArguments != null)
				result += "<" + string.Join (",", GenericMemberArguments.Select (t => t.ToString ())) + ">";
			if (format == Format.WithArgs)
				return result;
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
			return string.Format ("({8}) {0}::{1}{2}{3}{7} {4}{5}{6}",
			                      Namespace,
			                      TypeName,
			                      GenericTypeArguments != null ? "<" + string.Join (",", GenericTypeArguments.Select (t => t.ToString ())) + ">" : string.Empty,
			                      NestedType != null ? "+" + NestedType.ToString () : string.Empty,
			                      MemberName ?? string.Empty,
			                      GenericMemberArguments != null ? "<" + string.Join (",", GenericMemberArguments.Select (t => t.ToString ())) + ">" : string.Empty,
			                      MemberArguments != null ? "(" + string.Join (",", MemberArguments.Select (m => m.ToString ())) + ")" : string.Empty,
			                      ArrayDimension > 0 ? "[" + new string (',', ArrayDimension - 1) + "]" : string.Empty,
			                      DescKind.ToString ()[0]);
			                      
		}

		public override bool Equals (object other)
		{
			var otherDesc = other as EcmaDesc;
			return otherDesc != null && Equals (otherDesc);
		}

		public bool Equals (EcmaDesc other)
		{
			return What (DescKind == other.DescKind)
				&& TypeName == other.TypeName
				&& Namespace == other.Namespace
				&& MemberName == other.MemberName
				&& NestedType == other.NestedType
				&& ArrayDimension == other.ArrayDimension
				&& (GenericTypeArguments == null || GenericTypeArguments.SequenceEqual (other.GenericTypeArguments))
				&& (GenericMemberArguments == null || GenericMemberArguments.SequenceEqual (other.GenericMemberArguments))
				&& (MemberArguments == null || MemberArguments.SequenceEqual (other.MemberArguments));
		}

		bool What (bool input)
		{
			if (!input)
				throw new Exception ("Not equal");
			return input;
		}
	}
}