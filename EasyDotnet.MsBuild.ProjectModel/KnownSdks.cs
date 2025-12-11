namespace EasyDotnet.MsBuild.ProjectModel;

public record SdkInfo(string Name, string Description);

/// <summary>
/// Well-known MSBuild SDK identifiers.
/// </summary>
public static class KnownSdks
{
  public static IEnumerable<SdkInfo> GetAll() => typeof(KnownSdks)
      .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
      .Where(f => f.FieldType == typeof(SdkInfo))
      .Select(f => (SdkInfo)f.GetValue(null)!)
      .Where(sdk => sdk is not null);

  public static readonly SdkInfo MicrosoftNetSdk = new(
      Name: "Microsoft.NET.Sdk",
      Description: "The standard . NET SDK for console apps, class libraries, and web applications."
  );

  public static readonly SdkInfo MicrosoftNetSdkWeb = new(
      Name: "Microsoft.NET.Sdk.Web",
      Description: "SDK for ASP.NET Core web applications and APIs."
  );

  public static readonly SdkInfo MicrosoftNetSdkWorker = new(
      Name: "Microsoft.NET.Sdk.Worker",
      Description: "SDK for building background services and worker applications."
  );

  public static readonly SdkInfo MicrosoftNetSdkRazor = new(
      Name: "Microsoft.NET.Sdk.Razor",
      Description: "SDK for Razor class libraries."
  );

  public static readonly SdkInfo MicrosoftNetSdkBlazorWebAssembly = new(
      Name: "Microsoft.NET.Sdk.BlazorWebAssembly",
      Description: "SDK for Blazor WebAssembly applications."
  );

  public static readonly SdkInfo MSBuildSdkExtras = new(
      Name: "MSBuild.Sdk.Extras",
      Description: "Community SDK for multi-targeting legacy frameworks (WPF, UWP, Xamarin)."
  );
}