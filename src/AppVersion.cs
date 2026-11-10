using System.Reflection;

[assembly: AssemblyTitle("曳尔沐桌宠")]
[assembly: AssemblyVersion(ElymsPet.AppVersion.Value + ".0")]
[assembly: AssemblyFileVersion(ElymsPet.AppVersion.Value + ".0")]

namespace ElymsPet {
 public static class AppVersion {
  public const string Value = "2.0.13";
  public const string UserAgent = "ElymsDesktopPet/" + Value;
 }
}
