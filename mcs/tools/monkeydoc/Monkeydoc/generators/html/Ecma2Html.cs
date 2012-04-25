using System;
using System.IO;
using System.Xml;
using System.Xml.Xsl;
using System.Xml.XPath;

namespace MonkeyDoc.Generators.Html
{
	public class Ecma2Html : IHtmlExporter
	{
		static string css_ecma;
		static string js;
		static XslTransform ecma_transform;
		static XsltArgumentList args = new XsltArgumentList();

		public override string CssCode {
			get {
				if (css_ecma != null)
					return css_ecma;
				System.Reflection.Assembly assembly = typeof(EcmaHelpSource).Assembly;
				Stream str_css = assembly.GetManifestResourceStream ("mono-ecma.css");
				css_ecma = (new StreamReader (str_css)).ReadToEnd();
				return css_ecma;
			}
		}

		public string JsCode {
			get {
				if (js != null)
					return js;
				System.Reflection.Assembly assembly = typeof(EcmaHelpSource).Assembly;
				Stream str_js = assembly.GetManifestResourceStream ("helper.js");
				js = (new StreamReader (str_js)).ReadToEnd();
				return js;
			}
		}
		
		public string Htmlize (XmlReader ecma_xml)
		{
			return Htmlize(ecma_xml, null);
		}

		public string Htmlize (XmlReader ecma_xml, XsltArgumentList args)
		{
			EnsureTransform ();
		
			var output = new StringBuilder ();
			ecma_transform.Transform (ecma_xml, 
			                          args, 
			                          XmlWriter.Create (output, ecma_transform.OutputSettings),
			                          CreateDocumentResolver ());
			return output.ToString ();
		}

		public string Export (Stream stream)
		{
			return Htmlize (new XPathDocument (stream));
		}

		public string Export (string input)
		{
			return Htmlize (new XPathDocument (new StringReader (input)));
		}
	}
}