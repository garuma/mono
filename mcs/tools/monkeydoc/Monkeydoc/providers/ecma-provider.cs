//
// The ecmaspec provider is for ECMA specifications
//
// Authors:
//	John Luke (jluke@cfl.rr.com)
//	Ben Maurer (bmaurer@users.sourceforge.net)
//
// Use like this:
//   mono assembler.exe --ecmaspec DIRECTORY --out name
//

using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;

using Mono.Lucene.Net.Index;
using Mono.Lucene.Net.Documents;

using MonkeyDoc.Ecma;

namespace MonkeyDoc.Providers
{
	public enum EcmaNodeType {
		Invalid,
		Namespace,
		Type,
		Member,
		Meta, // A node that's here to serve as a header for other node
	}

	public class EcmaProvider : Provider
	{
		string baseDir;

		public EcmaProvider (string baseDir)
		{
			this.baseDir = baseDir;
		}

		public abstract void PopulateTree (Tree tree)
		{
			var root = tree.RootNode;
			var storage = tree.HelpSource.Storage;
			int resID = 0;

			foreach (var asm in Directory.EnumerateDirectories (baseDir)) {
				using (var reader = XmlReader.Create (File.OpenRead (Path.Combine (asm, "index.xml")))) {
					reader.ReadToFollowing ("Types");
					var types = XElement.Load (reader.ReadSubtree ());

					foreach (var ns in types.Elements ("Namespace")) {
						var nsNode = root.GetOrCreateNode (ns.Attribute ("Name").Value, "N:" + ns.Attribute ("Name").Value);

						foreach (var type in ns.Elements ("Type")) {
							// Add the XML file corresponding to the type to our storage
							var id = resID++;
							using (var file = File.OpenRead (Path.Combine (asm, nsNode.Caption, type.Attribute ("Name").Value)))
								hs.Storage.Store (id.ToString (), file);

							var url = "ecma:" + id + type.Attribute ("Name").Value;
							var typeNode = nsNode.CreateNode ((string)(type.Attribute ("DisplayName") ?? type.Attribute ("Name")),
							                                  url);

							// Add meta "Members" node
							typeNode.CreateNode ("Members", "*");
							var members = type.Element ("Members").Elements ("Member").ToLookup (m => m.Element ("MemberType").Value);
							foreach (var memberType in members) {
								// We pluralize the member type to get the caption and take the first letter as URL
								var node = typeNode.CreateNode (memberType.Key + 's', memberType.Key[0]);
								int memberIndex = 0;
								// We do not escape much member name here
								foreach (var member in memberType)
									node.CreateNode (MakeMemberCaption (member), (memberType++).ToString ());
							}
						}

						nsNode.Sort ();
					}

					root.Sort ();
				}
			}
		}

		string MakeMemberCaption (XElement member)
		{
			var caption = (string)member.Attribute ("MemberName");
			var args = member.Element ("Parameters");
			if (args != null) {
				caption += '(';
				caption += args.Elements ("Parameter")
				               .Select (p => (string)p.Attribute ("Type"))
				               .Aggregate ((p1, p2) => p1 + "," + p2);
				caption += ')';
			}
			
			return caption;
		}

		public abstract void CloseTree (HelpSource hs, Tree tree)
		{
			AddImages (hs);
			AddExtensionMethods (hs);
		}

		void AddEcmaXml (HelpSource hs)
		{
			var xmls = Directory.EnumerateDirectories (baseDir) // Assemblies
				.SelectMany (Directory.EnumerateDirectories) // Namespaces
				.SelectMany (Directory.EnumerateFiles)
				.Where (f => f.EndsWith (".xml")); // Type XML files

			int resID = 0;
			foreach (var xml in xmls)
				using (var file = File.OpenRead (xml))
					hs.Storage.Store ((resID++).ToString (), file);
		}

		void AddImages (HelpSource hs)
		{
			var imgs = Directory.EnumerateDirectories (baseDir)
				.Select (d => Path.Combine (d, "_images"))
				.Where (Path.Exists)
				.SelectMany (Directory.EnumerateFiles);

			foreach (var img in imgs)
				using (var file = File.OpenRead (img))
					hs.Storage.Store (Path.GetFileName (img), file);
		}

