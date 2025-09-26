using System.Collections.Generic;
using EasyDotnet.Domain.Models.MsBuild.Build;

namespace EasyDotnet.IDE.Controllers.MsBuild;

public sealed record BuildResultResponse(bool Success, IAsyncEnumerable<BuildMessageWithProject> Errors, IAsyncEnumerable<BuildMessageWithProject> Warnings);