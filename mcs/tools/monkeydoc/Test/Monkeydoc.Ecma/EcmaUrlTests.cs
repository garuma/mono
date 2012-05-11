using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using NUnit.Framework;

using MonkeyDoc;
using Monkeydoc.Ecma;

namespace MonoTests.MonkeyDoc.Ecma
{
	[TestFixture]
	public class EcmaUrlTests
	{
		EcmaUrlParser parser;

		[SetUp]
		public void Setup ()
		{
			parser = new EcmaUrlParser ();
		}
		
		void AssertValidUrl (string url)
		{
			try {
				parser.IsValid (url);
			} catch {
				Assert.Fail (string.Format ("URL '{0}' deemed not valid", url));
			}
		}

		void AssertInvalidUrl (string url)
		{
			try {
				parser.IsValid (url);
			} catch {
				return;
			}
			Assert.Fail (string.Format ("URL '{0}' deemed valid", url));
		}

		void AssertUrlDesc (EcmaDesc expected, string url)
		{
			EcmaDesc actual = null;
			try {
				actual = parser.Parse (url);
			} catch (Exception e) {
				Assert.Fail (string.Format ("URL '{0}' deemed not valid: {1}{2}", url, Environment.NewLine, e.ToString ()));
			}

			Assert.AreEqual (expected, actual, "Converted URL differs");
		}

		[Test]
		public void CommonMethodUrlIsValidTest ()
		{
			AssertValidUrl ("M:System.String.FooBar()");
			AssertValidUrl ("M:System.String.FooBar(System.String, Int32)");
			AssertValidUrl ("M:System.Foo.Int32<System.String+FooBar<System.Blop<T, U`2>>>.Foo()");
			AssertValidUrl ("M:System.Foo.Int32<System.String+FooBar<System.Blop<T, U`2>>>.Foo(Bleh,Bar)");
			AssertValidUrl ("M:System.Foo.Int32<System.String+FooBar<System.Blop<T, U`2>>>.Foo(Bleh<V>,Bar)");
		}

		[Test]
		public void CommonTypeUrlIsValidTest ()
		{
			AssertValidUrl ("T:Int32");
			AssertValidUrl ("T:System.Foo.Int32");
			AssertValidUrl ("T:System.Foo.Int32<System.String+FooBar`1>");
			AssertValidUrl ("T:System.Foo.Int32<System.String+FooBar<System.Blop<T, U>>>");
			AssertValidUrl ("T:System.Foo.Int32<T,U>");
			AssertValidUrl ("T:System.Foo.Int32<System.String+FooBar<System.Blop<T, U>>>");
			AssertValidUrl ("T:System.Foo.Int32<System.String+FooBar<System.Blop<T, U`2>>>");
		}

		[Test]
		public void CommonTypeUrlNotValidTest ()
		{
			AssertInvalidUrl ("TInt32");
			AssertInvalidUrl ("K:Int32");
			AssertInvalidUrl ("T:System..Foo.Int32");
			AssertInvalidUrl ("T:System.Foo.Int32<System.String+FooBar`1");
			AssertInvalidUrl ("T:System.Foo.Int32<System.String+FooBarSystem.Blop<T, U>>>");
			AssertInvalidUrl ("T:System.Foo.Int32<T,>");
			AssertInvalidUrl ("T:System.Foo.Int32<+FooBar<System.Blop<T, U>>>");
		}

		[Test]
		public void NamespaceValidTest ()
		{
			AssertValidUrl ("N:Foo.Bar");
			AssertValidUrl ("N:Foo");
			AssertValidUrl ("N:Foo.Bar.Baz");
			AssertValidUrl ("N:A.B.C");

			var ast = new EcmaDesc () { DescKind = EcmaDesc.Kind.Namespace,
			                            Namespace = "Foo.Bar.Blop" };
			AssertUrlDesc (ast, "N:Foo.Bar.Blop");
		}

		[Test]
		public void ConstructorValidTest ()
		{
			AssertValidUrl ("C:Gendarme.Rules.Concurrency.DecorateThreadsRule.DecorateThreadsRule");
			AssertValidUrl ("C:Gendarme.Rules.Concurrency.DecorateThreadsRule.DecorateThreadsRule()");
			AssertValidUrl ("C:Gendarme.Rules.Concurrency.DecorateThreadsRule.DecorateThreadsRule(System.String)");
		}

		[Test]
		public void MetaEtcNodeTest ()
		{
			AssertValidUrl ("T:Foo.Bar.Type/*");
			var ast = new EcmaDesc () { DescKind = EcmaDesc.Kind.Type,
			                            Namespace = "Foo.Bar",
			                            TypeName = "Type",
			                            Etc = '*' };
			AssertUrlDesc (ast, "T:Foo.Bar.Type/*");
		}

		[Test]
		public void SimpleTypeUrlParseTest ()
		{
			var ast = new EcmaDesc () { DescKind = EcmaDesc.Kind.Type,
			                            TypeName = "String",
			                            Namespace = "System" };
			AssertUrlDesc (ast, "T:System.String");
		}

