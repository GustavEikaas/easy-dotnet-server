namespace EasyDotnet.IDE.Models.Client.Quickfix;

public sealed record SetQuickFixRequest(QuickFixRequestType QuickFixRequestType, QuickFixItem[] QuickFixItems);

public enum QuickFixRequestType
{
  MsBuildDiagnostic = 0
}

public sealed record QuickFixItem(string FileName, int LineNumber, int ColumnNumber, string Text, QuickFixItemType Type);

public enum QuickFixItemType
{
  Information = 0,
  Warning = 1,
  Error = 2,
}