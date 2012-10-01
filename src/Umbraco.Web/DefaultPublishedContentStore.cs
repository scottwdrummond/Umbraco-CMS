using System;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Umbraco.Core.Models;
using Umbraco.Web.Routing;
using umbraco;
using umbraco.NodeFactory;
using umbraco.interfaces;
using System.Linq;

namespace Umbraco.Web
{
	/// <summary>
    /// An IPublishedContentStore which uses the Xml cache system to return data
    /// </summary>
    internal class DefaultPublishedContentStore : IPublishedContentStore
    {

    	private IDocument ConvertToDocument(XmlNode xmlNode)
		{
			if (xmlNode == null)
				return null;

			return new Models.XmlDocument(xmlNode);
		}
		
    	public virtual IDocument GetDocumentById(UmbracoContext umbracoContext, int nodeId)
    	{
    		if (umbracoContext == null) throw new ArgumentNullException("umbracoContext");

    		return ConvertToDocument(GetXml(umbracoContext).GetElementById(nodeId.ToString()));
    	}

		public IDocument GetDocumentByRoute(UmbracoContext umbracoContext, string route, bool? hideTopLevelNode = null)
        {
			if (umbracoContext == null) throw new ArgumentNullException("umbracoContext");
			if (route == null) throw new ArgumentNullException("route");

			//set the default to be what is in the settings
			hideTopLevelNode = hideTopLevelNode ?? GlobalSettings.HideTopLevelNodeFromPath;

			//the route always needs to be lower case because we only store the urlName attribute in lower case
        	route = route.ToLowerInvariant();

			int pos = route.IndexOf('/');
			string path = pos == 0 ? route : route.Substring(pos);
			int startNodeId = pos == 0 ? 0 : int.Parse(route.Substring(0, pos));

            var xpath = CreateXpathQuery(startNodeId, path, hideTopLevelNode.Value);

			//check if we can find the node in our xml cache
			var found = GetXml(umbracoContext).SelectSingleNode(xpath);

			// if hideTopLevelNodePath is true then for url /foo we looked for /*/foo
			// but maybe that was the url of a non-default top-level node, so we also
			// have to look for /foo (see note in NiceUrlProvider).
			if (found == null && hideTopLevelNode.Value && path.Length > 1 && path.IndexOf('/', 1) < 0)
			{
				xpath = CreateXpathQuery(startNodeId, path, false);
				found = GetXml(umbracoContext).SelectSingleNode(xpath);
			}

        	return ConvertToDocument(found);
        }

		public IDocument GetDocumentByUrlAlias(UmbracoContext umbracoContext, int rootNodeId, string alias)
        {
			if (umbracoContext == null) throw new ArgumentNullException("umbracoContext");
			if (alias == null) throw new ArgumentNullException("alias");

			// the alias may be "foo/bar" or "/foo/bar"
            // there may be spaces as in "/foo/bar,  /foo/nil"
            // these should probably be taken care of earlier on

            alias = alias.TrimStart('/');
            var xpathBuilder = new StringBuilder();
            xpathBuilder.Append("/root");
            if (rootNodeId > 0)
                xpathBuilder.AppendFormat("//*[@isDoc and @id={0}]", rootNodeId);
            xpathBuilder.Append("//*[@isDoc and (");
            xpathBuilder.AppendFormat("contains(concat(',',translate(umbracoUrlAlias, ' ', ''),','),',{0},')", alias);
            xpathBuilder.AppendFormat(" or contains(concat(',',translate(umbracoUrlAlias, ' ', ''),','),',/{0},')", alias);
            xpathBuilder.Append(")]");

            var xpath = xpathBuilder.ToString();

			return ConvertToDocument(GetXml(umbracoContext).SelectSingleNode(xpath));
        }

        public bool HasContent(UmbracoContext umbracoContext)
        {
            const string xpath = "/root/*[@isDoc]";
            var node = GetXml(umbracoContext).SelectSingleNode(xpath);
            return node != null;
        }

		XmlDocument GetXml(UmbracoContext umbracoContext)
		{
			if (umbracoContext == null) throw new ArgumentNullException("umbracoContext");

			return umbracoContext.GetXml();
		}

		static readonly char[] SlashChar = new char[] { '/' };

        protected string CreateXpathQuery(int startNodeId, string path, bool hideTopLevelNodeFromPath)
        {
            string xpath;

            if (path == string.Empty || path == "/")
            {
                // if url is empty
                if (startNodeId > 0)
                {
                    // if in a domain then use the root node of the domain
                    xpath = string.Format("/root//*[@isDoc and @id={0}]", startNodeId);
                }
                else
                {
                    // if not in a domain - what is the default page?
                    // let's say it is the first one in the tree, if any -- order by sortOrder

					// but!
					// umbraco does not consistently guarantee that sortOrder starts with 0
					// so the one that we want is the one with the smallest sortOrder
					// read http://stackoverflow.com/questions/1128745/how-can-i-use-xpath-to-find-the-minimum-value-of-an-attribute-in-a-set-of-elemen
                    
					// so that one does not work, because min(@sortOrder) maybe 1
					// xpath = "/root/*[@isDoc and @sortOrder='0']";

					// and we can't use min() because that's XPath 2.0
					// that one works
					xpath = "/root/*[@isDoc and not(@sortOrder > ../*[@isDoc]/@sortOrder)][1]";
                }
            }
            else
            {
                // if url is not empty, then use it to try lookup a matching page
                var urlParts = path.Split(SlashChar, StringSplitOptions.RemoveEmptyEntries);
                var xpathBuilder = new StringBuilder();
                int partsIndex = 0;

                xpathBuilder.Append("/root");
				
                if (startNodeId == 0)
                {
                    if (hideTopLevelNodeFromPath)
                        xpathBuilder.Append("/*[@isDoc]"); // first node is not in the url
                }
                else
                {
                    xpathBuilder.AppendFormat("//*[@isDoc and @id={0}]", startNodeId);
					// always "hide top level" when there's a domain
                }
                while (partsIndex < urlParts.Length)
                    xpathBuilder.AppendFormat("/*[@isDoc and @urlName='{0}']", urlParts[partsIndex++]);

                xpath = xpathBuilder.ToString();
            }

            return xpath;
        }
    }
}