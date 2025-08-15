namespace EasyDotnet.MsBuild.Contracts;

//Represents either a .props or .target file imported by a project
public record MsBuildSource(string Path, DateTime MTime);