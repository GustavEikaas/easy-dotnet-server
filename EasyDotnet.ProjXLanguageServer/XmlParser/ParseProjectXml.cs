using System.Xml.Linq;

namespace EasyDotnet.ProjXLanguageServer.XmlParser;

public static class ProjectXml
{
  public static ProjectNode ParseProjectXml(string xml)
  {
    var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    var root = new ProjectNode([]);

    foreach (var child in doc.Root!.Elements())
    {
      switch (child.Name.LocalName)
      {
        case "PropertyGroup":
          var propGroup = new PropertyGroupNode([.. child.Elements().Select(p => new MsBuildPropertyNode(p.Name.LocalName, p.Value))]);
          root.Children.Add(propGroup);
          break;

        case "ItemGroup":
          var itemGroup = new ItemGroupNode([.. child.Elements().Select(i => new ItemNode(i.Name.LocalName, i.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value)))]);
          root.Children.Add(itemGroup);
          break;

        default:
          root.Children.Add(new MsBuildPropertyNode(child.Name.LocalName, child.Value));
          break;
      }
    }

    return root;
  }
}