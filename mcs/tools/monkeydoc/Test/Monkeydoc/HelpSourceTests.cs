using System;
using System.Linq;
using System.Collections.Generic;

using NUnit.Framework;

using MonkeyDoc;

namespace MonoTests.MonkeyDoc
{
	[TestFixture]
	public class HelpSourceTest
	{
		class CheckGenerator : IDocGenerator<bool>
		{
			public bool Generate (HelpSource hs, string id)
			{
				if (hs == null || string.IsNullOrEmpty (id))
					return false;
				if (hs.IsRawContent (id))
					return hs.GetText (id) != null;

				return hs.IsGeneratedContent (id) ? hs.GetCachedText (id) != null : hs.GetCachedHelpStream (id) != null;
			}
		}

		/* This test verifies that for every node in our tree that possed a PublicUrl,
		 * we can correctly access it back through RenderUrl
		 */
		[Test]
		public void ReachabilityTest ()
		{
			var rootTree = RootTree.LoadTree ("/home/jeremie/monodoc/");
			Node result;
			var generator = new CheckGenerator ();

			foreach (var leaf in GetLeaves (rootTree.RootNode)) {
				Assert.IsTrue (rootTree.RenderUrl (leaf.PublicUrl, generator, out result), leaf.PublicUrl);
				Assert.IsTrue (leaf == result,
				               string.Format ("{0} != {1}?", leaf.Element, result.Element));
			}
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
	}
}