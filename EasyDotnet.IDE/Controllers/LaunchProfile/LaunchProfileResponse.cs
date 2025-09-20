namespace EasyDotnet.IDE.Controllers.LaunchProfile;

public sealed record LaunchProfileResponse(string Name, Domain.Models.LaunchProfile.LaunchProfile Value);
