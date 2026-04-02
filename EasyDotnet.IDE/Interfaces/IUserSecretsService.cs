using EasyDotnet.IDE.Models.Secrets;

namespace EasyDotnet.IDE.Interfaces;

public interface IUserSecretsService
{
  Task<ProjectUserSecret> AddUserSecretsId(string projectPath);
}