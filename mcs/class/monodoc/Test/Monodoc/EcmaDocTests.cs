using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

using NUnit.Framework;

using Monodoc;
using Monodoc.Generators;

namespace MonoTests.Monodoc
{
	[TestFixture]
	public class EcmaDocTest
	{
		[Test]
		public void CountTypeGenericArgumentsTest ()
		{
			var ecmaDoc = Type.GetType ("Monodoc.Providers.EcmaDoc, monodoc, PublicKey=0738eb9f132ed756");
			var countTypeGenericArguments = (Func<string, int>)Delegate.CreateDelegate (typeof (Func<string, int>), ecmaDoc.GetMethod ("CountTypeGenericArguments"));

			Assert.AreEqual (0, countTypeGenericArguments ("T:System.String"), "#0a");
			Assert.AreEqual (0, countTypeGenericArguments ("T:String"), "#0b");
			Assert.AreEqual (0, countTypeGenericArguments ("String"), "#0c");

			Assert.AreEqual (1, countTypeGenericArguments ("T:System.Collections.Foo<T>"), "#1a");
			Assert.AreEqual (1, countTypeGenericArguments ("T:System.Foo<T>"), "#1b");
			Assert.AreEqual (1, countTypeGenericArguments ("T:Foo<T>"), "#1c");
			Assert.AreEqual (1, countTypeGenericArguments ("Foo<T>"), "#1d");

			Assert.AreEqual (2, countTypeGenericArguments ("T:System.Collections.Foo<T, U>"), "#2a");
			Assert.AreEqual (2, countTypeGenericArguments ("T:System.Foo<TKey, TValue>"), "#2b");
			Assert.AreEqual (2, countTypeGenericArguments ("T:Foo<Something,Else>"), "#2c");
			Assert.AreEqual (2, countTypeGenericArguments ("Foo<TDelegate,TArray>"), "#2d");

			Assert.AreEqual (3, countTypeGenericArguments ("T:System.Collections.Foo<T, U, V>"), "#3a");
			Assert.AreEqual (3, countTypeGenericArguments ("T:System.Foo<TKey, TValue, THash>"), "#3b");
			Assert.AreEqual (3, countTypeGenericArguments ("T:Foo<Something,Else,Really>"), "#3c");
			Assert.AreEqual (3, countTypeGenericArguments ("Foo<TDelegate,TArray,TEvent>"), "#3d");
		}
	}
}