		void AddExtensionMethods (HelpSource hs)
		{
			var extensionMethods = Directory.EnumerateDirectories (baseDir)
				.SelectMany (d => Path.Combine (d, "index.xml"))
				.Where (File.Exists)
				.Select (f => {
					using (var file = File.OpenRead (f)) {
						var reader = XmlReader.Create (file);
						reader.ReadToFollowing ("ExtensionMethods");
						return reader.ReadInnerXml ();
					}
				});

			hs.Storage.Store ("ExtensionMethods.xml",
			                  "<ExtensionMethods>" + extensionMethods.Aggregate (string.Concat) + "</ExtensionMethods>");
		}

		IEnumerable<string> GetEcmaXmls ()
		{
			return Directory.EnumerateDirectories (baseDir) // Assemblies
				.SelectMany (Directory.EnumerateDirectories) // Namespaces
				.SelectMany (Directory.EnumerateFiles)
				.Where (f => f.EndsWith (".xml")); // Type XML files
		}
	}

	public class EcmaHelpSource : HelpSource
	{
		const string EcmaPrefix = "ecma:";
		EcmaUrlParser parser = new EcmaUrlParser ();

		public EcmaSpecHelpSource (string base_file, bool create) : base (base_file, create)
		{
		}

		protected override string UriPrefix {
			get {
				return EcmaPrefix;
			}
		}

		public override bool CanHandleUrl (string url)
		{
			if (url.Length > 2 && url[1] == ':') {
				switch (url[0]) {
				case 'T':
				case 'M':
				case 'C':
				case 'P':
				case 'E':
				case 'F':
					return true;
				}
			}
			return base.CanHandleUrl (url);
		}

		public override GetPublicUrl (Node node)
		{
			string url = string.Empty;
			var type = GetNodeType (node);
			switch (type) {
			case EcmaNodeType.Namespace:
				return node.Element; // A namespace node has already a well formated internal url
			case EcmaNodeType.Type:
				return MakeTypeNodeUrl (node);
			case EcmaNodeType.Meta:
				return MakeTypeNodeUrl (node) + GenerateMetaSuffix (node);
			case EcmaNodeType.Member:
				return GetNodeMemberTypeChar (node) + ':' + MakeTypeNodeUrl (GetNodeTypeParent (node)).Substring (2) + "." + node.Caption;
			default:
				return null;
			}
		}

		string MakeTypeNodeUrl (Node node)
		{
			// A Type node has a Element property of the form: 'ecma:{number}#{typename}/'
			var hashIndex = node.Element.IndexOf ('#');
			return "T:" + node.Parent.Caption + '.' + node.Element.Substring (hashIndex, node.Element.Length - hashIndex - 2);
		}

		EcmaNodeType GetNodeType (Node node)
		{
			// We guess the node type by checking the depth level it's at in the tree
			int level = GetNodeLevel (node);
			switch (level) {
			case 0:
				return EcmaNodeType.Namespace;
			case 1:
				return EcmaNodeType.Type;
			case 2:
				return EcmaNodeType.Meta;
			case 3: // Here it's either a member or, in case of overload, a meta
				return node.Nodes != null ? EcmaNodeType.Meta : EcmaNodeType.Member;
			case 4: // At this level, everything is necessarily a member
				return EcmaNodeType.Member;
			default:
				return EcmaNodeType.Invalid;
			}
		}

		int GetNodeLevel (Node node)
		{
			int i = 0;
			for (; node != null; i++)
				node = node.Parent;
			return i;
		}

		char GetNodeMemberTypeChar (Node node)
		{
			int level = GetNodeLevel (node);
			// Only methods can be under a meta node, so in case the member level is
			// deeper than normal (which indicate an overload meta), return 'M' directly
			return level == 3 ? node.Parent.Element[0] : 'M';
		}

