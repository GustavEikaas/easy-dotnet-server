using EasyDotnet.Domain.Models.Secrets;

namespace EasyDotnet.Application.Interfaces;

public interface IUserSecretsService
{
  Task<ProjectUserSecret> AddUserSecretsId(string projectPath);
}