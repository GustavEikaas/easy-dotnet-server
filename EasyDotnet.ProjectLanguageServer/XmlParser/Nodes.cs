namespace EasyDotnet.ProjectLanguageServer.XmlParser;

public abstract record MsBuildXmlNode(string Name, MsBuildXmlNode? Parent = null);

public record ProjectNode(List<MsBuildXmlNode> Children) : MsBuildXmlNode("Project");

public record PropertyGroupNode(List<MsBuildPropertyNode> Properties) : MsBuildXmlNode("PropertyGroup");

public record ItemGroupNode(List<ItemNode> Items) : MsBuildXmlNode("ItemGroup");

public record MsBuildPropertyNode(string Name, string Value = "") : MsBuildXmlNode(Name);

public record ItemNode(string Name, Dictionary<string, string> Metadata) : MsBuildXmlNode(Name);