		Node GetNodeTypeParent (Node node)
		{
			// Type nodes are always at level 2 so we just need to get there
			int level = 0;
			Node result = node;
			while (node != null) {
				level++;
				if (level > 2)
					result = result.Parent;
			}
			return level < 2 ? null : result;
		}

		string GenerateMetaSuffix (Node node)
		{
			string suffix = string.Empty;
			while (node != null)
				suffix = '/' + node.Element + suffix;
		}

		public override string GetInternalIdForUrl (string url, out Node node)
		{
			node = MatchNode (url);
			return node.GetInternalUrl ();
		}

		public override Node MatchNode (string url)
		{
			EcmaDesc desc;
			if (!parser.TryParse (url, out desc))
				return null;

			// Namespace search
			Node result = null;
			Node currentNode = Tree.RootNode;
			Node searchNode = new Node () { Caption = desc.Namespace };
			int index = currentNode.Nodes.BinarySearch (searchNode, EcmaGenericNodeComparer.Instance);
			if (index >= 0)
				result = currentNode.Nodes[index];
			if (desc.DescKind == EcmaDesc.Kind.Namespace || index < 0)
				return result;

			// Type search
			currentNode = result;
			result = null;
			searchNode.Caption = desc.ToCompleteTypeName ();
			index = currentNode.Nodes.BinarySearch (searchNode, EcmaTypeNodeComparer.Instance);
			if (index >= 0)
				result = currentNode.Nodes[index];
			if (desc.DescKind == EcmaDesc.Kind.Type || index < 0)
				return result;

			// Member selection
			currentNode = result;
			result = null;
			string memberCaption = GetCaptionForMemberKind (desc.DescKind);
			if (memberCaption == null)
				return result;
			searchNode.Caption = memberCaption;
			index = currentNode.Nodes.FindIndex (searchNode, EcmaGenericNodeComparer.Instance);
			if (index < 0)
				return null;
			currentNode = currentNode.Nodes[index];

			// Member search
			result = null;
			searchNode.Caption = desc.ToCompleteMemberName (EcmaDesc.Format.WithoutArgs);
			index = currentNode.Nodes.BinarySearch (searchNode, EcmaGenericNodeComparer.Instance);
			if (index < 0)
				return null;
			result = currentNode.Nodes[index];
			if (result.Nodes.Count == 0)
				return result;

			// Overloads search
			searchNode.Caption = desc.ToCompleteMemberName (EcmaDesc.Format.WithArgs);
			index = currentNode.Nodes.BinarySearch (searchNode, EcmaGenericNodeComparer.Instance);
			if (index < 0)
				return result;
			result = result.Nodes[index];
			
			return result;
		}

		// This comparer returns the answer straight from caption comparison
		class EcmaGenericNodeComparer : IComparer<Node>
		{
			public static readonly EcmaGenericNodeComparer Instance = new EcmaGenericNodeComparer ();

			public int Compare (Node n1, Node n2)
			{
				return n1.Caption.CompareTo (n2.Caption);
			}
		}

		// This comparer take into account the space in the caption
		class EcmaTypeNodeComparer : IComparer<Node>
		{
			public static readonly EcmaTypeNodeComparer Instance = new EcmaTypeNodeComparer ();

			public int Compare (Node n1, Node n2)
			{
				return Clear (n1.Caption).CompareTo (Clear ( n2.Caption));
			}

			string Clear (string caption)
			{
				int lastSpace = caption.LastIndexOf (' ');
				return lastSpace == -1 ? caption : caption.Substring (0, lastSpace);
			}
		}

		string GetCaptionForMemberKind (EcmaDesc.Kind kind)
		{
			switch (kind) {
			case EcmaDesc.Kind.Method:
				return "Methods";
			case EcmaDesc.Kind.Property:
				return "Properties";
			case EcmaDesc.Kind.Constructor:
				return "Constructors";
			case EcmaDesc.Kind.Field:
				return "Fields";
			case EcmaDesc.Kind.Event:
				return "Events";
			default:
				return null;
			}
		}
	}
}