		[Test]
		public void TypeWithOneGenericUrlParseTest ()
		{
			var generics = new[] {
				new EcmaDesc {
					DescKind = EcmaDesc.Kind.Type,
					TypeName = "T"
				}
			};
			var ast = new EcmaDesc () { DescKind = EcmaDesc.Kind.Type,
			                            TypeName = "String",
			                            Namespace = "System",
			                            GenericTypeArguments = generics,
			};

			AssertUrlDesc (ast, "T:System.String<T>");
		}

		[Test]
		public void TypeWithNestedGenericUrlParseTest ()
		{
			var generics = new[] {
				new EcmaDesc {
					DescKind = EcmaDesc.Kind.Type,
					TypeName = "T"
				},
				new EcmaDesc {
					DescKind = EcmaDesc.Kind.Type,
					Namespace = "System.Collections.Generic",
					TypeName = "List",
					GenericTypeArguments = new[] {
						new EcmaDesc {
							DescKind = EcmaDesc.Kind.Type,
							TypeName = "V"
						}
					}
				}
			};
			var ast = new EcmaDesc () { DescKind = EcmaDesc.Kind.Type,
			                            TypeName = "String",
			                            Namespace = "System",
			                            GenericTypeArguments = generics,
			};

			AssertUrlDesc (ast, "T:System.String<T, System.Collections.Generic.List<V>>");
		}

		[Test]
		public void SimpleMethodUrlParseTest ()
		{
			var ast = new EcmaDesc () { DescKind = EcmaDesc.Kind.Method,
			                            TypeName = "String",
			                            Namespace = "System",
			                            MemberName = "FooBar"
			};
			AssertUrlDesc (ast, "M:System.String.FooBar()");
		}

		[Test]
		public void MethodWithArgsUrlParseTest ()
		{
			var args = new[] {
				new EcmaDesc {
					DescKind = EcmaDesc.Kind.Type,
					Namespace = "System",
					TypeName = "String"
				},
				new EcmaDesc {
					DescKind = EcmaDesc.Kind.Type,
					TypeName = "Int32"
				}
			};
			var ast = new EcmaDesc () { DescKind = EcmaDesc.Kind.Method,
			                            TypeName = "String",
			                            Namespace = "System",
			                            MemberName = "FooBar",
			                            MemberArguments = args
			};
			AssertUrlDesc (ast, "M:System.String.FooBar(System.String, Int32)");
		}

		[Test]
		public void MethodWithArgsAndGenericsUrlParseTest ()
		{
			var args = new[] {
				new EcmaDesc {
					DescKind = EcmaDesc.Kind.Type,
					Namespace = "System",
					TypeName = "String"
				},
				new EcmaDesc {
					DescKind = EcmaDesc.Kind.Type,
					Namespace = "System.Collections.Generic",
					TypeName = "Dictionary",
					GenericTypeArguments = new[] {
						new EcmaDesc {
							DescKind = EcmaDesc.Kind.Type,
							TypeName = "K"
						},
						new EcmaDesc {
							DescKind = EcmaDesc.Kind.Type,
							TypeName = "V"
						}
					}
				}
			};

			var generics = new[] {
				new EcmaDesc {
					DescKind = EcmaDesc.Kind.Type,
					TypeName = "Action",
					GenericTypeArguments = new[] {
						new EcmaDesc {
							DescKind = EcmaDesc.Kind.Type,
							Namespace = "System",
							TypeName = "Single",
						},
						new EcmaDesc {
							DescKind = EcmaDesc.Kind.Type,
							TypeName = "int",
						},
					}
				}
			};

			var ast = new EcmaDesc () { DescKind = EcmaDesc.Kind.Method,
			                            TypeName = "String",
			                            Namespace = "System",
			                            MemberName = "FooBar",
			                            MemberArguments = args,
			                            GenericMemberArguments = generics
			};
			AssertUrlDesc (ast, "M:System.String.FooBar<Action<System.Single, int>>(System.String, System.Collections.Generic.Dictionary<K, V>)");
		}

		/*		[Test]
		public void TreeParsabilityTest ()
		{
			var rootTree = RootTree.LoadTree ("/home/jeremie/monodoc/");
			Node result;
			var generator = new CheckGenerator ();

			foreach (var leaf in GetLeaves (rootTree.RootNode).Where (IsEcmaNode))
				AssertUrl (leaf.PublicUrl);
		}

		IEnumerable<Node> GetLeaves (Node node)
		{
			if (node == null)
				yield break;

			if (node.IsLeaf)
				yield return node;
			else {
				foreach (var child in node.Nodes) {
					if (!string.IsNullOrEmpty (child.Element) && !child.Element.StartsWith ("root:/"))
						yield return child;
					foreach (var childLeaf in GetLeaves (child))
						yield return childLeaf;
				}
			}
		}

		bool IsEcmaNode (Node node)
		{
			var url = node.PublicUrl;
			return url != null && url.Length > 2 && url[1] == ':';
		}*/
	}
}